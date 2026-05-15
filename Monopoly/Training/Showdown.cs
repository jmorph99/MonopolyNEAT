using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TRAINING
{
    // Showdown runs N 6-player games where each seat is occupied by a
    // policy backed by a different training method. Reports per-model
    // win rates after every game and a final tally.
    //
    // Models considered:
    //   0  NEAT       (loaded from NEAT population file; uses population[0])
    //   1  ES         (loaded from ES checkpoint)
    //   2  CMA-ES     (loaded from CMA-ES checkpoint)
    //   3  PSRO       (loaded from PSRO checkpoint, uses the current mean)
    //   4  PPO        (loaded from PPO checkpoint)
    //   5  Scripted   (always available)
    //
    // Missing or unparseable checkpoints fall back to ScriptedPolicy for
    // that seat — Showdown still runs.
    //
    // Each game uses a fresh random seat permutation so no model has a
    // permanent seat-1 advantage. Win counts are tallied per *model*,
    // not per seat. Parallelised with a worker-stealing pool capped at
    // MAX_WORKERS threads.
    public static class Showdown
    {
        public const int MAX_WORKERS = 8;
        public const int MODEL_COUNT = 6;

        public static readonly string[] LABELS = new string[]
        {
            "NEAT", "ES", "CMAES", "PSRO", "PPO", "Scripted"
        };

        // Build the 6 model templates. policies[m] is the IPolicy for model m;
        // null entries (none here — Scripted is always present) would be
        // skipped. All policy implementations are read-only / thread-safe
        // (Phenotype.Propagate and MLP.Propagate both allocate per-call
        // workspaces) so the same instance can be shared across worker games.
        public static MONOPOLY.IPolicy[] BuildModels(string neatPath, string esPath, string cmaesPath, string psroPath, string ppoPath)
        {
            MONOPOLY.IPolicy[] models = new MONOPOLY.IPolicy[MODEL_COUNT];

            models[0] = TryLoadNeat(neatPath) ?? FallbackScripted("NEAT", neatPath);
            models[1] = TryLoadEs(esPath) ?? FallbackScripted("ES", esPath);
            models[2] = TryLoadCmaEs(cmaesPath) ?? FallbackScripted("CMAES", cmaesPath);
            models[3] = TryLoadPsro(psroPath) ?? FallbackScripted("PSRO", psroPath);
            models[4] = TryLoadPpo(ppoPath) ?? FallbackScripted("PPO", ppoPath);
            models[5] = new MONOPOLY.ScriptedPolicy();
            return models;
        }

        private static MONOPOLY.IPolicy FallbackScripted(string label, string path)
        {
            Console.WriteLine("  [" + label + "] checkpoint missing or unreadable (" + path + ") -> ScriptedPolicy");
            return new MONOPOLY.ScriptedPolicy();
        }

        private static MONOPOLY.IPolicy TryLoadNeat(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                // NEAT load relies on global singletons populated in Program.Main.
                float score;
                NEAT.Population.instance.Load(path, out score);
                if (NEAT.Population.instance.population.Count == 0) return null;
                NEAT.Phenotype p = NEAT.Population.instance.population[0];
                Console.WriteLine("  [NEAT] loaded gen=" + NEAT.Population.instance.GENERATION
                    + " species=" + NEAT.Population.instance.species.Count
                    + " from " + path);
                return new MONOPOLY.NeuralPolicy(p);
            }
            catch (Exception e) { Console.WriteLine("  [NEAT] load error: " + e.Message); return null; }
        }

        private static MONOPOLY.IPolicy TryLoadEs(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                EsTrainer t = new EsTrainer();
                if (!t.Load(path)) return null;
                Console.WriteLine("  [ES] loaded gen=" + t.Generation + " from " + path);
                return new MONOPOLY.NeuralPolicy(t.policy);
            }
            catch (Exception e) { Console.WriteLine("  [ES] load error: " + e.Message); return null; }
        }

        private static MONOPOLY.IPolicy TryLoadCmaEs(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                CmaEsTrainer t = new CmaEsTrainer();
                if (!t.Load(path)) return null;
                Console.WriteLine("  [CMAES] loaded gen=" + t.Generation + " from " + path);
                return new MONOPOLY.NeuralPolicy(t.policy);
            }
            catch (Exception e) { Console.WriteLine("  [CMAES] load error: " + e.Message); return null; }
        }

        private static MONOPOLY.IPolicy TryLoadPsro(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                LeagueTrainer t = new LeagueTrainer();
                if (!t.Load(path)) return null;
                Console.WriteLine("  [PSRO] loaded gen=" + t.Generation + " league=" + t.league.Count + " from " + path);
                return new MONOPOLY.NeuralPolicy(t.policy);
            }
            catch (Exception e) { Console.WriteLine("  [PSRO] load error: " + e.Message); return null; }
        }

        private static MONOPOLY.IPolicy TryLoadPpo(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                PpoTrainer t = new PpoTrainer();
                if (!t.Load(path)) return null;
                Console.WriteLine("  [PPO] loaded gen=" + t.Generation + " from " + path);
                return new MONOPOLY.NeuralPolicy(t.policy);
            }
            catch (Exception e) { Console.WriteLine("  [PPO] load error: " + e.Message); return null; }
        }

        // Play `games` matches; in each, model m is randomly assigned to
        // a seat 0..5. Returns wins[m] = total wins of model m across all
        // games.
        public static int[] Run(MONOPOLY.IPolicy[] models, int games)
        {
            if (models == null || models.Length != MODEL_COUNT)
            {
                throw new ArgumentException("Showdown.Run expects exactly " + MODEL_COUNT + " models");
            }

            int[] wins = new int[MODEL_COUNT];
            int[] played = new int[MODEL_COUNT];     // games-played per model (== games, but useful if seats < models)
            int[] seatedAt = new int[MODEL_COUNT];   // last-seat-each (debug)
            object sync = new object();
            int nextGame = 0;

            int workers = Math.Min(MAX_WORKERS, Math.Max(1, games));
            Thread[] threads = new Thread[workers];
            for (int t = 0; t < workers; t++)
            {
                threads[t] = new Thread(() =>
                {
                    Random localRng = new Random();
                    while (true)
                    {
                        int g;
                        lock (sync)
                        {
                            if (nextGame >= games) return;
                            g = nextGame++;
                        }

                        // Build a random permutation of model indices for the 6 seats.
                        int[] modelAtSeat = new int[MODEL_COUNT];
                        for (int i = 0; i < MODEL_COUNT; i++) modelAtSeat[i] = i;
                        for (int i = MODEL_COUNT - 1; i > 0; i--)
                        {
                            int j = localRng.Next(0, i + 1);
                            (modelAtSeat[i], modelAtSeat[j]) = (modelAtSeat[j], modelAtSeat[i]);
                        }

                        MONOPOLY.IPolicy[] seatPolicies = new MONOPOLY.IPolicy[MODEL_COUNT];
                        for (int s = 0; s < MODEL_COUNT; s++) seatPolicies[s] = models[modelAtSeat[s]];

                        MONOPOLY.Board board = new MONOPOLY.Board(seatPolicies);
                        MONOPOLY.Board.EOutcome outcome = MONOPOLY.Board.EOutcome.ONGOING;
                        while (outcome == MONOPOLY.Board.EOutcome.ONGOING) outcome = board.Step();

                        int winnerSeat = MONOPOLY.Board.SeatForWinOutcome(outcome);
                        lock (sync)
                        {
                            for (int s = 0; s < MODEL_COUNT; s++) played[modelAtSeat[s]]++;
                            if (winnerSeat >= 0)
                            {
                                int winnerModel = modelAtSeat[winnerSeat];
                                wins[winnerModel]++;
                                Console.WriteLine("  game " + g + ": winner=" + LABELS[winnerModel] + " (seat " + winnerSeat + ")");
                            }
                            else
                            {
                                Console.WriteLine("  game " + g + ": draw");
                            }
                        }
                    }
                });
                threads[t].Start();
            }
            for (int t = 0; t < workers; t++) threads[t].Join();

            return wins;
        }

        public static void PrintTally(int[] wins, int games)
        {
            Console.WriteLine("");
            Console.WriteLine("====== Showdown final tally over " + games + " games ======");
            for (int m = 0; m < MODEL_COUNT; m++)
            {
                float rate = games > 0 ? (float)wins[m] / games : 0.0f;
                Console.WriteLine(string.Format("  {0,-10} wins={1,4}  rate={2:0.000}", LABELS[m], wins[m], rate));
            }
            Console.WriteLine("==============================================");
        }
    }
}
