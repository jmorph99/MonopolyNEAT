using System;

namespace MONOPOLY
{
    // NeuralPolicy is the IPolicy adapter for any IEvaluator (NEAT phenotype,
    // fixed-topology MLP, ONNX-backed net, ...). For each decision it builds
    // the network's 127-float input via Projection.Project, propagates the
    // network, and thresholds the outputs into the discrete decisions Board
    // asks for. Output mapping is fixed:
    //   Y[0] buy        Y[1] jail (3-way)   Y[2] mortgage
    //   Y[3] advance    Y[4] auction bid    Y[5] build house
    //   Y[6] sell house Y[7] offer trade    Y[8] accept trade
    public class NeuralPolicy : IPolicy
    {
        public IEvaluator network;

        public NeuralPolicy(IEvaluator net)
        {
            network = net;
        }

        // Back-compat constructor: callers that still hand in a NEAT.Phenotype
        // directly get auto-wrapped. Lets the NEAT trainer keep its existing
        // call sites unchanged.
        public NeuralPolicy(NEAT.Phenotype net)
        {
            network = new PhenotypeEvaluator(net);
        }

        public Player.EBuyDecision DecideBuy(Board board, int playerIdx, int tileIdx)
        {
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Tile(tileIdx));
            float[] Y = network.Propagate(pack);

            if (Y[0] > 0.5f)
            {
                return Player.EBuyDecision.BUY;
            }
            return Player.EBuyDecision.AUCTION;
        }

        public Player.EJailDecision DecideJail(Board board, int playerIdx)
        {
            float[] pack = Projection.Project(board, playerIdx);
            float[] Y = network.Propagate(pack);

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

        public Player.EDecision DecideMortgage(Board board, int playerIdx, int tileIdx)
        {
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Tile(tileIdx));
            float[] Y = network.Propagate(pack);

            if (Y[2] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAdvance(Board board, int playerIdx, int tileIdx)
        {
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Tile(tileIdx));
            float[] Y = network.Propagate(pack);

            if (Y[3] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public int DecideAuctionBid(Board board, int playerIdx, int tileIdx)
        {
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Tile(tileIdx));
            float[] Y = network.Propagate(pack);

            float result = Y[4];
            float money = Projection.ConvertMoneyValue(result);

            Monopoly.Analytics.instance.MakeBid(tileIdx, (int)money);

            return (int)money;
        }

        public int DecideBuildHouse(Board board, int playerIdx, int set)
        {
            // First property in the set carries the selection flag, matching
            // the original adapter pattern.
            int firstInSet = Board.SETS[set, 0];
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Tile(firstInSet));
            float[] Y = network.Propagate(pack);

            float result = Y[5];
            float money = Projection.ConvertHouseValue(result);

            return (int)money;
        }

        public int DecideSellHouse(Board board, int playerIdx, int set)
        {
            int firstInSet = Board.SETS[set, 0];
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Tile(firstInSet));
            float[] Y = network.Propagate(pack);

            float result = Y[6];
            float money = Projection.ConvertHouseValue(result);

            return (int)money;
        }

        public Player.EDecision DecideOfferTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            int[] flagged = ConcatTiles(giving, receiving);
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Trade(flagged, moneyBalance));
            float[] Y = network.Propagate(pack);

            if (Y[7] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        public Player.EDecision DecideAcceptTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance)
        {
            int[] flagged = ConcatTiles(giving, receiving);
            float[] pack = Projection.Project(board, playerIdx, DecisionContext.Trade(flagged, moneyBalance));
            float[] Y = network.Propagate(pack);

            if (Y[8] > 0.5f)
            {
                return Player.EDecision.YES;
            }
            return Player.EDecision.NO;
        }

        private static int[] ConcatTiles(int[] a, int[] b)
        {
            int[] result = new int[a.Length + b.Length];
            Array.Copy(a, 0, result, 0, a.Length);
            Array.Copy(b, 0, result, a.Length, b.Length);
            return result;
        }
    }
}
