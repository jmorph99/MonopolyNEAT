using System.IO;

namespace Monopoly
{
    class Program
    {
        static void Main(string[] args)
        {
            Analytics a = new Analytics();
            Analytics.instance = a;

            string path = "C:\\Users\\Brad\\Desktop\\monopoly_population.txt";

            RNG.Initialise();

            NEAT.NetworkFactory.Initialise();
            NEAT.Mutation.Initialise();
            NEAT.Crossover.Initialise();
            NEAT.Population.Initialise();

            Tournament tournament = new Tournament();

            if (File.Exists(path))
            {
                NEAT.Population.instance.Load(path, out tournament.championScore);
            }
            else
            {
                tournament.Initialise();
            }

            for (int i = 0; i < 1000; i++)
            {
                tournament.ExecuteTournament();
                NEAT.Population.instance.NewGeneration();
                NEAT.Population.instance.Save(path, tournament.championScore);
            }
        }
    }
}
