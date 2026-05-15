namespace MONOPOLY
{
    // NeuralPolicy is the IPolicy adapter for a NEAT.Phenotype. It reads the
    // current network input from the shared NetworkAdapter, propagates the
    // network, and thresholds the outputs into the discrete decisions Board
    // asks for. Output mapping is fixed:
    //   Y[0] buy        Y[1] jail (3-way)   Y[2] mortgage
    //   Y[3] advance    Y[4] auction bid    Y[5] build house
    //   Y[6] sell house Y[7] offer trade    Y[8] accept trade
    public class NeuralPolicy : IPolicy
    {
        public NEAT.Phenotype network;
        public NetworkAdapter adapter;

        public NeuralPolicy(NEAT.Phenotype net, NetworkAdapter ad)
        {
            network = net;
            adapter = ad;
        }

        public Player.EBuyDecision DecideBuy(Player self, int index)
        {
            float[] Y = network.Propagate(adapter.pack);

            if (Y[0] > 0.5f)
            {
                return Player.EBuyDecision.BUY;
            }
            return Player.EBuyDecision.AUCTION;
        }

        public Player.EJailDecision DecideJail(Player self)
        {
            float[] Y = network.Propagate(adapter.pack);

            if (Y[1] < 0.333f)
            {
                return Player.EJailDecision.CARD;
            }
            else if (Y[1] < 0.666f)
            {
                return Player.EJailDecision.ROLL;
            }
            return Player.EJailDecision.PAY;
        }

        public Player.EDecision DecideMortgage(Player self, int index)
        {
            float[] Y = network.Propagate(adapter.pack);

            if (Y[2] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAdvance(Player self, int index)
        {
            float[] Y = network.Propagate(adapter.pack);

            if (Y[3] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public int DecideAuctionBid(Player self, int index)
        {
            float[] Y = network.Propagate(adapter.pack);

            float result = Y[4];
            float money = adapter.ConvertMoneyValue(result);

            Monopoly.Analytics.instance.MakeBid(index, (int)money);

            return (int)money;
        }

        public int DecideBuildHouse(Player self, int set)
        {
            float[] Y = network.Propagate(adapter.pack);

            float result = Y[5];
            float money = adapter.ConvertHouseValue(result);

            return (int)money;
        }

        public int DecideSellHouse(Player self, int set)
        {
            float[] Y = network.Propagate(adapter.pack);

            float result = Y[6];
            float money = adapter.ConvertHouseValue(result);

            return (int)money;
        }

        public Player.EDecision DecideOfferTrade(Player self)
        {
            float[] Y = network.Propagate(adapter.pack);

            if (Y[7] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAcceptTrade(Player self)
        {
            float[] Y = network.Propagate(adapter.pack);

            if (Y[8] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }
    }
}
