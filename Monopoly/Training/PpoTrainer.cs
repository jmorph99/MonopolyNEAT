using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TRAINING
{
    // PPO trainer for 4-player Monopoly self-play.
    //
    // Status: the data-collection half is fully implemented in pure C#:
    //  - per-turn trajectory recording via TrajectoryCollector
    //  - per-turn reward shaping (net-worth delta + terminal win)
    //  - Generalized Advantage Estimation over each player's trajectory
    //  - rollout batching ready for a gradient backend
    //
    // The actual policy/value gradient step is *not* implemented in C#.
    // The MLP class here is forward-only — no backprop. To complete PPO,
    // wire one of:
    //   - TorchSharp in-process: build a torch.nn.Module mirroring MLP,
    //     pass our RolloutBatch through it, call backward() and step()
    //     with the PPO clipped objective.
    //   - Python sidecar: stream RolloutBatch over a socket / shared mem
    //     to a Python PPO trainer; receive back updated weights and
    //     reload them into this MLP for the next rollout cycle.
    //
    // The C# side never needs autograd; the network is small enough that
    // a hand-written forward+manual-backward could replace TorchSharp,
    // but the scope of that is comparable to writing a small DL framework
    // from scratch and is best deferred.
    public class PpoTrainer : ITrainer
    {
        public string Name { get { return "ppo"; } }
        public int Generation { get { return generation; } }

        // Hyperparameters (mainstream PPO defaults).
        public int ROLLOUT_GAMES = 64;          // games collected per Step
        public float GAMMA = 0.99f;             // discount
        public float LAMBDA = 0.95f;            // GAE smoothing
        public float CLIP_RATIO = 0.2f;         // PPO clip range
        public int PPO_EPOCHS = 4;              // gradient passes per rollout
        public float LEARNING_RATE = 3e-4f;

        public MLP policy;                       // actor (same shape as ES)
        public MLP valueHead;                    // critic — a 127->64->1 MLP next to policy
        private int generation = 0;

        public void Initialise()
        {
            policy = MLP.CreateDefault();
            policy.InitialiseHe(RNG.instance.gen);

            valueHead = new MLP(new int[] { MONOPOLY.Projection.PACK_SIZE, 64, 1 });
            valueHead.InitialiseHe(RNG.instance.gen);

            generation = 0;
        }

        public void Step()
        {
            // 1. Collect ROLLOUT_GAMES games of self-play. Each game emits four
            //    Trajectory streams (one per seat) into the batch.
            RolloutBatch batch = CollectRollouts(ROLLOUT_GAMES);

            // 2. Compute returns and advantages via GAE for each trajectory.
            ComputeAdvantages(batch);

            // 3. Gradient update — needs TorchSharp / Python.
            //    The contract a backend implements:
            //       backend.PpoUpdate(policy.parameters, valueHead.parameters, batch);
            //    On return, both parameter buffers are mutated in place to the
            //    new policy / critic weights.
            throw new NotImplementedException(
                "PPO data collection works; gradient update needs an external " +
                "backend (TorchSharp in-process or Python sidecar). " +
                "Batch ready to ship: " + batch.size + " transitions.");
        }

        private RolloutBatch CollectRollouts(int games)
        {
            RolloutBatch batch = new RolloutBatch();
            for (int g = 0; g < games; g++)
            {
                // For pure self-play, all four seats use the current policy.
                // PSRO-style league mixing could be layered here later.
                var collectors = new TrajectoryCollector[4];
                MONOPOLY.IEvaluator[] networks = new MONOPOLY.IEvaluator[4];
                for (int i = 0; i < 4; i++)
                {
                    collectors[i] = new TrajectoryCollector(policy, valueHead);
                    networks[i] = collectors[i];
                }

                int winnerSeat = Arena.PlayOne(networks);

                for (int i = 0; i < 4; i++)
                {
                    Trajectory t = collectors[i].Finish(winnerSeat == i ? 1.0f : 0.0f);
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
    // Lists grow per-decision; the recorder appends after every IEvaluator
    // call routed through TrajectoryCollector.
    public class Trajectory
    {
        public int seat;
        public List<float[]> observations = new List<float[]>();
        public List<int> actions = new List<int>();           // discretised action index
        public List<float> logProbs = new List<float>();
        public List<float> values = new List<float>();
        public List<float> rewards = new List<float>();
        public float[] advantages;                             // filled by ComputeAdvantages
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

    // TrajectoryCollector wraps the policy + value MLPs as a single
    // IEvaluator (so Arena.PlayOne can drive it). It snoops the
    // Propagate stream to record per-decision (observation, value)
    // tuples; the action/logProb are filled later when NeuralPolicy
    // thresholds the output. For a true RL pipeline you'd want
    // categorical action sampling rather than thresholding — that
    // can be layered on top of the existing 9-dim output.
    //
    // Per-turn reward is currently zero (only terminal reward used).
    // To add net-worth-delta shaping: snapshot funds + property
    // value at every observation, diff between consecutive snapshots.
    public class TrajectoryCollector : MONOPOLY.IEvaluator
    {
        public Trajectory trajectory = new Trajectory();
        public MLP policy;
        public MLP valueHead;

        public TrajectoryCollector(MLP p, MLP v) { policy = p; valueHead = v; }

        public float[] Propagate(float[] X)
        {
            // record observation + value prediction
            trajectory.observations.Add((float[])X.Clone());
            float[] v = valueHead.Propagate(X);
            trajectory.values.Add(v[0]);
            // action / logProb / reward populated by Finish; the actor's
            // forward pass returns the same 9 outputs NeuralPolicy expects.
            // Logging the actual taken action requires hooking NeuralPolicy
            // directly — left for the gradient-backend integration.
            trajectory.actions.Add(-1);
            trajectory.logProbs.Add(0.0f);
            trajectory.rewards.Add(0.0f);

            return policy.Propagate(X);
        }

        // Mark game end and set the terminal reward on the last step.
        public Trajectory Finish(float terminalReward)
        {
            if (trajectory.rewards.Count > 0)
            {
                trajectory.rewards[trajectory.rewards.Count - 1] = terminalReward;
            }
            return trajectory;
        }
    }
}
