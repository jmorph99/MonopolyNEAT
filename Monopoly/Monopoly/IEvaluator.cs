namespace MONOPOLY
{
    // IEvaluator is what NeuralPolicy actually depends on: anything that maps
    // a 127-float input vector to a 9-float output vector. NEAT.Phenotype
    // satisfies this (wrapped in PhenotypeEvaluator) and so does the
    // fixed-topology MLP used by ES / PPO / DQN. Keeping this seam thin lets
    // the same game-side decision code drive any of those network types.
    public interface IEvaluator
    {
        float[] Propagate(float[] X);
    }

    // Adapter: a NEAT.Phenotype as an IEvaluator. Lives on the MONOPOLY side
    // so NEAT remains ignorant of game-side abstractions.
    public class PhenotypeEvaluator : IEvaluator
    {
        public readonly NEAT.Phenotype phenotype;

        public PhenotypeEvaluator(NEAT.Phenotype p)
        {
            phenotype = p;
        }

        public float[] Propagate(float[] X)
        {
            return phenotype.Propagate(X);
        }
    }
}
