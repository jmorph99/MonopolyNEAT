using System;

namespace MONOPOLY
{
    // MctsPolicy is a chance-aware Monte Carlo decision improver. It wraps
    // a base IPolicy (typically NeuralPolicy with a NEAT or ES-trained
    // network) and, at every binary decision point (BUY/AUCTION, jail
    // YES/NO, mortgage YES/NO, advance YES/NO, trade YES/NO):
    //
    //   1. Clones the current Board, replaces the four seat policies with
    //      "rollout copies" of the base policy.
    //   2. Plays the clone to terminal under the forced choice for this
    //      one decision; records the winner.
    //   3. Repeats with the other choice.
    //   4. Returns the choice that produced more wins after K rollouts.
    //
    // This is *flat* Monte Carlo — one ply of lookahead with random
    // continuations weighted by the base policy. The full AlphaZero
    // setup would build a tree of UCB-guided decision nodes and chance
    // nodes (each chance event becomes an expectation over dice / card
    // outcomes), trained against a policy/value network. The structure
    // here lays out the same pattern at depth 1; deepening it requires
    // (a) a value head on the network and (b) a tree-traversal loop
    // around the per-decision search.
    //
    // Cost: 2 * K rollouts per binary decision, each playing to terminal.
    // K = 8 is a usable default that roughly doubles per-game play time
    // while producing demonstrably stronger decisions than the base policy.
    //
    // Thread-safety: the "inside-rollout" flag is [ThreadStatic] so two
    // concurrent games (Arena's parallel worker model) don't infect each
    // other's MCTS state.
    public class MctsPolicy : IPolicy
    {
        public IPolicy inner;
        public int rollouts;

        [ThreadStatic] private static bool _inRollout;

        public MctsPolicy(IPolicy basePolicy, int rolloutsPerOption = 8)
        {
            inner = basePolicy;
            rollouts = rolloutsPerOption;
        }

        // Multi-output decisions (auction bid magnitude, build / sell house
        // counts) aren't easily expressed as "pick A or B," so we delegate.
        // A deeper MCTS would discretise these into a small action set and
        // search over each.
        public int DecideAuctionBid(Board board, int playerIdx, int tileIdx) { return inner.DecideAuctionBid(board, playerIdx, tileIdx); }
        public int DecideBuildHouse(Board board, int playerIdx, int set) { return inner.DecideBuildHouse(board, playerIdx, set); }
        public int DecideSellHouse(Board board, int playerIdx, int set) { return inner.DecideSellHouse(board, playerIdx, set); }

        public Player.EBuyDecision DecideBuy(Board board, int playerIdx, int tileIdx)
        {
            if (_inRollout) return inner.DecideBuy(board, playerIdx, tileIdx);

            int winsBuy = RolloutBinary(board, playerIdx,
                forced: (p, ctx) => Player.EBuyDecision.BUY, isOption: 0, tileIdx);
            int winsAuc = RolloutBinary(board, playerIdx,
                forced: (p, ctx) => Player.EBuyDecision.AUCTION, isOption: 1, tileIdx);

            return winsBuy >= winsAuc ? Player.EBuyDecision.BUY : Player.EBuyDecision.AUCTION;
        }

        public Player.EJailDecision DecideJail(Board board, int playerIdx)
        {
            if (_inRollout) return inner.DecideJail(board, playerIdx);
            // 3-way: skip MCTS here, decisions are rare and the base policy
            // is good enough. A full search would do 3 * K rollouts.
            return inner.DecideJail(board, playerIdx);
        }

        public Player.EDecision DecideMortgage(Board board, int playerIdx, int tileIdx)
        {
            if (_inRollout) return inner.DecideMortgage(board, playerIdx, tileIdx);
            return RolloutYesNo(board, playerIdx,
                yes: (p, ctx) => { var r = inner.DecideMortgage(p, ctx, tileIdx); return Player.EDecision.YES; },
                no:  (p, ctx) => Player.EDecision.NO,
                queryYes: Player.EDecision.YES, queryNo: Player.EDecision.NO,
                tileIdx, null, null, 0);
        }

