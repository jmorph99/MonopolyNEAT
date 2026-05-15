namespace MONOPOLY
{
    // IPolicy is the decision surface a Board calls when a player must choose
    // between options. Implementations: ScriptedPolicy (hard-coded baseline),
    // NeuralPolicy (NEAT-evolved network).
    //
    // `self` is the Player whose turn it is for the decision. Implementations
    // that condition on game state read it from there or from external context
    // they were given at construction (e.g. NetworkAdapter for NeuralPolicy).
    public interface IPolicy
    {
        Player.EBuyDecision DecideBuy(Player self, int index);
        Player.EJailDecision DecideJail(Player self);
        Player.EDecision DecideMortgage(Player self, int index);
        Player.EDecision DecideAdvance(Player self, int index);
        int DecideAuctionBid(Player self, int index);
        int DecideBuildHouse(Player self, int set);
        int DecideSellHouse(Player self, int set);
        Player.EDecision DecideOfferTrade(Player self);
        Player.EDecision DecideAcceptTrade(Player self);
    }
}
