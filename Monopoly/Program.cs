using System;
using System.IO;

namespace Monopoly
{
    // Program is the training driver. CLI:
    //   Monopoly [trainer] [checkpoint-path] [iterations]
    //
    // trainer:        neat (default) | es | cmaes | psro | ppo | dqn
    // checkpoint:     file to load from / save to (default: monopoly_population.txt)
    // iterations:     how many training iterations to run (default: 1000)
    //
    // The trainer abstraction in TRAINING.ITrainer lets each method bring
    // its own state and on-disk format; this file just picks which one to
    // run and hands it the file path.
    class Program
    {
        static void Main(string[] args)
        {
            string trainerName = args.Length > 0 ? args[0] : "neat";
            string path = args.Length > 1 ? args[1] : "monopoly_population.txt";
            int iterations = args.Length > 2 ? int.Parse(args[2]) : 1000;

            Analytics a = new Analytics();
            Analytics.instance = a;

            RNG.Initialise();

            NEAT.NetworkFactory.Initialise();
            NEAT.Mutation.Initialise();
            NEAT.Crossover.Initialise();
            NEAT.Population.Initialise();

            TRAINING.ITrainer trainer = BuildTrainer(trainerName);
            Console.WriteLine("TRAINER: " + trainer.Name);
            Console.WriteLine("CHECKPOINT: " + path);

            if (!trainer.Load(path))
            {
                Console.WriteLine("No checkpoint at '" + path + "', initialising fresh.");
                trainer.Initialise();
            }

            for (int i = 0; i < iterations; i++)
            {
                trainer.Step();
                trainer.Save(path);
            }
        }

        static TRAINING.ITrainer BuildTrainer(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "neat":
                    return new TRAINING.NeatTrainer();
                case "es":
                case "openai-es":
                case "openai_es":
                    return new TRAINING.EsTrainer();
                case "cmaes":
                case "cma-es":
                case "cma_es":
                    return new TRAINING.CmaEsTrainer();
                case "psro":
                case "league":
                    return new TRAINING.LeagueTrainer(new TRAINING.EsTrainer());
                case "ppo":
                    return new TRAINING.PpoTrainer();
                case "dqn":
                case "rainbow":
                    return new TRAINING.DqnTrainer();
                default:
                    throw new ArgumentException("Unknown trainer: " + name +
                        " (try: neat, es, cmaes, psro, ppo, dqn)");
            }
        }
    }
}