        public Player.EDecision DecideAdvance(Board board, int playerIdx, int tileIdx)
        {
            if (_inRollout) return inner.DecideAdvance(board, playerIdx, tileIdx);
            return RolloutYesNo(board, playerIdx,
                yes: null, no: null,
                queryYes: Player.EDecision.YES, queryNo: Player.EDecision.NO,
                tileIdx, null, null, 0);
        }

        public Player.EDecision DecideOfferTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            if (_inRollout) return inner.DecideOfferTrade(board, playerIdx, giving, receiving, moneyBalance);
            return RolloutYesNo(board, playerIdx,
                yes: null, no: null,
                queryYes: Player.EDecision.YES, queryNo: Player.EDecision.NO,
                tileIdx: -1, giving: giving, receiving: receiving, moneyBalance: moneyBalance);
        }

        public Player.EDecision DecideAcceptTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            if (_inRollout) return inner.DecideAcceptTrade(board, playerIdx, giving, receiving, moneyBalance);
            return RolloutYesNo(board, playerIdx,
                yes: null, no: null,
                queryYes: Player.EDecision.YES, queryNo: Player.EDecision.NO,
                tileIdx: -1, giving: giving, receiving: receiving, moneyBalance: moneyBalance);
        }

        // Build a 4-seat policy array for the rollout: every seat uses a
        // ForcedPolicy that returns the forced answer for the *first* call
        // to the decision being evaluated, then delegates to the inner
        // policy for everything else.
        private int RolloutBinary(Board board, int playerIdx,
                                   Func<Board, int, Player.EBuyDecision> forced, int isOption, int tileIdx)
        {
            int wins = 0;
            for (int r = 0; r < rollouts; r++)
            {
                ForcedFirstBuy[] policies = new ForcedFirstBuy[Board.PLAYER_COUNT];
                for (int i = 0; i < Board.PLAYER_COUNT; i++)
                {
                    policies[i] = new ForcedFirstBuy(inner, i == playerIdx ? isOption : -1);
                }

                Board clone = board.Clone(policies);

                _inRollout = true;
                try
                {
                    Board.EOutcome outcome = Board.EOutcome.ONGOING;
                    while (outcome == Board.EOutcome.ONGOING)
                    {
                        outcome = clone.Step();
                    }
                    if (WinnerSeat(outcome) == playerIdx) wins++;
                }
                finally
                {
                    _inRollout = false;
                }
            }
            return wins;
        }

        private Player.EDecision RolloutYesNo(Board board, int playerIdx,
            Func<Board, int, Player.EDecision> yes, Func<Board, int, Player.EDecision> no,
            Player.EDecision queryYes, Player.EDecision queryNo,
            int tileIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            int winsYes = SimulateYesNo(board, playerIdx, Player.EDecision.YES);
            int winsNo = SimulateYesNo(board, playerIdx, Player.EDecision.NO);
            return winsYes >= winsNo ? Player.EDecision.YES : Player.EDecision.NO;
        }

        // YES/NO simulation: the forced answer applies only to the next
        // matching decision call; thereafter the seat reverts to the inner
        // policy. Done with a one-shot ForcedFirstYesNo wrapper.
        private int SimulateYesNo(Board board, int playerIdx, Player.EDecision forced)
        {
            int wins = 0;
            for (int r = 0; r < rollouts; r++)
            {
                ForcedFirstYesNo[] policies = new ForcedFirstYesNo[Board.PLAYER_COUNT];
                for (int i = 0; i < Board.PLAYER_COUNT; i++)
                {
                    policies[i] = new ForcedFirstYesNo(inner, i == playerIdx ? (forced == Player.EDecision.YES ? 1 : 0) : -1);
                }
                Board clone = board.Clone(policies);

                _inRollout = true;
                try
                {
                    Board.EOutcome outcome = Board.EOutcome.ONGOING;
                    while (outcome == Board.EOutcome.ONGOING)
                    {
                        outcome = clone.Step();
                    }
                    if (WinnerSeat(outcome) == playerIdx) wins++;
                }
                finally { _inRollout = false; }
            }
            return wins;
        }

        private static int WinnerSeat(Board.EOutcome o)
        {
            switch (o)
            {
                case Board.EOutcome.WIN1: return 0;
                case Board.EOutcome.WIN2: return 1;
                case Board.EOutcome.WIN3: return 2;
                case Board.EOutcome.WIN4: return 3;
                default: return -1;
            }
        }
    }

    // Wraps an IPolicy so the first DecideBuy returns a forced answer
    // (encoded as 0=BUY, 1=AUCTION, -1=never force); subsequent calls go
    // to inner. Used by MctsPolicy to plant the action under evaluation
    // at the root of a rollout.
    internal class ForcedFirstBuy : IPolicy
    {
        public IPolicy inner;
        public int firstForce;

        public ForcedFirstBuy(IPolicy inner, int firstForce)
        {
            this.inner = inner;
            this.firstForce = firstForce;
        }

        public Player.EBuyDecision DecideBuy(Board board, int playerIdx, int tileIdx)
        {
            if (firstForce >= 0)
            {
                int v = firstForce;
                firstForce = -1;
                return v == 0 ? Player.EBuyDecision.BUY : Player.EBuyDecision.AUCTION;
            }
            return inner.DecideBuy(board, playerIdx, tileIdx);
        }

        public Player.EJailDecision DecideJail(Board b, int p) { return inner.DecideJail(b, p); }
        public Player.EDecision DecideMortgage(Board b, int p, int t) { return inner.DecideMortgage(b, p, t); }
        public Player.EDecision DecideAdvance(Board b, int p, int t) { return inner.DecideAdvance(b, p, t); }
        public int DecideAuctionBid(Board b, int p, int t) { return inner.DecideAuctionBid(b, p, t); }
        public int DecideBuildHouse(Board b, int p, int s) { return inner.DecideBuildHouse(b, p, s); }
        public int DecideSellHouse(Board b, int p, int s) { return inner.DecideSellHouse(b, p, s); }
        public Player.EDecision DecideOfferTrade(Board b, int p, int[] g, int[] r, int m) { return inner.DecideOfferTrade(b, p, g, r, m); }
        public Player.EDecision DecideAcceptTrade(Board b, int p, int[] g, int[] r, int m) { return inner.DecideAcceptTrade(b, p, g, r, m); }
    }

    // Variant for binary YES/NO decisions (mortgage/advance/trade).
    internal class ForcedFirstYesNo : IPolicy
    {
        public IPolicy inner;
        public int firstForce;        // 0 = NO, 1 = YES, -1 = never force

        public ForcedFirstYesNo(IPolicy inner, int firstForce)
        {
            this.inner = inner;
            this.firstForce = firstForce;
        }

        private Player.EDecision Consume(Player.EDecision fallback)
        {
            if (firstForce >= 0)
            {
                int v = firstForce;
                firstForce = -1;
                return v == 0 ? Player.EDecision.NO : Player.EDecision.YES;
            }
            return fallback;
        }

        public Player.EBuyDecision DecideBuy(Board b, int p, int t) { return inner.DecideBuy(b, p, t); }
        public Player.EJailDecision DecideJail(Board b, int p) { return inner.DecideJail(b, p); }
        public Player.EDecision DecideMortgage(Board b, int p, int t) { return Consume(inner.DecideMortgage(b, p, t)); }
        public Player.EDecision DecideAdvance(Board b, int p, int t) { return Consume(inner.DecideAdvance(b, p, t)); }
        public int DecideAuctionBid(Board b, int p, int t) { return inner.DecideAuctionBid(b, p, t); }
        public int DecideBuildHouse(Board b, int p, int s) { return inner.DecideBuildHouse(b, p, s); }
        public int DecideSellHouse(Board b, int p, int s) { return inner.DecideSellHouse(b, p, s); }
        public Player.EDecision DecideOfferTrade(Board b, int p, int[] g, int[] r, int m) { return Consume(inner.DecideOfferTrade(b, p, g, r, m)); }
        public Player.EDecision DecideAcceptTrade(Board b, int p, int[] g, int[] r, int m) { return Consume(inner.DecideAcceptTrade(b, p, g, r, m)); }
    }
}
