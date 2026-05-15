using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TRAINING
{
    // PSRO (Policy-Space Response Oracles) on top of an ES-style inner loop.
    //
    // What it solves: pure self-play in a non-transitive game (rock-paper-
    // scissors-style cycles) chases its tail — generation N+5 can lose to
    // generation N. The fix is to evaluate the current trainee against a
    // frozen *league* of past policies, weighted by a meta-strategy that
    // approximates the Nash equilibrium of the empirical match-up matrix.
    //
    // 4-player reduction: for the meta-game we treat each match as
    // "candidate-i vs three copies of league-j", which collapses the
    // 4-player environment into a 2-player symmetric matrix game whose
    // entries A[i,j] are i's win rate vs a table of j-clones. Nash on
    // this matrix is solved by fictitious play (a few thousand cheap
    // iterations on a small matrix).
    //
    // The inner trainer here is a self-contained OpenAI-ES loop on a
    // fixed-topology MLP, but with the opponent set drawn from the league
    // (sampled with replacement under the meta-distribution) rather than
    // from sibling candidates. Every FREEZE_PERIOD generations, the
    // current mean is frozen into the league and the win matrix and meta
    // weights are recomputed.
    public class LeagueTrainer : ITrainer
    {
        public string Name { get { return "psro"; } }
        public int Generation { get { return generation; } }

        // Inner-loop (ES) hyperparameters.
        public int POPULATION_SIZE = 32;            // even
        public int GAMES_PER_CANDIDATE = 32;
        public float SIGMA = 0.05f;
        public float LEARNING_RATE = 0.02f;
        public int WORKERS = 8;

        // League / meta-game hyperparameters.
        public int FREEZE_PERIOD = 5;               // freeze every N generations
        public int LEAGUE_EVAL_GAMES = 64;          // games to estimate each A[i,j]
        public int FICTITIOUS_PLAY_ITERS = 5000;    // cheap, n_league^2 LP-free
        public int MAX_LEAGUE_SIZE = 32;            // hard cap; oldest pruned beyond

        // Inner state (ES).
        public MLP policy;                          // current mean
        private int generation = 0;

        // League state.
        public List<float[]> league;                 // each is a flat param vector
        public float[,] winMatrix;                   // empirical A[i,j], lazily resized
        public float[] metaWeights;                  // length league.Count
        public int[] layerSizes;

        // We hold the inner trainer's ITrainer interface contract independently;
        // this class IS the trainer rather than wrapping one. That keeps the
        // opponent-sampling concern co-located with the gradient step.

        public LeagueTrainer() { }

        // Compatibility constructor — Program.cs calls `new LeagueTrainer(new EsTrainer())`
        // for forward-compatibility. We ignore the inner trainer here: this
        // version has the ES update inlined so the opponent set is determined
        // by the league, not by sibling candidates. A future refactor could
        // make the inner update pluggable for PPO/DQN inner loops.
        public LeagueTrainer(ITrainer inner) : this() { }

        public void Initialise()
        {
            if (POPULATION_SIZE % 2 != 0)
            {
                throw new InvalidOperationException("PSRO POPULATION_SIZE must be even");
            }

            policy = MLP.CreateDefault();
            policy.InitialiseHe(RNG.instance.gen);
            layerSizes = policy.layerSizes;

            league = new List<float[]>();
            // Seed the league with the initial mean so generation 0 already
            // has someone to play against. If the league were empty the
            // candidate would have no opponents at all.
            league.Add((float[])policy.parameters.Clone());

            metaWeights = new float[] { 1.0f };
            winMatrix = new float[1, 1];
            winMatrix[0, 0] = 0.25f;   // self-play 4-clones is by symmetry 0.25 wins / game

            generation = 0;
        }

        public void Step()
        {
            int N = POPULATION_SIZE;
            int half = N / 2;
            int paramCount = policy.parameters.Length;

            // 1. Antithetic sample (same as plain ES).
            float[][] epsilons = new float[half][];
            for (int i = 0; i < half; i++)
            {
                epsilons[i] = new float[paramCount];
                for (int p = 0; p < paramCount; p++) epsilons[i][p] = MLP.SampleGaussian(RNG.instance.gen);
            }

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
                candidates[i] = new MLP(layerSizes, buf);
            }

            // 2. Evaluate each candidate against league opponents drawn under metaWeights.
            float[] fitness = EvaluateAgainstLeague(candidates);

            // 3. ES update: centred-rank shape, antithetic collapse, SGD-like step.
            float[] shaped = EsTrainer.CentredRanks(fitness);
            float[] gradient = new float[paramCount];
            for (int i = 0; i < half; i++)
            {
                float w = shaped[2 * i] - shaped[2 * i + 1];
                float[] e = epsilons[i];
                for (int p = 0; p < paramCount; p++) gradient[p] += w * e[p];
            }
            float scale = LEARNING_RATE / (N * SIGMA);
            for (int p = 0; p < paramCount; p++) policy.parameters[p] += scale * gradient[p];

            float mean = 0.0f, max = float.MinValue;
            for (int i = 0; i < N; i++) { mean += fitness[i]; if (fitness[i] > max) max = fitness[i]; }
            mean /= N;
            Console.WriteLine("PSRO gen " + generation + " mean=" + mean.ToString("0.00")
                + " max=" + max.ToString("0.00") + " league=" + league.Count);

            generation++;

            // 4. Periodically freeze the current mean into the league and
            //    recompute the meta-mixture. This is the actual "PSRO oracle"
            //    cycle — the inner loop is the oracle, the league update is
            //    the meta-step.
            if (generation % FREEZE_PERIOD == 0)
            {
                FreezeIntoLeague();
            }
        }

        private float[] EvaluateAgainstLeague(MLP[] candidates)
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
                            // Sample three opponents from the league, IID, under metaWeights.
                            float[] o1 = SampleLeague();
                            float[] o2 = SampleLeague();
                            float[] o3 = SampleLeague();

                            MONOPOLY.IEvaluator[] networks = new MONOPOLY.IEvaluator[4]
                            {
                                candidates[idx],
                                new MLP(layerSizes, o1),
                                new MLP(layerSizes, o2),
                                new MLP(layerSizes, o3),
                            };

                            int[] seats = new int[] { 0, 1, 2, 3 };
                            for (int i = 3; i > 0; i--)
                            {
                                int j = RNG.instance.gen.Next(0, i + 1);
                                (seats[i], seats[j]) = (seats[j], seats[i]);
                            }
                            MONOPOLY.IEvaluator[] seated = new MONOPOLY.IEvaluator[4];
                            for (int i = 0; i < 4; i++) seated[i] = networks[seats[i]];

                            int winnerSeat = Arena.PlayOne(seated);
                            if (winnerSeat >= 0 && seats[winnerSeat] == 0) wins++;
                        }
                        lock (sync) { fitness[idx] = wins; }
                    }
                });
                threads[t].Start();
            }
            for (int t = 0; t < threads.Length; t++) threads[t].Join();
            return fitness;
        }

        // Categorical sample from metaWeights. Falls back to uniform if
        // metaWeights doesn't sum cleanly (e.g. just after a freeze).
        private float[] SampleLeague()
        {
            float r = (float)RNG.instance.gen.NextDouble();
            float cum = 0.0f;
            for (int i = 0; i < league.Count; i++)
            {
                cum += metaWeights[i];
                if (r <= cum) return league[i];
            }
            return league[league.Count - 1];
        }

        private void FreezeIntoLeague()
        {
            // 1. Append the current mean as a new league member.
            league.Add((float[])policy.parameters.Clone());

            // 2. Prune oldest if past cap (keep the very first as ground anchor).
            while (league.Count > MAX_LEAGUE_SIZE)
            {
                league.RemoveAt(1);   // index 0 is the initial random anchor
            }

            // 3. Expand win matrix to new size and fill the new row/column.
            int L = league.Count;
            float[,] newMatrix = new float[L, L];
            int oldL = winMatrix.GetLength(0);
            // Copy intersection
            int copyL = Math.Min(oldL, L);
            for (int i = 0; i < copyL; i++)
            {
                for (int j = 0; j < copyL; j++) newMatrix[i, j] = winMatrix[i, j];
            }
            winMatrix = newMatrix;

            // Fill new row L-1 (and matching column) with empirical play.
            // A[L-1, j] = win rate of player L-1 vs three copies of j.
            int newest = L - 1;
            for (int j = 0; j < L; j++)
            {
                float winRate = EstimateWinRate(league[newest], league[j], LEAGUE_EVAL_GAMES);
                winMatrix[newest, j] = winRate;
                if (j != newest)
                {
                    // Symmetry doesn't hold; fill the column with j-vs-newest.
                    winMatrix[j, newest] = EstimateWinRate(league[j], league[newest], LEAGUE_EVAL_GAMES);
                }
            }

            // 4. Solve meta-game by fictitious play and update metaWeights.
            metaWeights = FictitiousPlay(winMatrix, FICTITIOUS_PLAY_ITERS);
            Console.WriteLine("PSRO league=" + L + " metaWeights=" + FormatVector(metaWeights, 4));
        }

        // Estimate win rate of `row` vs three copies of `col`. Cheap helper
        // used to populate the meta-game matrix entries.
        private float EstimateWinRate(float[] row, float[] col, int games)
        {
            MLP r = new MLP(layerSizes, (float[])row.Clone());
            int wins = 0;
            for (int g = 0; g < games; g++)
            {
                MONOPOLY.IEvaluator[] networks = new MONOPOLY.IEvaluator[4]
                {
                    r,
                    new MLP(layerSizes, (float[])col.Clone()),
                    new MLP(layerSizes, (float[])col.Clone()),
                    new MLP(layerSizes, (float[])col.Clone()),
                };
                int[] seats = new int[] { 0, 1, 2, 3 };
                for (int i = 3; i > 0; i--)
                {
                    int j = RNG.instance.gen.Next(0, i + 1);
                    (seats[i], seats[j]) = (seats[j], seats[i]);
                }
                MONOPOLY.IEvaluator[] seated = new MONOPOLY.IEvaluator[4];
                for (int i = 0; i < 4; i++) seated[i] = networks[seats[i]];

                int winnerSeat = Arena.PlayOne(seated);
                if (winnerSeat >= 0 && seats[winnerSeat] == 0) wins++;
            }
            return (float)wins / games;
        }

        // Fictitious play on a symmetric matrix game. Returns a mixed
        // strategy over rows that approximates a Nash equilibrium.
        //
        // FP doesn't always converge for general games but it converges
        // for zero-sum 2-player games and for the symmetric reduction of
        // a symmetric game (which the row-vs-clones reduction gives us).
        public static float[] FictitiousPlay(float[,] payoff, int iters)
        {
            int L = payoff.GetLength(0);
            float[] counts = new float[L];
            counts[0] = 1.0f;     // arbitrary starting strategy
            float total = 1.0f;

            for (int t = 0; t < iters; t++)
            {
                // Compute expected payoff against current opponent mixture.
                // Best response: argmax_i sum_j (counts[j]/total) * payoff[i, j]
                int best = 0;
                float bestVal = float.MinValue;
                for (int i = 0; i < L; i++)
                {
                    float val = 0.0f;
                    for (int j = 0; j < L; j++) val += counts[j] * payoff[i, j];
                    if (val > bestVal) { bestVal = val; best = i; }
                }
                counts[best]++;
                total++;
            }

            float[] mix = new float[L];
            for (int i = 0; i < L; i++) mix[i] = counts[i] / total;
            return mix;
        }

        private static string FormatVector(float[] v, int maxPrint)
        {
            StringBuilder sb = new StringBuilder("[");
            int show = Math.Min(maxPrint, v.Length);
            for (int i = 0; i < show; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(v[i].ToString("0.00"));
            }
            if (show < v.Length) sb.Append(",...");
            sb.Append("]");
            return sb.ToString();
        }

        // On-disk format: header + league size, then per-member parameter blocks,
        // then meta weights, then win matrix.
        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(generation).Append('\n');
            for (int i = 0; i < layerSizes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(layerSizes[i]);
            }
            sb.Append('\n');
            sb.Append(SIGMA).Append(',').Append(LEARNING_RATE).Append('\n');
            sb.Append(league.Count).Append('\n');

            // current mean
            for (int i = 0; i < policy.parameters.Length; i++) sb.Append(policy.parameters[i]).Append('\n');

            // league members
            for (int k = 0; k < league.Count; k++)
            {
                for (int i = 0; i < league[k].Length; i++) sb.Append(league[k][i]).Append('\n');
            }

            // meta weights
            for (int i = 0; i < metaWeights.Length; i++) sb.Append(metaWeights[i]).Append('\n');

            // win matrix (row-major)
            int L = winMatrix.GetLength(0);
            for (int i = 0; i < L; i++)
                for (int j = 0; j < L; j++) sb.Append(winMatrix[i, j]).Append('\n');

            File.WriteAllText(path, sb.ToString());
        }

        public bool Load(string path)
        {
            if (!File.Exists(path)) return false;
            string[] lines = File.ReadAllLines(path);
            int cursor = 0;

            generation = int.Parse(lines[cursor++]);

            string[] shapeParts = lines[cursor++].Split(',');
            layerSizes = new int[shapeParts.Length];
            for (int i = 0; i < shapeParts.Length; i++) layerSizes[i] = int.Parse(shapeParts[i]);

            string[] hyperParts = lines[cursor++].Split(',');
            SIGMA = float.Parse(hyperParts[0]);
            LEARNING_RATE = float.Parse(hyperParts[1]);

            int leagueCount = int.Parse(lines[cursor++]);

            policy = new MLP(layerSizes);
            for (int i = 0; i < policy.parameters.Length; i++) policy.parameters[i] = float.Parse(lines[cursor++]);

            league = new List<float[]>();
            for (int k = 0; k < leagueCount; k++)
            {
                float[] buf = new float[policy.parameters.Length];
                for (int i = 0; i < buf.Length; i++) buf[i] = float.Parse(lines[cursor++]);
                league.Add(buf);
            }

            metaWeights = new float[leagueCount];
            for (int i = 0; i < leagueCount; i++) metaWeights[i] = float.Parse(lines[cursor++]);

            winMatrix = new float[leagueCount, leagueCount];
            for (int i = 0; i < leagueCount; i++)
                for (int j = 0; j < leagueCount; j++) winMatrix[i, j] = float.Parse(lines[cursor++]);

            return true;
        }
    }
}
