using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TRAINING
{
    // PPO trainer for 4-player Monopoly self-play, wired to the TorchSharp
    // backend for the gradient step.
    //
    // Pipeline per Step():
    //   1. Collect ROLLOUT_GAMES self-play games where each seat uses a
    //      RecordingPolicy that samples binary actions from Bernoulli(Y[dim])
    //      and records (state, action_dim, action, prob, V(state)).
    //   2. Compute GAE-smoothed advantages and bootstrapped returns over
    //      each per-seat trajectory.
    //   3. Hand the batch to TorchPpoBackend.Update — which copies the MLP
    //      parameters into a torch module, runs the clipped objective with
    //      Adam for PPO_EPOCHS passes, and copies the updated weights back.
    //
    // Action model: only the five binary decisions (buy, mortgage, advance,
    // offer trade, accept trade) are learned via policy gradient. The
    // 3-way jail decision and the continuous outputs (auction bid, build /
    // sell counts) are taken deterministically through the actor MLP but
    // don't contribute to the policy loss. They still inherit weight
    // updates from the binary decisions because the trunk is shared.
    public class PpoTrainer : ITrainer
    {
        public string Name { get { return "ppo"; } }
        public int Generation { get { return generation; } }

        // Hyperparameters (mainstream PPO defaults). Game count is small
        // because each game generates a few hundred decisions × 4 seats —
        // even 16 games already gives a batch of ~10K transitions.
        public int ROLLOUT_GAMES = 16;
        public float GAMMA = 0.99f;
        public float LAMBDA = 0.95f;
        public float CLIP_RATIO = 0.2f;
        public int PPO_EPOCHS = 4;
        public float LEARNING_RATE = 3e-4f;

        public MLP policy;
        public MLP valueHead;
        private TorchPpoBackend backend;
        private int generation = 0;

        public void Initialise()
        {
            policy = MLP.CreateDefault();
            policy.InitialiseHe(RNG.instance.gen);

            valueHead = new MLP(new int[] { MONOPOLY.Projection.PACK_SIZE, 64, 1 });
            valueHead.InitialiseHe(RNG.instance.gen);

            BuildBackend();
            generation = 0;
        }

        private void BuildBackend()
        {
            backend?.Dispose();
            backend = new TorchPpoBackend(policy.layerSizes, valueHead.layerSizes)
            {
                clipEps = CLIP_RATIO,
                epochs = PPO_EPOCHS,
                learningRate = LEARNING_RATE,
            };
        }

        public void Step()
        {
            // 1. Collect rollouts using stochastic-action RecordingPolicy.
            RolloutBatch batch = CollectRollouts(ROLLOUT_GAMES);
            // 2. GAE advantages + returns.
            ComputeAdvantages(batch);
            // 3. Gradient update via TorchSharp; backend mutates the host MLPs in place.
            var (pLoss, vLoss, ent) = backend.Update(policy, valueHead, batch);

            float meanReward = 0.0f; int rewardSteps = 0;
            for (int t = 0; t < batch.trajectories.Count; t++)
            {
                var traj = batch.trajectories[t];
                for (int i = 0; i < traj.rewards.Count; i++) { meanReward += traj.rewards[i]; rewardSteps++; }
            }
            meanReward = rewardSteps > 0 ? meanReward / rewardSteps : 0.0f;

            Console.WriteLine("PPO gen " + generation
                + " N=" + batch.size
                + " pLoss=" + pLoss.ToString("0.0000")
                + " vLoss=" + vLoss.ToString("0.0000")
                + " entropy=" + ent.ToString("0.0000")
                + " meanReward=" + meanReward.ToString("0.0000"));

            generation++;
        }

        // Collect `games` self-play games. Each seat uses a RecordingPolicy
        // that samples binary actions stochastically and records per-decision
        // (state, action_dim, action, prob, value). The terminal reward is
        // attached after each game.
        protected RolloutBatch CollectRollouts(int games)
        {
            RolloutBatch batch = new RolloutBatch();
            for (int g = 0; g < games; g++)
            {
                var recorders = new MONOPOLY.RecordingPolicy[4];
                MONOPOLY.IPolicy[] policies = new MONOPOLY.IPolicy[4];
                for (int i = 0; i < 4; i++)
                {
                    recorders[i] = new MONOPOLY.RecordingPolicy(policy, valueHead);
                    policies[i] = recorders[i];
                }

                MONOPOLY.Board board = new MONOPOLY.Board(policies);
                MONOPOLY.Board.EOutcome outcome = MONOPOLY.Board.EOutcome.ONGOING;
                while (outcome == MONOPOLY.Board.EOutcome.ONGOING) outcome = board.Step();

                int winnerSeat = -1;
                switch (outcome)
                {
                    case MONOPOLY.Board.EOutcome.WIN1: winnerSeat = 0; break;
                    case MONOPOLY.Board.EOutcome.WIN2: winnerSeat = 1; break;
                    case MONOPOLY.Board.EOutcome.WIN3: winnerSeat = 2; break;
                    case MONOPOLY.Board.EOutcome.WIN4: winnerSeat = 3; break;
                }

                for (int i = 0; i < 4; i++)
                {
                    Trajectory t = recorders[i].Finalise(winnerSeat == i ? 1.0f : 0.0f);
                    t.seat = i;
                    batch.Add(t);
                }
            }
            return batch;
        }

        // GAE: A_t = sum_{l=0}^{T-t} (gamma*lambda)^l * delta_{t+l}
        //       where delta_t = r_t + gamma * V(s_{t+1}) - V(s_t)
        //       and returns_t = A_t + V(s_t).
        public void ComputeAdvantages(RolloutBatch batch)
        {
            for (int k = 0; k < batch.trajectories.Count; k++)
            {
                Trajectory t = batch.trajectories[k];
                int T = t.rewards.Count;
                t.advantages = new float[T];
                t.returns = new float[T];

                float gae = 0.0f;
                for (int i = T - 1; i >= 0; i--)
                {
                    float nextV = (i == T - 1) ? 0.0f : t.values[i + 1];
                    float delta = t.rewards[i] + GAMMA * nextV - t.values[i];
                    gae = delta + GAMMA * LAMBDA * gae;
                    t.advantages[i] = gae;
                    t.returns[i] = gae + t.values[i];
                }
            }
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(generation).Append('\n');
            for (int i = 0; i < policy.layerSizes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(policy.layerSizes[i]);
            }
            sb.Append('\n');
            for (int i = 0; i < valueHead.layerSizes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(valueHead.layerSizes[i]);
            }
            sb.Append('\n');
            for (int p = 0; p < policy.parameters.Length; p++) sb.Append(policy.parameters[p]).Append('\n');
            for (int p = 0; p < valueHead.parameters.Length; p++) sb.Append(valueHead.parameters[p]).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        public bool Load(string path)
        {
            if (!File.Exists(path)) return false;
            string[] lines = File.ReadAllLines(path);
            generation = int.Parse(lines[0]);

            int[] pShape = ParseShape(lines[1]);
            int[] vShape = ParseShape(lines[2]);

            policy = new MLP(pShape);
            valueHead = new MLP(vShape);

            int cursor = 3;
            for (int p = 0; p < policy.parameters.Length; p++) policy.parameters[p] = float.Parse(lines[cursor++]);
            for (int p = 0; p < valueHead.parameters.Length; p++) valueHead.parameters[p] = float.Parse(lines[cursor++]);

            // Rebuild the torch backend with the loaded shapes; weights are
            // copied in at the start of each Update so we don't push them now.
            BuildBackend();
            return true;
        }

        private static int[] ParseShape(string line)
        {
            string[] parts = line.Split(',');
            int[] r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = int.Parse(parts[i]);
            return r;
        }
    }

    // One Trajectory = one player's perspective in one game.
    //
    // Each entry is one *learnable* decision the policy made — currently
    // the five Bernoulli outputs (buy, mortgage, advance, offer-trade,
    // accept-trade). Other decisions (jail 3-way, auction bid, build/sell
    // house counts) are taken deterministically and not recorded here.
    //
    // Per-step fields:
    //   observations[i] : 127-float state at the moment of decision i
    //   actionDims[i]   : which actor output index (0..8) drove decision i
    //   actions[i]      : 0 or 1 (the sampled binary action)
    //   probs[i]        : actor's Y[actionDims[i]] at recording time
    //                     (the policy-gradient "old" probability)
    //   values[i]       : critic estimate V(observations[i]) at recording time
    //   rewards[i]      : 0 for intermediate decisions, +1 for the last
    //                     decision of the winner, 0 for the last decision
    //                     of non-winners
    public class Trajectory
    {
        public int seat;
        public List<float[]> observations = new List<float[]>();
        public List<int> actionDims = new List<int>();
        public List<int> actions = new List<int>();
        public List<float> probs = new List<float>();
        public List<float> values = new List<float>();
        public List<float> rewards = new List<float>();
        public float[] advantages;
        public float[] returns;
    }

    // RolloutBatch holds all trajectories collected in one Step. Exposes
    // flat-array views convenient for shipping to a gradient backend.
    public class RolloutBatch
    {
        public List<Trajectory> trajectories = new List<Trajectory>();
        public int size = 0;
        public void Add(Trajectory t) { trajectories.Add(t); size += t.observations.Count; }
    }

}
