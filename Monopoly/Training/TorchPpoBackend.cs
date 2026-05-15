using System;
using System.Collections.Generic;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace TRAINING
{
    // TorchPpoBackend mirrors our sigmoid-activated MLP as a torch.nn
    // module, copies weights in/out of the host MLP class, and runs the
    // PPO clipped-objective update on a collected RolloutBatch.
    //
    // Network shape: both actor and critic are torch.nn.Sequential
    // modules constructed from the host MLP's layerSizes so weight
    // copying is a per-layer reshape. Linear's weight layout in PyTorch
    // is (out_features, in_features), which matches our MLP's flat
    // row-major (out, in) packing — no transpose needed.
    //
    // Action model: each binary decision dim is independent Bernoulli(Y[dim]).
    //   log p(a | s) = a * log(Y[dim]) + (1-a) * log(1 - Y[dim])
    //   entropy      = -Y[dim] log Y[dim] - (1-Y[dim]) log (1-Y[dim])
    //
    // Loss = clipped policy loss + 0.5 * value MSE - 0.01 * entropy.
    // Trains for PPO_EPOCHS passes over the full batch (no minibatching
    // here — batch is already in memory and the gradient step is cheap).
    public class TorchPpoBackend : IDisposable
    {
        public Module<Tensor, Tensor> actor;
        public Module<Tensor, Tensor> critic;
        public optim.Optimizer actorOpt;
        public optim.Optimizer criticOpt;

        public int[] actorShape;
        public int[] criticShape;

        public float clipEps = 0.2f;
        public float valueCoef = 0.5f;
        public float entropyCoef = 0.01f;
        public float learningRate = 3e-4f;
        public int epochs = 4;

        public TorchPpoBackend(int[] actorShape, int[] criticShape)
        {
            this.actorShape = actorShape;
            this.criticShape = criticShape;
            actor = BuildSequential(actorShape);
            critic = BuildSequential(criticShape);

            actorOpt = optim.Adam(actor.parameters(), lr: learningRate);
            criticOpt = optim.Adam(critic.parameters(), lr: learningRate);
        }

        // Build a sigmoid-on-every-layer MLP. Matches the host MLP class
        // so the threshold semantics NeuralPolicy expects keep working.
        private static Module<Tensor, Tensor> BuildSequential(int[] shape)
        {
            var layers = new List<Module<Tensor, Tensor>>();
            for (int L = 0; L < shape.Length - 1; L++)
            {
                layers.Add(Linear(shape[L], shape[L + 1]));
                layers.Add(Sigmoid());
            }
            return Sequential(layers.ToArray());
        }

        // Copy weights from the host MLP into the torch module. Called
        // before every Update — gradient steps mutate the torch params,
        // then WriteToMlp pushes them back to the host buffer.
        public void LoadFromMlp(MLP src, Module<Tensor, Tensor> dst, int[] shape)
        {
            using var _ = torch.no_grad();
            var named = new Dictionary<string, Tensor>();
            foreach (var (name, param) in dst.named_parameters())
            {
                named[name] = param;
            }

            // Sequential names its children "0", "1", "2", ...
            // For our [Linear, Sigmoid] pattern, Linear modules live at
            // indices 0, 2, 4, ... so layer L's Linear is index 2*L.
            for (int L = 0; L < shape.Length - 1; L++)
            {
                int rows = shape[L + 1];
                int cols = shape[L];
                int wBase = src.weightOffsets[L];
                int bBase = src.biasOffsets[L];

                float[] wFlat = new float[rows * cols];
                Array.Copy(src.parameters, wBase, wFlat, 0, rows * cols);
                float[] bFlat = new float[rows];
                Array.Copy(src.parameters, bBase, bFlat, 0, rows);

                Tensor wT = torch.tensor(wFlat, new long[] { rows, cols });
                Tensor bT = torch.tensor(bFlat, new long[] { rows });

                string wKey = (2 * L) + ".weight";
                string bKey = (2 * L) + ".bias";
                named[wKey].copy_(wT);
                named[bKey].copy_(bT);
            }
        }

        public void WriteToMlp(Module<Tensor, Tensor> src, MLP dst, int[] shape)
        {
            using var _ = torch.no_grad();
            var named = new Dictionary<string, Tensor>();
            foreach (var (name, param) in src.named_parameters())
            {
                named[name] = param;
            }

            for (int L = 0; L < shape.Length - 1; L++)
            {
                int rows = shape[L + 1];
                int cols = shape[L];
                int wBase = dst.weightOffsets[L];
                int bBase = dst.biasOffsets[L];

                string wKey = (2 * L) + ".weight";
                string bKey = (2 * L) + ".bias";

                float[] wFlat = named[wKey].cpu().data<float>().ToArray();
                float[] bFlat = named[bKey].cpu().data<float>().ToArray();
                Array.Copy(wFlat, 0, dst.parameters, wBase, rows * cols);
                Array.Copy(bFlat, 0, dst.parameters, bBase, rows);
            }
        }

        // Run the PPO update on a collected batch. Returns the final
        // (policy_loss, value_loss, entropy) for logging.
        public (float, float, float) Update(MLP actorMlp, MLP criticMlp, RolloutBatch batch)
        {
            LoadFromMlp(actorMlp, actor, actorShape);
            LoadFromMlp(criticMlp, critic, criticShape);

            // Flatten all trajectories into one giant batch. Skip any with
            // zero entries (rare, but happens on instant-bankruptcy seats).
            int N = 0;
            for (int t = 0; t < batch.trajectories.Count; t++) N += batch.trajectories[t].observations.Count;
            if (N == 0) return (0.0f, 0.0f, 0.0f);

            int obsDim = actorShape[0];
            float[] flatStates = new float[N * obsDim];
            long[] flatDims = new long[N];
            float[] flatActions = new float[N];
            float[] flatOldProbs = new float[N];
            float[] flatAdvantages = new float[N];
            float[] flatReturns = new float[N];

            int cursor = 0;
            foreach (var traj in batch.trajectories)
            {
                int T = traj.observations.Count;
                if (T == 0) continue;
                // Standardise per-trajectory advantage so it has zero mean
                // and unit variance — standard PPO trick to keep the
                // gradient well-scaled even when the reward signal is
                // sparse (one game = one terminal reward).
                float advMean = 0.0f, advVar = 0.0f;
                for (int i = 0; i < T; i++) advMean += traj.advantages[i];
                advMean /= T;
                for (int i = 0; i < T; i++) advVar += (traj.advantages[i] - advMean) * (traj.advantages[i] - advMean);
                advVar = (T > 1) ? advVar / (T - 1) : 1.0f;
                float advStd = (float)Math.Sqrt(advVar + 1e-8f);

                for (int i = 0; i < T; i++)
                {
                    Array.Copy(traj.observations[i], 0, flatStates, cursor * obsDim, obsDim);
                    flatDims[cursor] = traj.actionDims[i];
                    flatActions[cursor] = traj.actions[i];
                    flatOldProbs[cursor] = traj.probs[i];
                    flatAdvantages[cursor] = (traj.advantages[i] - advMean) / advStd;
                    flatReturns[cursor] = traj.returns[i];
                    cursor++;
                }
            }
            N = cursor;

            Tensor states = torch.tensor(flatStates, new long[] { N, obsDim });
            Tensor actionDims = torch.tensor(flatDims, new long[] { N });
            Tensor actions = torch.tensor(flatActions, new long[] { N });
            Tensor oldProbs = torch.tensor(flatOldProbs, new long[] { N });
            Tensor advantages = torch.tensor(flatAdvantages, new long[] { N });
            Tensor returns = torch.tensor(flatReturns, new long[] { N });

            float finalPolicyLoss = 0.0f, finalValueLoss = 0.0f, finalEntropy = 0.0f;

            // Old log-prob (constant across epochs).
            Tensor oldLogProb = BernoulliLogProb(oldProbs, actions);

            for (int epoch = 0; epoch < epochs; epoch++)
            {
                actorOpt.zero_grad();
                criticOpt.zero_grad();

                Tensor probsFull = actor.forward(states);                       // (N, 9)
                Tensor newProbsAtDim = probsFull.gather(1, actionDims.unsqueeze(-1)).squeeze(-1); // (N,)
                // Same clamp as RecordingPolicy to keep log-stable.
                newProbsAtDim = torch.clamp(newProbsAtDim, 1e-6f, 1.0f - 1e-6f);

                Tensor newLogProb = BernoulliLogProb(newProbsAtDim, actions);
                Tensor ratio = torch.exp(newLogProb - oldLogProb);

                Tensor surr1 = ratio * advantages;
                Tensor surr2 = torch.clamp(ratio, 1.0f - clipEps, 1.0f + clipEps) * advantages;
                Tensor policyLoss = -torch.min(surr1, surr2).mean();

                Tensor values = critic.forward(states).squeeze(-1);
                Tensor valueLoss = (values - returns).pow(2).mean();

                Tensor entropy = -(newProbsAtDim * torch.log(newProbsAtDim)
                                   + (1.0f - newProbsAtDim) * torch.log(1.0f - newProbsAtDim));
                Tensor entropyMean = entropy.mean();

                Tensor loss = policyLoss + valueCoef * valueLoss - entropyCoef * entropyMean;
                loss.backward();

                actorOpt.step();
                criticOpt.step();

                finalPolicyLoss = policyLoss.item<float>();
                finalValueLoss = valueLoss.item<float>();
                finalEntropy = entropyMean.item<float>();
            }

            WriteToMlp(actor, actorMlp, actorShape);
            WriteToMlp(critic, criticMlp, criticShape);

            return (finalPolicyLoss, finalValueLoss, finalEntropy);
        }

        // log Bernoulli(p; a) = a*log(p) + (1-a)*log(1-p), elementwise.
        private static Tensor BernoulliLogProb(Tensor p, Tensor a)
        {
            return a * torch.log(p) + (1.0f - a) * torch.log(1.0f - p);
        }

        public void Dispose()
        {
            actor?.Dispose();
            critic?.Dispose();
            actorOpt?.Dispose();
            criticOpt?.Dispose();
        }
    }
}
