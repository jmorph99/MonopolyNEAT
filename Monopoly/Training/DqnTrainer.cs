using System;

namespace TRAINING
{
    // Placeholder — replaced by the Rainbow DQN replay-buffer scaffolding
    // in a later commit. Gradient training requires an external backend
    // (TorchSharp in-process, or a Python sidecar via RPC).
    public class DqnTrainer : ITrainer
    {
        public string Name { get { return "dqn"; } }
        public int Generation { get; private set; }

        public void Initialise() { throw new NotImplementedException("DqnTrainer not yet implemented"); }
        public void Step() { throw new NotImplementedException("DqnTrainer not yet implemented"); }
        public void Save(string path) { throw new NotImplementedException("DqnTrainer not yet implemented"); }
        public bool Load(string path) { return false; }
    }
}
