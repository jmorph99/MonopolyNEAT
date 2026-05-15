using System;
using System.IO;
using System.Text;
using System.Threading;

namespace TRAINING
{
    // sep-CMA-ES (Ros & Hansen 2008): the diagonal-covariance variant of
    // CMA-ES. Full CMA-ES maintains an n*n covariance matrix C — for our
    // ~13K-parameter MLP that would be ~670 MB and an O(n^3) eigendecomp
    // every cycle. sep-CMA-ES restricts C to its diagonal, dropping memory
    // to O(n) and ditching the eigendecomp entirely.
    //
    // What sep-CMA-ES keeps from the full algorithm:
    //  - Cumulative step-size adaptation (the ps evolution path).
    //  - Rank-mu update of the per-dimension variances (the pc path + diagC).
    //  - Weighted recombination with log-rank weights.
    //  - Mu-eff-based learning rates.
    // What it loses:
    //  - Correlation between parameters. For NN training this matters less
    //    than you'd think because the parameter Hessian is approximately
    //    block-diagonal anyway (different layers don't co-vary strongly).
    //
    // Hyperparameters follow the paper's defaults adjusted for separability;
    // c_cov is scaled by (n+2)/3 vs. the full-CMA-ES default. Population
    // size lambda = 32 is comfortable for the default MLP — much smaller
    // than ES's 64, because CMA-ES extracts more signal per sample.
    public class CmaEsTrainer : ITrainer
    {
        public string Name { get { return "cmaes"; } }
        public int Generation { get { return generation; } }

        public int POPULATION_SIZE = 32;       // lambda
        public int GAMES_PER_CANDIDATE = 64;
        public int WORKERS = 8;
        public float INITIAL_SIGMA = 0.05f;

        // State
        public MLP policy;
        public float[] mean;        // m, length n
        public float[] diagC;       // diagonal of C, length n  (variances)
        public float[] pSigma;      // evolution path for sigma, length n
        public float[] pC;          // evolution path for C, length n
        public float sigma;

        // Derived constants (recomputed in Initialise after n is known)
        private float[] weights;    // length mu
        private int mu;
        private float muEff;
        private float cSigma, dSigma;
        private float cC, cCov;
        private float chiN;          // expected ||N(0, I)|| for sigma damping
        private int n;
        private int generation = 0;

        public void Initialise()
        {
            policy = MLP.CreateDefault();
            policy.InitialiseHe(RNG.instance.gen);

            n = policy.totalParams;
            mean = (float[])policy.parameters.Clone();

            diagC = new float[n];
            for (int i = 0; i < n; i++) diagC[i] = 1.0f;

            pSigma = new float[n];
            pC = new float[n];
            sigma = INITIAL_SIGMA;
            generation = 0;

            RecomputeStrategyParameters();
        }

        // Standard sep-CMA-ES strategy parameters. All formulae from
        // Ros & Hansen (2008) "A simple modification in CMA-ES achieving
        // linear time and space complexity".
        private void RecomputeStrategyParameters()
        {
            int lambda = POPULATION_SIZE;
            mu = lambda / 2;

            // Log-rank recombination weights, normalised.
            weights = new float[mu];
            float wSum = 0.0f;
            for (int k = 0; k < mu; k++)
            {
                weights[k] = (float)(Math.Log(mu + 0.5) - Math.Log(k + 1));
                wSum += weights[k];
            }
            for (int k = 0; k < mu; k++) weights[k] /= wSum;

            // mu_eff: variance-effective population size.
            float sumWSq = 0.0f;
            for (int k = 0; k < mu; k++) sumWSq += weights[k] * weights[k];
            muEff = 1.0f / sumWSq;

            // Adaptation rates. Both nominally O(1/n) for full CMA-ES;
            // c_cov is rescaled by (n+2)/3 for sep-CMA-ES per the paper.
            cSigma = (muEff + 2.0f) / (n + muEff + 5.0f);
            dSigma = 1.0f + 2.0f * Math.Max(0.0f, (float)Math.Sqrt((muEff - 1.0f) / (n + 1)) - 1.0f) + cSigma;

            cC = (4.0f + muEff / n) / (n + 4.0f + 2.0f * muEff / n);

            float cCovFull = 2.0f / ((n + 1.3f) * (n + 1.3f) + muEff)
                             + (1.0f - 1.0f / muEff) * Math.Min(1.0f, (2.0f * muEff - 1.0f) / ((n + 2.0f) * (n + 2.0f) + muEff));
            cCov = cCovFull * (n + 2.0f) / 3.0f;   // sep-CMA-ES scaling

            // E||N(0, I)|| ~ sqrt(n) * (1 - 1/(4n) + 1/(21 n^2))
            chiN = (float)(Math.Sqrt(n) * (1.0 - 1.0 / (4.0 * n) + 1.0 / (21.0 * n * n)));
        }

