namespace TRAINING
{
    // ITrainer is what Program.Main drives: an opaque "do one generation of
    // training" loop. Implementations are NEAT, Evolution Strategies,
    // CMA-ES, PSRO, PPO (when wired to a gradient backend), Rainbow DQN
    // (same), and AlphaZero (same). The simulator and Arena code stay
    // ignorant of which trainer is running.
    //
    // Save/Load is per-trainer (each algorithm has its own state shape).
    // NEAT preserves its original on-disk format; the new trainers use a
    // simple JSON-ish format described in their own files.
    public interface ITrainer
    {
        string Name { get; }
        int Generation { get; }

        // Set up from scratch when no checkpoint exists. Called exactly once.
        void Initialise();

        // Run one training iteration: evaluate the current candidates by
        // self-play, then update the algorithm's internal state (next NEAT
        // generation, next ES mean, next PPO gradient step, etc.).
        void Step();

        // Persist current state to disk. Format is trainer-specific.
        void Save(string path);

        // Resume from a checkpoint written by Save. Returns true if the file
        // existed and loaded successfully; false otherwise (caller falls
        // back to Initialise).
        bool Load(string path);
    }
}
