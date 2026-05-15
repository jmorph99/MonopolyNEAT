namespace MONOPOLY
{
    // ScriptedPolicy is the hard-coded baseline that the old `Player` class
    // exposed through virtual methods. Useful as a benchmark opponent when
    // evaluating a learned NeuralPolicy.
    public class ScriptedPolicy : IPolicy
    {
        public Player.EBuyDecision DecideBuy(Player self, int index)
        {
            return Player.EBuyDecision.BUY;
        }

        public Player.EJailDecision DecideJail(Player self)
        {
            return Player.EJailDecision.ROLL;
        }

        public Player.EDecision DecideMortgage(Player self, int index)
        {
            if (self.funds < 0)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAdvance(Player self, int index)
        {
            return Player.EDecision.YES;
        }

        public int DecideAuctionBid(Player self, int index)
        {
            return Board.COSTS[index];
        }

        public int DecideBuildHouse(Player self, int set)
        {
            return 15;
        }

        public int DecideSellHouse(Player self, int set)
        {
            if (self.funds < 0)
            {
                return 15;
            }
            return 0;
        }

        public Player.EDecision DecideOfferTrade(Player self)
        {
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAcceptTrade(Player self)
        {
            return Player.EDecision.NO;
        }
    }
}
