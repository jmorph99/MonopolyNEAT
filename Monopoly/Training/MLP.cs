using System;

namespace TRAINING
{
    // MLP is a fixed-topology feed-forward network whose weights live in a
    // flat float[] vector. This is the shape ES / PPO / DQN want: a single
    // parameter vector you can perturb (ES), differentiate (PPO/DQN), or
    // average across runs.
    //
    // Architecture: INPUT (sigmoid hidden layers) OUTPUT. Layer sizes are
    // configurable via the constructor; the default 126 -> 64 -> 64 -> 9 is
    // small enough that a 256-genome ES population fits comfortably in RAM
    // and a single Propagate is a few microseconds on CPU.
    //
    // The network exposes itself as MONOPOLY.IEvaluator so NeuralPolicy
    // doesn't care whether it's running a NEAT phenotype or an MLP. The
    // 127-element input vector (126 features + 1 selection-money context) is
    // accepted as-is — the trailing context byte is just another input
    // dimension to the MLP.
    public class MLP : MONOPOLY.IEvaluator
    {
        public readonly int[] layerSizes;     // [in, h1, h2, ..., out]
        public readonly int totalParams;
        public readonly int totalActivations;

        // Per-layer offsets into the flat parameter vector. Layer L's weights
        // start at weightOffsets[L] and have shape (layerSizes[L+1], layerSizes[L]).
        // The corresponding bias block starts at biasOffsets[L] with length
        // layerSizes[L+1].
        public readonly int[] weightOffsets;
        public readonly int[] biasOffsets;

        // Parameter buffer. Public so trainers can index it directly during
        // perturbation / gradient steps.
        public float[] parameters;

        public MLP(int[] layers, float[] paramBuf = null)
        {
            if (layers == null || layers.Length < 2)
            {
                throw new ArgumentException("MLP needs at least input and output layer sizes");
            }

            layerSizes = (int[])layers.Clone();

            int numLayers = layerSizes.Length;
            weightOffsets = new int[numLayers - 1];
            biasOffsets = new int[numLayers - 1];

            int cursor = 0;
            int act = 0;
            for (int L = 0; L < numLayers - 1; L++)
            {
                weightOffsets[L] = cursor;
                cursor += layerSizes[L] * layerSizes[L + 1];
                biasOffsets[L] = cursor;
                cursor += layerSizes[L + 1];
                act += layerSizes[L + 1];
            }

            totalParams = cursor;
            totalActivations = act;

            parameters = paramBuf ?? new float[totalParams];
            if (parameters.Length != totalParams)
            {
                throw new ArgumentException("paramBuf has wrong length for given layer shape");
            }
        }

        // Default shape used by ES / PPO / DQN trainers: 127 -> 64 -> 64 -> 9.
        // 127 because Projection emits 127 floats; the MLP simply treats the
        // money-context slot as one more input dimension.
        public static MLP CreateDefault()
        {
            return new MLP(new int[] { MONOPOLY.Projection.PACK_SIZE, 64, 64, 9 });
        }

        // Initialise parameters with He initialisation: weights ~ N(0, sqrt(2/fan_in)),
        // biases = 0. Sigmoid activations don't actually benefit from He (Xavier is
        // a better fit), but the difference is minor for a network this small and
        // the keep-it-simple choice avoids per-layer activation-aware scaling.
        public void InitialiseHe(Random rng)
        {
            for (int L = 0; L < layerSizes.Length - 1; L++)
            {
                int fanIn = layerSizes[L];
                float scale = (float)Math.Sqrt(2.0 / Math.Max(1, fanIn));
                int wStart = weightOffsets[L];
                int wLen = layerSizes[L] * layerSizes[L + 1];

                for (int i = 0; i < wLen; i++)
                {
                    parameters[wStart + i] = SampleGaussian(rng) * scale;
                }

                int bStart = biasOffsets[L];
                int bLen = layerSizes[L + 1];
                for (int i = 0; i < bLen; i++)
                {
                    parameters[bStart + i] = 0.0f;
                }
            }
        }

        public float[] Propagate(float[] X)
        {
            int inDim = layerSizes[0];
            // X may be PACK_SIZE long; trim to inDim if shorter, copy otherwise.
            // (Projection emits PACK_SIZE; treating it as the MLP's input is fine.)
            float[] cur = new float[inDim];
            int copyLen = Math.Min(inDim, X.Length);
            Array.Copy(X, cur, copyLen);

            for (int L = 0; L < layerSizes.Length - 1; L++)
            {
                int rows = layerSizes[L + 1];
                int cols = layerSizes[L];
                int wBase = weightOffsets[L];
                int bBase = biasOffsets[L];

                float[] next = new float[rows];
                for (int r = 0; r < rows; r++)
                {
                    float sum = parameters[bBase + r];
                    int rowOffset = wBase + r * cols;
                    for (int c = 0; c < cols; c++)
                    {
                        sum += parameters[rowOffset + c] * cur[c];
                    }
                    next[r] = Sigmoid(sum);
                }
                cur = next;
            }

            return cur;
        }

        // Standard logistic sigmoid; matches NEAT.Phenotype's activation so
        // network output magnitudes are comparable across the two backends
        // (important for output thresholds in NeuralPolicy).
        public static float Sigmoid(float x)
        {
            // Clamp to avoid overflow on extreme inputs from un-trained nets.
            if (x > 30.0f) return 1.0f;
            if (x < -30.0f) return 0.0f;
            return 1.0f / (1.0f + (float)Math.Exp(-x));
        }

        // Box-Muller, single sample. Trainers that need many gaussians per
        // step have their own bulk samplers; this one is for init only.
        public static float SampleGaussian(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        // Build a sibling MLP that shares the layer shape but has its own
        // parameter buffer (so trainers can clone-and-perturb without aliasing).
        public MLP CloneShape()
        {
            return new MLP(layerSizes);
        }
    }
}
