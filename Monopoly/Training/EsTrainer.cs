using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TRAINING
{
    // OpenAI-ES (Salimans et al., 2017) on a fixed-topology MLP.
    //
    // The "policy" is one parameter vector theta (the MLP weights). Each
    // generation:
    //   1. Sample N antithetic perturbation vectors  epsilon_i ~ N(0, I)
    //      (so for each +epsilon we also try -epsilon, halving variance).
    //   2. Evaluate every candidate theta + sigma * epsilon_i by 4-player
    //      self-play. Fitness = wins against three random peers from the
    //      same population.
    //   3. Compute rank-shaped weights w_i (centred ranks, mean 0).
    //   4. Update theta <- theta + (alpha / (N * sigma)) * sum_i w_i * epsilon_i.
    //
    // Why rank-shaping: raw win counts are heavy-tailed (a few candidates
    // beat everyone else, most score zero), and the gradient estimate is
    // dominated by them. Centred-rank shaping makes the update scale-free
    // and reduces variance dramatically.
    //
    // Why antithetic sampling: pairs (+epsilon, -epsilon) cancel the
    // first-order noise in the gradient estimator, roughly doubling
    // sample efficiency for the cost of pairing.
    //
    // Hardware-wise, this is CPU-bound: every Step does POPULATION_SIZE
    // forward passes per game * GAMES_PER_CANDIDATE games. Threaded across
    // WORKERS via Arena.
    public class EsTrainer : ITrainer
    {
        public string Name { get { return "es"; } }
        public int Generation { get { return generation; } }

        // Hyperparameters. Defaults pick a footprint comparable to NEAT:
        // 64 candidates × 64 games = 4096 games per Step, vs NEAT's
        // 256 × 2000 = 512K per generation. ES is much more sample-
        // efficient per gradient estimate so this is intentionally smaller.
        public int POPULATION_SIZE = 64;        // must be even (antithetic)
        public int GAMES_PER_CANDIDATE = 64;
        public float SIGMA = 0.05f;             // perturbation stddev
        public float LEARNING_RATE = 0.02f;
        public int WORKERS = 8;

        public MLP policy;                       // current theta
        private int generation = 0;

        // Each step's per-candidate fitness (wins). Exposed for debugging /
        // analytics; cleared and refilled by Step().
        public float[] lastFitness;

        public EsTrainer() { }

        public void Initialise()
        {
            if (POPULATION_SIZE % 2 != 0)
            {
                throw new InvalidOperationException("ES POPULATION_SIZE must be even (antithetic sampling)");
            }

            policy = MLP.CreateDefault();
            policy.InitialiseHe(RNG.instance.gen);
            generation = 0;
        }

        public void Step()
        {
            int N = POPULATION_SIZE;
            int half = N / 2;
            int paramCount = policy.parameters.Length;

            // 1. Sample antithetic perturbations. epsilons[i] is reused as
            //    both +epsilon for candidate 2*i and -epsilon for 2*i+1.
            float[][] epsilons = new float[half][];
            for (int i = 0; i < half; i++)
            {
                epsilons[i] = new float[paramCount];
                for (int p = 0; p < paramCount; p++)
                {
                    epsilons[i][p] = MLP.SampleGaussian(RNG.instance.gen);
                }
            }

            // 2. Materialise N candidate MLPs sharing the policy's layer shape.
            //    Each one gets its own parameter buffer = theta + sign * sigma * epsilon.
            MLP[] candidates = new MLP[N];
            for (int i = 0; i < N; i++)
            {
                int eIdx = i / 2;
                float sign = (i % 2 == 0) ? 1.0f : -1.0f;
                float[] buf = new float[paramCount];
                for (int p = 0; p < paramCount; p++)
                {
                    buf[p] = policy.parameters[p] + sign * SIGMA * epsilons[eIdx][p];
                }
                candidates[i] = new MLP(policy.layerSizes, buf);
            }

            // 3. Evaluate every candidate via self-play vs three peers.
            //    Each candidate's opponents are drawn uniformly from the
            //    *rest* of the candidate set, so the comparison stays
            //    in-distribution. Wins counted per candidate, threaded.
            float[] fitness = EvaluatePopulation(candidates);
            lastFitness = fitness;

            // 4. Centred-rank shaping. ranks[i] in [0, N-1]; centred to
            //    [-0.5, 0.5]; this is the standard OpenAI-ES utility.
            float[] shaped = CentredRanks(fitness);

            // 5. Gradient estimate and SGD update. For an antithetic pair
            //    (theta + sigma*e, theta - sigma*e), the contribution to
            //    sum_i w_i * epsilon_i is (w_plus - w_minus) * e, so we
            //    aggregate pairs into a single delta per epsilon.
            float[] gradient = new float[paramCount];
            for (int i = 0; i < half; i++)
            {
                float wPlus = shaped[2 * i];
                float wMinus = shaped[2 * i + 1];
                float w = wPlus - wMinus;
                float[] e = epsilons[i];
                for (int p = 0; p < paramCount; p++)
                {
                    gradient[p] += w * e[p];
                }
            }

            float scale = LEARNING_RATE / (N * SIGMA);
            for (int p = 0; p < paramCount; p++)
            {
                policy.parameters[p] += scale * gradient[p];
            }

            // Report
            float meanFit = 0.0f, maxFit = float.MinValue;
            for (int i = 0; i < N; i++)
            {
                meanFit += fitness[i];
                if (fitness[i] > maxFit) maxFit = fitness[i];
            }
            meanFit /= N;
            Console.WriteLine("ES gen " + generation + " mean=" + meanFit.ToString("0.00") + " max=" + maxFit.ToString("0.00"));

            generation++;
        }

        // Each candidate plays GAMES_PER_CANDIDATE games where it is one of
        // four brains; the other three are sampled uniformly from the
        // remaining candidates. Wins go to the candidate that won the seat.
        //
        // Threaded over candidates via a worker pool. Per-candidate fitness
        // is a single int incremented under lock — coarse, but the lock is
        // held briefly relative to game length.
        private float[] EvaluatePopulation(MLP[] candidates)
        {
            int N = candidates.Length;
            float[] fitness = new float[N];
            object sync = new object();

            int nextIdx = 0;
            Thread[] threads = new Thread[Math.Max(1, WORKERS)];
            for (int t = 0; t < threads.Length; t++)
            {
                threads[t] = new Thread(() =>
                {
                    while (true)
                    {
                        int idx;
                        lock (sync)
                        {
                            if (nextIdx >= N) return;
                            idx = nextIdx++;
                        }

                        int wins = 0;
                        for (int g = 0; g < GAMES_PER_CANDIDATE; g++)
                        {
                            // Sample three distinct opponents from the rest.
                            int[] opp = new int[3];
                            for (int k = 0; k < 3; k++)
                            {
                                int o;
                                int safety = 0;
                                do
                                {
                                    o = RNG.instance.gen.Next(0, N);
                                    safety++;
                                } while ((o == idx || ArrayContains(opp, k, o)) && safety < 20);
                                opp[k] = o;
                            }

                            MONOPOLY.IEvaluator[] networks = new MONOPOLY.IEvaluator[4]
                            {
                                candidates[idx],
                                candidates[opp[0]],
                                candidates[opp[1]],
                                candidates[opp[2]],
                            };

                            // Shuffle seats so seat-1 advantage doesn't bias fitness.
                            int[] seats = new int[] { 0, 1, 2, 3 };
                            for (int i = 3; i > 0; i--)
                            {
                                int j = RNG.instance.gen.Next(0, i + 1);
                                (seats[i], seats[j]) = (seats[j], seats[i]);
                            }
                            MONOPOLY.IEvaluator[] seated = new MONOPOLY.IEvaluator[4];
                            for (int i = 0; i < 4; i++) seated[i] = networks[seats[i]];

                            int winnerSeat = Arena.PlayOne(seated);
                            if (winnerSeat >= 0 && seats[winnerSeat] == 0)
                            {
                                wins++;
                            }
                        }

                        lock (sync) { fitness[idx] = wins; }
                    }
                });
                threads[t].Start();
            }
            for (int t = 0; t < threads.Length; t++) threads[t].Join();

            return fitness;
        }

        private static bool ArrayContains(int[] arr, int len, int v)
        {
            for (int i = 0; i < len; i++) { if (arr[i] == v) return true; }
            return false;
        }

        // Centred-rank shaping: rank ascending, divide by (N-1) to get
        // [0,1], subtract 0.5 to get [-0.5, 0.5]. Ties broken arbitrarily
        // by stable sort.
        public static float[] CentredRanks(float[] x)
        {
            int N = x.Length;
            int[] order = new int[N];
            for (int i = 0; i < N; i++) order[i] = i;
            Array.Sort(order, (a, b) => x[a].CompareTo(x[b]));

            float[] shaped = new float[N];
            for (int r = 0; r < N; r++)
            {
                shaped[order[r]] = (float)r / Math.Max(1, N - 1) - 0.5f;
            }
            return shaped;
        }

        // On-disk format (ES checkpoint):
        //   generation
        //   layer_size_1,layer_size_2,...
        //   sigma,learning_rate
        //   parameter_1
        //   parameter_2
        //   ...
        // Newline-delimited. Cheap to parse, easy to inspect.
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
            sb.Append(SIGMA).Append(',').Append(LEARNING_RATE).Append('\n');
            for (int p = 0; p < policy.parameters.Length; p++)
            {
                sb.Append(policy.parameters[p]).Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
        }

        public bool Load(string path)
        {
            if (!File.Exists(path)) return false;

            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 4) return false;

            generation = int.Parse(lines[0]);

            string[] shapeParts = lines[1].Split(',');
            int[] shape = new int[shapeParts.Length];
            for (int i = 0; i < shapeParts.Length; i++) shape[i] = int.Parse(shapeParts[i]);

            string[] hyperParts = lines[2].Split(',');
            SIGMA = float.Parse(hyperParts[0]);
            LEARNING_RATE = float.Parse(hyperParts[1]);

            policy = new MLP(shape);
            for (int p = 0; p < policy.parameters.Length; p++)
            {
                policy.parameters[p] = float.Parse(lines[3 + p]);
            }
            return true;
        }
    }
}
