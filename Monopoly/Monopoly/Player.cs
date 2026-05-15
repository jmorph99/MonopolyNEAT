using System.Collections.Generic;

namespace MONOPOLY
{
    // Player is the in-game record of a participant: position, funds, items
    // they own, jail/double counters. Decision-making lives in IPolicy.
    public class Player
    {
        public enum EState
        {
            NORMAL,
            JAIL,
            RETIRED,
        }

        public enum EBuyDecision
        {
            BUY,
            AUCTION,
        }

        public enum EJailDecision
        {
            ROLL,
            PAY,
            CARD
        }

        public enum EDecision
        {
            YES,
            NO
        }

        public EState state = EState.NORMAL;

        public int position = 0;
        public int funds = 1500;

        public int jail = 0;
        public int doub = 0;

        public List<int> items;

        // Get-Out-of-Jail-Free cards the player currently holds. Tracked by
        // reference so they can be returned to the correct deck (Chance or
        // Community Chest) when used, transferred, or surrendered.
        public List<GetOutOfJailCard> heldCards;

        // Count of held jail cards. Kept as a read-only property so the
        // network projection has the same simple integer signal it used to,
        // and so external "do I have a card?" checks read naturally.
        public int card { get { return heldCards.Count; } }

        public Player()
        {
            items = new List<int>();
            heldCards = new List<GetOutOfJailCard>();
        }
    }
}
