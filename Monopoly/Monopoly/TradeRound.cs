using System;
using System.Collections.Generic;

namespace MONOPOLY
{
    // TradeRound runs the speculative trade phase at the start of a player's
    // turn: up to four random trade proposals between the current player
    // and a randomly-chosen other active player. Each proposal is offered
    // to the current player's policy; if accepted, it's offered to the
    // other player's policy. Both sides must say YES for the trade to fire.
    //
    // Behavior is preserved exactly from Board.Trading() — same RNG draws
    // in the same order, same TRADE_ATTEMPTS / TRADE_ITEM_MAX /
    // TRADE_MONEY_MAX constants, same ownership transfer and fund settlement.
    public static class TradeRound
    {
        public const int TRADE_ATTEMPTS = 4;
        public const int TRADE_ITEM_MAX = 5;
        public const int TRADE_MONEY_MAX = 500;

        public static void Run(Board board)
        {
            int turn = board.turn;

            List<Player> candidates = new List<Player>();
            List<int> candidates_index = new List<int>();

            for (int i = 0; i < Board.PLAYER_COUNT; i++)
            {
                if (i == turn) continue;
                if (board.players[i].state == Player.EState.RETIRED) continue;

                candidates.Add(board.players[i]);
                candidates_index.Add(i);
            }

            if (candidates.Count == 0)
            {
                return;
            }

            for (int t = 0; t < TRADE_ATTEMPTS; t++)
            {
                int give = board.random.gen.Next(0, Math.Min(board.players[turn].items.Count, TRADE_ITEM_MAX));
                int selectedPlayer = board.random.gen.Next(0, candidates.Count);

                Player other = candidates[selectedPlayer];
                int other_index = candidates_index[selectedPlayer];

                int recieve = board.random.gen.Next(0, Math.Min(other.items.Count, TRADE_ITEM_MAX));

                if (board.players[turn].funds < 0 || other.funds < 0)
                {
                    continue;
                }

                int moneyGive = board.random.gen.Next(0, Math.Min(board.players[turn].funds, TRADE_MONEY_MAX));
                int moneyRecieve = board.random.gen.Next(0, Math.Min(other.funds, TRADE_MONEY_MAX));
                int moneyBalance = moneyGive - moneyRecieve;

                if (give == 0 || recieve == 0)
                {
                    continue;
                }

                List<int> gift = new List<int>();
                List<int> possible = new List<int>(board.players[turn].items);

                for (int i = 0; i < give; i++)
                {
                    int selection = board.random.gen.Next(0, possible.Count);
                    gift.Add(possible[selection]);
                    possible.RemoveAt(selection);
                }

                List<int> returning = new List<int>();
                possible = new List<int>(other.items);

                for (int i = 0; i < recieve; i++)
                {
                    int selection = board.random.gen.Next(0, possible.Count);
                    returning.Add(possible[selection]);
                    possible.RemoveAt(selection);
                }

                int[] giftArr = gift.ToArray();
                int[] returningArr = returning.ToArray();

                Player.EDecision decision = board.policies[turn].DecideOfferTrade(board, turn, giftArr, returningArr, moneyBalance);
                if (decision == Player.EDecision.NO) continue;

                Player.EDecision decision2 = board.policies[other_index].DecideAcceptTrade(board, other_index, giftArr, returningArr, moneyBalance);
                if (decision2 == Player.EDecision.NO) continue;

                for (int i = 0; i < gift.Count; i++)
                {
                    Monopoly.Analytics.instance.MadeTrade(gift[i]);
                    board.players[turn].items.Remove(gift[i]);
                    other.items.Add(gift[i]);
                    board.owners[gift[i]] = other_index;
                }

                for (int i = 0; i < returning.Count; i++)
                {
                    Monopoly.Analytics.instance.MadeTrade(returning[i]);
                    other.items.Remove(returning[i]);
                    board.players[turn].items.Add(returning[i]);
                    board.owners[returning[i]] = turn;
                }

                board.players[turn].funds -= moneyBalance;
                other.funds += moneyBalance;
            }
        }
    }
}
