namespace MONOPOLY
{
    // IPolicy is the decision surface a Board calls when a player must choose
    // between options. Implementations: ScriptedPolicy (hard-coded baseline),
    // NeuralPolicy (NEAT-evolved network).
    //
    // Each method receives the Board and the index of the player being asked.
    // Implementations that need the network's view of state call
    // Projection.Project(board, playerIdx, ...) to build their input vector.
    // Implementations that don't need the network read fields from the Board
    // directly.
    public interface IPolicy
    {
        Player.EBuyDecision DecideBuy(Board board, int playerIdx, int tileIdx);
        Player.EJailDecision DecideJail(Board board, int playerIdx);
        Player.EDecision DecideMortgage(Board board, int playerIdx, int tileIdx);
        Player.EDecision DecideAdvance(Board board, int playerIdx, int tileIdx);
        int DecideAuctionBid(Board board, int playerIdx, int tileIdx);
        int DecideBuildHouse(Board board, int playerIdx, int set);
        int DecideSellHouse(Board board, int playerIdx, int set);
        Player.EDecision DecideOfferTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance);
        Player.EDecision DecideAcceptTrade(Board board, int playerIdx, int[] giving, int[] receiving, int moneyBalance);
    }
}
