using System;

namespace TRAINING
{
    // Placeholder — replaced by the PPO data-collection scaffolding in a
    // later commit. Gradient training requires an external backend
    // (TorchSharp in-process, or a Python sidecar via RPC).
    public class PpoTrainer : ITrainer
    {
        public string Name { get { return "ppo"; } }
        public int Generation { get; private set; }

        public void Initialise() { throw new NotImplementedException("PpoTrainer not yet implemented"); }
        public void Step() { throw new NotImplementedException("PpoTrainer not yet implemented"); }
        public void Save(string path) { throw new NotImplementedException("PpoTrainer not yet implemented"); }
        public bool Load(string path) { return false; }
    }
}
