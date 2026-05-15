using System;
using System.Threading;

namespace TRAINING
{
    // Arena is the game-playing primitive every trainer shares: hand it four
    // IEvaluator brains, get back a winner index (or -1 for draw). It owns
    // the Board setup and the win-detection mapping; trainers compose this
    // into whatever evaluation scheme they need (single-elimination bracket
    // for NEAT, batch self-play for ES, replay-buffer fill for DQN, ...).
    //
    // The shape of a Monopoly self-play game is fixed by the simulator: 4
    // seats, one IPolicy per seat. The trainer chooses which 4 brains face
    // each other and how their seats get shuffled.
    public static class Arena
    {
        // Outcome of a single game: the seat index (0..3) that won, or -1
        // for a draw / stalemate. The mapping from Board.EOutcome to seat
        // is canonical and lives here so every caller agrees.
        public static int PlayOne(MONOPOLY.IEvaluator[] networks)
        {
            if (networks == null || networks.Length != 4)
            {
                throw new ArgumentException("Arena.PlayOne requires exactly 4 networks");
            }

            MONOPOLY.IPolicy[] policies = new MONOPOLY.IPolicy[4];
            for (int p = 0; p < 4; p++)
            {
                policies[p] = new MONOPOLY.NeuralPolicy(networks[p]);
            }

            MONOPOLY.Board board = new MONOPOLY.Board(policies);
            MONOPOLY.Board.EOutcome outcome = MONOPOLY.Board.EOutcome.ONGOING;
            while (outcome == MONOPOLY.Board.EOutcome.ONGOING)
            {
                outcome = board.Step();
            }

            switch (outcome)
            {
                case MONOPOLY.Board.EOutcome.WIN1: return 0;
                case MONOPOLY.Board.EOutcome.WIN2: return 1;
                case MONOPOLY.Board.EOutcome.WIN3: return 2;
                case MONOPOLY.Board.EOutcome.WIN4: return 3;
                default: return -1;
            }
        }

        // PlayOne but with a callback that gets the finished Board (used by
        // PPO / DQN data collection: they need the per-turn trajectory the
        // policy emitted, not just the win flag). The callback runs on the
        // worker thread so it must be thread-safe.
        public static int PlayOneWithBoard(MONOPOLY.IEvaluator[] networks, Action<MONOPOLY.Board, int> onComplete)
        {
            MONOPOLY.IPolicy[] policies = new MONOPOLY.IPolicy[4];
            for (int p = 0; p < 4; p++)
            {
                policies[p] = new MONOPOLY.NeuralPolicy(networks[p]);
            }

            MONOPOLY.Board board = new MONOPOLY.Board(policies);
            MONOPOLY.Board.EOutcome outcome = MONOPOLY.Board.EOutcome.ONGOING;
            while (outcome == MONOPOLY.Board.EOutcome.ONGOING)
            {
                outcome = board.Step();
            }

            int winner;
            switch (outcome)
            {
                case MONOPOLY.Board.EOutcome.WIN1: winner = 0; break;
                case MONOPOLY.Board.EOutcome.WIN2: winner = 1; break;
                case MONOPOLY.Board.EOutcome.WIN3: winner = 2; break;
                case MONOPOLY.Board.EOutcome.WIN4: winner = 3; break;
                default: winner = -1; break;
            }

            onComplete?.Invoke(board, winner);
            return winner;
        }

        // Run `games` matches with the four fixed networks. Seat shuffle
        // happens once per game. Returns wins per *network* (not per seat),
        // i.e. wins[i] is how often networks[i] won regardless of which seat
        // it was assigned in each game. Single-threaded.
        public static int[] PlayMany(MONOPOLY.IEvaluator[] networks, int games)
        {
            int[] wins = new int[networks.Length];
            int[] seat = new int[networks.Length];

            for (int g = 0; g < games; g++)
            {
                for (int i = 0; i < networks.Length; i++) seat[i] = i;
                // Fisher-Yates shuffle
                for (int i = networks.Length - 1; i > 0; i--)
                {
                    int j = RNG.instance.gen.Next(0, i + 1);
                    (seat[i], seat[j]) = (seat[j], seat[i]);
                }

                MONOPOLY.IEvaluator[] seated = new MONOPOLY.IEvaluator[networks.Length];
                for (int i = 0; i < networks.Length; i++)
                {
                    seated[i] = networks[seat[i]];
                }

                int winnerSeat = PlayOne(seated);
                if (winnerSeat >= 0)
                {
                    wins[seat[winnerSeat]]++;
                }
            }

            return wins;
        }

        // Parallel version of PlayMany. Splits `games` across `workers`
        // threads and merges wins under a lock. Game RNG calls are guarded
        // by lock(RNG.instance.gen) in Board, so this is safe but the lock
        // becomes a contention point — the productive parallelism actually
        // comes from Board.Step doing its policy/network work outside that
        // lock. Mirrors the existing Tournament.PlayGameThread pattern.
        public static int[] PlayManyParallel(MONOPOLY.IEvaluator[] networks, int games, int workers)
        {
            int[] totalWins = new int[networks.Length];
            object sync = new object();

            int perWorker = games / workers;
            int remainder = games - perWorker * workers;

            Thread[] threads = new Thread[workers];
            for (int w = 0; w < workers; w++)
            {
                int budget = perWorker + (w < remainder ? 1 : 0);
                threads[w] = new Thread(() =>
                {
                    int[] localWins = PlayMany(networks, budget);
                    lock (sync)
                    {
                        for (int i = 0; i < localWins.Length; i++)
                        {
                            totalWins[i] += localWins[i];
                        }
                    }
                });
                threads[w].Start();
            }
            for (int w = 0; w < workers; w++) threads[w].Join();

            return totalWins;
        }
    }
}
