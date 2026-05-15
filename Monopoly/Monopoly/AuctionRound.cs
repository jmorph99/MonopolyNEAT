using System.Collections.Generic;

namespace MONOPOLY
{
    // AuctionRound runs a single tile auction: every non-retired player is
    // asked for a bid, those who bid more than they can afford drop out,
    // the highest bid wins (random tie-break), and the winner pays the
    // bank. If nobody can afford their bid, a random non-retired player
    // gets the tile for free — preserving the original fallback.
    //
    // Behavior is preserved exactly from Board.Auction(int).
    public static class AuctionRound
    {
        public static void Run(Board board, int index)
        {
            bool[] participation = new bool[board.player_count];
            for (int i = 0; i < board.player_count; i++)
            {
                participation[i] = board.players[i].state != Player.EState.RETIRED;
            }

            int[] bids = new int[board.player_count];
            for (int i = 0; i < board.player_count; i++)
            {
                bids[i] = board.policies[i].DecideAuctionBid(board, i, index);
                if (bids[i] > board.players[i].funds)
                {
                    participation[i] = false;
                }
            }

            int max = 0;
            for (int i = 0; i < board.player_count; i++)
            {
                if (participation[i] && bids[i] > max)
                {
                    max = bids[i];
                }
            }

            List<int> candidates = new List<int>();
            List<int> backup = new List<int>();
            for (int i = 0; i < board.player_count; i++)
            {
                if (participation[i] && bids[i] == max)
                {
                    candidates.Add(i);
                }
                if (board.players[i].state != Player.EState.RETIRED)
                {
                    backup.Add(i);
                }
            }

            if (candidates.Count > 0)
            {
                int winner = candidates[board.random.gen.Next(0, candidates.Count)];

                board.Payment(winner, max);
                board.owners[index] = winner;
                board.players[winner].items.Add(index);
                if (board.original[index] == -1) board.original[index] = winner;
            }
            else
            {
                int winner = backup[board.random.gen.Next(0, backup.Count)];

                board.owners[index] = winner;
                board.players[winner].items.Add(index);
                if (board.original[index] == -1) board.original[index] = winner;
            }
        }
    }
}
