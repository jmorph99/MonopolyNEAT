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

            // 6-player showdown: each seat is a different model. Reports
            // per-model win rate over N games. Caps at 8 worker threads.
            //   Usage: showdown [games] [neat] [es] [cmaes] [psro] [ppo]
            if (trainerName == "showdown")
            {
                RunShowdown(args);
                return;
            }

            // Trainer-mode CLI: trainer <path> <iterations>
            string path = args.Length > 1 ? args[1] : "monopoly_population.txt";
            int iterations = args.Length > 2 ? int.Parse(args[2]) : 1000;

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

        // Build 6 models from checkpoint paths (positional args or sensible
        // defaults), play N 6-player games with random seat assignments,
        // and report per-model win rates. Pool capped at 8 threads.
        static void RunShowdown(string[] args)
        {
            int games = args.Length > 1 ? int.Parse(args[1]) : 12;
            string neat = args.Length > 2 ? args[2] : "monopoly_population.txt";
            string es = args.Length > 3 ? args[3] : "monopoly_es.txt";
            string cmaes = args.Length > 4 ? args[4] : "monopoly_cmaes.txt";
            string psro = args.Length > 5 ? args[5] : "monopoly_psro.txt";
            string ppo = args.Length > 6 ? args[6] : "monopoly_ppo.txt";

            Console.WriteLine("Showdown: " + games + " games, 6 players, max 8 threads");
            MONOPOLY.IPolicy[] models = TRAINING.Showdown.BuildModels(neat, es, cmaes, psro, ppo);

            int[] wins = TRAINING.Showdown.Run(models, games);
            TRAINING.Showdown.PrintTally(wins, games);
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
