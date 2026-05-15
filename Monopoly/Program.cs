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

            // Special non-trainer command: smoke-test MCTS by playing a
            // handful of games with seat 0 wrapped in MctsPolicy vs three
            // plain ScriptedPolicy seats. Verifies the Board.Clone path
            // and the MCTS rollout machinery without needing a checkpoint.
            if (trainerName == "mcts-demo")
            {
                int games = args.Length > 1 ? int.Parse(args[1]) : 3;
                int rollouts = args.Length > 2 ? int.Parse(args[2]) : 2;
                RunMctsDemo(games, rollouts);
                return;
            }

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

        // Play `games` matches with MctsPolicy(ScriptedPolicy, rollouts) at
        // seat 0 vs three ScriptedPolicy seats. Reports win count for seat 0.
        static void RunMctsDemo(int games, int rollouts)
        {
            Console.WriteLine("MCTS demo: " + games + " games, rollouts=" + rollouts);
            int seat0Wins = 0;
            for (int g = 0; g < games; g++)
            {
                MONOPOLY.IPolicy baseInner = new MONOPOLY.ScriptedPolicy();
                MONOPOLY.IPolicy[] policies = new MONOPOLY.IPolicy[]
                {
                    new MONOPOLY.MctsPolicy(baseInner, rollouts),
                    new MONOPOLY.ScriptedPolicy(),
                    new MONOPOLY.ScriptedPolicy(),
                    new MONOPOLY.ScriptedPolicy(),
                };
                MONOPOLY.Board board = new MONOPOLY.Board(policies);
                MONOPOLY.Board.EOutcome outcome = MONOPOLY.Board.EOutcome.ONGOING;
                while (outcome == MONOPOLY.Board.EOutcome.ONGOING) outcome = board.Step();

                if (outcome == MONOPOLY.Board.EOutcome.WIN1) seat0Wins++;
                Console.WriteLine("  game " + g + " outcome=" + outcome);
            }
            Console.WriteLine("MCTS seat-0 wins: " + seat0Wins + "/" + games);
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
