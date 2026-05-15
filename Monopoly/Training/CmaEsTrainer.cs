using System;

namespace TRAINING
{
    // Placeholder — replaced by the CMA-ES implementation in a later commit.
    public class CmaEsTrainer : ITrainer
    {
        public string Name { get { return "cmaes"; } }
        public int Generation { get; private set; }

        public void Initialise() { throw new NotImplementedException("CmaEsTrainer not yet implemented"); }
        public void Step() { throw new NotImplementedException("CmaEsTrainer not yet implemented"); }
        public void Save(string path) { throw new NotImplementedException("CmaEsTrainer not yet implemented"); }
        public bool Load(string path) { return false; }
    }
}
