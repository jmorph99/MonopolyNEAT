using System;
using System.IO;

namespace TRAINING
{
    // NeatTrainer adapts the original NEAT-tournament loop to the ITrainer
    // interface. All of the NEAT state lives in singletons (Population,
    // Mutation, NetworkFactory, Crossover) already — this class just holds
    // the Tournament wrapper and runs one iteration per Step() call.
    //
    // On-disk format is the long-standing custom-delimited blob written by
    // NEAT.Population.Save / Load. Existing checkpoint files (the
    // monopoly_population *.txt in the repo root) load through this path
    // unchanged.
    public class NeatTrainer : ITrainer
    {
        public string Name { get { return "neat"; } }
        public int Generation { get { return NEAT.Population.instance.GENERATION; } }

        public Monopoly.Tournament tournament;

        public NeatTrainer()
        {
            // The NEAT singletons must already be Initialise()-d by Program.
            tournament = new Monopoly.Tournament();
        }

        public void Initialise()
        {
            tournament.Initialise();
        }

        public void Step()
        {
            tournament.ExecuteTournament();
            NEAT.Population.instance.NewGeneration();
        }

        public void Save(string path)
        {
            NEAT.Population.instance.Save(path, tournament.championScore);
        }

        public bool Load(string path)
        {
            if (!File.Exists(path)) return false;
            NEAT.Population.instance.Load(path, out tournament.championScore);
            return true;
        }
    }
}