        public void Step()
        {
            int lambda = POPULATION_SIZE;

            // 1. Sample lambda candidates: x = m + sigma * sqrt(diagC) * z, z ~ N(0, I)
            float[][] zSamples = new float[lambda][];
            MLP[] candidates = new MLP[lambda];
            for (int k = 0; k < lambda; k++)
            {
                zSamples[k] = new float[n];
                float[] xBuf = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float z = MLP.SampleGaussian(RNG.instance.gen);
                    zSamples[k][i] = z;
                    xBuf[i] = mean[i] + sigma * (float)Math.Sqrt(Math.Max(0.0, diagC[i])) * z;
                }
                candidates[k] = new MLP(policy.layerSizes, xBuf);
            }

            // 2. Evaluate
            float[] fitness = EvaluatePopulation(candidates);

            // 3. Sort indices by fitness descending (best first).
            int[] order = new int[lambda];
            for (int i = 0; i < lambda; i++) order[i] = i;
            Array.Sort(order, (a, b) => fitness[b].CompareTo(fitness[a]));

            // 4. Weighted mean update.
            float[] meanOld = (float[])mean.Clone();
            for (int i = 0; i < n; i++) mean[i] = 0.0f;
            for (int k = 0; k < mu; k++)
            {
                float[] x = candidates[order[k]].parameters;
                float w = weights[k];
                for (int i = 0; i < n; i++) mean[i] += w * x[i];
            }

            // 5. Step-size path ps. Per-dim form: ps <- (1-c_s) ps + sqrt(c_s(2-c_s) mu_eff) * (m_new - m_old) / (sigma * sqrt(diagC))
            float invSigma = 1.0f / sigma;
            float sigmaPathCoef = (float)Math.Sqrt(cSigma * (2.0f - cSigma) * muEff);
            for (int i = 0; i < n; i++)
            {
                float invD = 1.0f / Math.Max(1e-12f, (float)Math.Sqrt(diagC[i]));
                pSigma[i] = (1.0f - cSigma) * pSigma[i] + sigmaPathCoef * invSigma * invD * (mean[i] - meanOld[i]);
            }

            // 6. Update sigma. ||pSigma|| / E||N|| > 1  -> grow sigma, else shrink.
            float psNorm = 0.0f;
            for (int i = 0; i < n; i++) psNorm += pSigma[i] * pSigma[i];
            psNorm = (float)Math.Sqrt(psNorm);
            sigma *= (float)Math.Exp((cSigma / dSigma) * (psNorm / chiN - 1.0f));

            // 7. Covariance-path pc. Heaviside-style indicator gates pc against
            //    sigma blowups; in practice this is rarely 0 in well-tuned runs.
            float hSigma = (psNorm / (float)Math.Sqrt(1.0 - Math.Pow(1.0 - cSigma, 2 * (generation + 1))) / chiN < (1.4f + 2.0f / (n + 1.0f))) ? 1.0f : 0.0f;
            float pcCoef = (float)Math.Sqrt(cC * (2.0f - cC) * muEff);
            for (int i = 0; i < n; i++)
            {
                pC[i] = (1.0f - cC) * pC[i] + hSigma * pcCoef * invSigma * (mean[i] - meanOld[i]);
            }

