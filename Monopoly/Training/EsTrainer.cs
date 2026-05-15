using System;

namespace TRAINING
{
    // Placeholder — replaced in the next commit by the OpenAI-ES implementation.
    public class EsTrainer : ITrainer
    {
        public string Name { get { return "es"; } }
        public int Generation { get; private set; }

        public void Initialise() { throw new NotImplementedException("EsTrainer not yet implemented"); }
        public void Step() { throw new NotImplementedException("EsTrainer not yet implemented"); }
        public void Save(string path) { throw new NotImplementedException("EsTrainer not yet implemented"); }
        public bool Load(string path) { return false; }
    }
}
