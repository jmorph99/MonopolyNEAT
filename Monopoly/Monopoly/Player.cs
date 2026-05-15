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
        public int card = 0;

        public List<int> items;

        public Player()
        {
            items = new List<int>();
        }
    }
}
