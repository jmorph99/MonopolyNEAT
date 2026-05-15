namespace MONOPOLY
{
    // ScriptedPolicy is the hard-coded baseline that the old `Player` class
    // exposed through virtual methods. Useful as a benchmark opponent when
    // evaluating a learned NeuralPolicy. Doesn't need the network input
    // projection — reads game state from the Board directly.
    public class ScriptedPolicy : IPolicy
    {
        public Player.EBuyDecision DecideBuy(Board board, int playerIdx, int tileIdx)
        {
            return Player.EBuyDecision.BUY;
        }

        public Player.EJailDecision DecideJail(Board board, int playerIdx)
        {
            return Player.EJailDecision.ROLL;
        }

        public Player.EDecision DecideMortgage(Board board, int playerIdx, int tileIdx)
        {
            if (board.players[playerIdx].funds < 0)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAdvance(Board board, int playerIdx, int tileIdx)
        {
            return Player.EDecision.YES;
        }

        public int DecideAuctionBid(Board board, int playerIdx, int tileIdx)
        {
            return Board.COSTS[tileIdx];
        }

        public int DecideBuildHouse(Board board, int playerIdx, int set)
        {
            return 15;
        }

        public int DecideSellHouse(Board board, int playerIdx, int set)
        {
            if (board.players[playerIdx].funds < 0)
            {
                return 15;
            }
            return 0;
        }

        public Player.EDecision DecideOfferTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAcceptTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            return Player.EDecision.NO;
        }
    }
}