            // 8. Update diagC: rank-one + rank-mu (separable form).
            //    diagC <- (1 - c_cov) diagC + (c_cov / mu_cov) * pc.^2 * 1[hSigma]
            //                                + c_cov * (1 - 1/mu_cov) * sum_k w_k * (x_k - m_old)^2 / sigma^2
            //    With mu_cov = mu_eff (standard choice), the (1/mu_cov) terms simplify.
            float oneOverMuCov = 1.0f / muEff;
            float[] newDiagC = new float[n];
            for (int i = 0; i < n; i++)
            {
                float rankOne = oneOverMuCov * pC[i] * pC[i];
                float rankMu = 0.0f;
                for (int k = 0; k < mu; k++)
                {
                    float diff = (candidates[order[k]].parameters[i] - meanOld[i]) * invSigma;
                    rankMu += weights[k] * diff * diff;
                }
                rankMu *= (1.0f - oneOverMuCov);
                newDiagC[i] = (1.0f - cCov) * diagC[i] + cCov * (rankOne + rankMu);
                if (newDiagC[i] < 1e-12f) newDiagC[i] = 1e-12f;
            }
            diagC = newDiagC;

            // Sync policy MLP to the new mean (so callers asking for the "current
            // best brain" get the centre of the search distribution).
            Array.Copy(mean, policy.parameters, n);

            // Report
            float bestFit = fitness[order[0]];
            float meanFit = 0.0f;
            for (int i = 0; i < lambda; i++) meanFit += fitness[i];
            meanFit /= lambda;
            Console.WriteLine("CMA-ES gen " + generation + " best=" + bestFit.ToString("0.00")
                + " mean=" + meanFit.ToString("0.00") + " sigma=" + sigma.ToString("0.000000")
                + " ||ps||=" + psNorm.ToString("0.00"));

            generation++;
        }

        // Reuses the round-robin-against-peers evaluation that EsTrainer uses.
        // Different file because we want to keep the trainer self-contained
        // and the call-site small.
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
                            int[] opp = new int[3];
                            for (int k = 0; k < 3; k++)
                            {
                                int o;
                                int safety = 0;
                                do { o = RNG.instance.gen.Next(0, N); safety++; }
                                while ((o == idx || ContainsBefore(opp, k, o)) && safety < 20);
                                opp[k] = o;
                            }

                            MONOPOLY.IEvaluator[] networks = new MONOPOLY.IEvaluator[4]
                            {
                                candidates[idx], candidates[opp[0]], candidates[opp[1]], candidates[opp[2]]
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

        private static bool ContainsBefore(int[] arr, int len, int v)
        {
            for (int i = 0; i < len; i++) if (arr[i] == v) return true;
            return false;
        }

        // Format: same plain-text style as ES, with the extra state CMA-ES needs.
        //   generation
        //   layer_sizes
        //   sigma
        //   mean (n lines)
        //   diagC (n lines)
        //   pSigma (n lines)
        //   pC (n lines)
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
            sb.Append(sigma).Append('\n');
            for (int i = 0; i < n; i++) sb.Append(mean[i]).Append('\n');
            for (int i = 0; i < n; i++) sb.Append(diagC[i]).Append('\n');
            for (int i = 0; i < n; i++) sb.Append(pSigma[i]).Append('\n');
            for (int i = 0; i < n; i++) sb.Append(pC[i]).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        public bool Load(string path)
        {
            if (!File.Exists(path)) return false;
            string[] lines = File.ReadAllLines(path);

            generation = int.Parse(lines[0]);
            string[] shapeParts = lines[1].Split(',');
            int[] shape = new int[shapeParts.Length];
            for (int i = 0; i < shapeParts.Length; i++) shape[i] = int.Parse(shapeParts[i]);

            policy = new MLP(shape);
            n = policy.totalParams;
            sigma = float.Parse(lines[2]);

            mean = new float[n];
            diagC = new float[n];
            pSigma = new float[n];
            pC = new float[n];

            int cursor = 3;
            for (int i = 0; i < n; i++) mean[i] = float.Parse(lines[cursor++]);
            for (int i = 0; i < n; i++) diagC[i] = float.Parse(lines[cursor++]);
            for (int i = 0; i < n; i++) pSigma[i] = float.Parse(lines[cursor++]);
            for (int i = 0; i < n; i++) pC[i] = float.Parse(lines[cursor++]);

            Array.Copy(mean, policy.parameters, n);
            RecomputeStrategyParameters();
            return true;
        }
    }
}
