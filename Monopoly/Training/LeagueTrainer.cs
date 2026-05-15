using System;

namespace TRAINING
{
    // Placeholder — replaced by the PSRO league wrapper in a later commit.
    public class LeagueTrainer : ITrainer
    {
        public string Name { get { return "psro"; } }
        public int Generation { get; private set; }

        public LeagueTrainer(ITrainer inner) { }

        public void Initialise() { throw new NotImplementedException("LeagueTrainer not yet implemented"); }
        public void Step() { throw new NotImplementedException("LeagueTrainer not yet implemented"); }
        public void Save(string path) { throw new NotImplementedException("LeagueTrainer not yet implemented"); }
        public bool Load(string path) { return false; }
    }
}
