using System.Collections.Generic;

namespace MONOPOLY
{
    // A Card is one entry in the Chance or Community Chest decks. Each
    // subclass owns its own effect; Board.DrawChance/DrawChest just rotate
    // the top of the deck and call Apply on the drawn card.
    //
    // Behavior is preserved exactly from the original DrawChance/DrawChest
    // switch statements, including the recursive ActivateTile() call on
    // Advance / BackThree / AdvanceToNearestTrain / AdvanceToNearestUtility.
    public abstract class Card
    {
        public abstract void Apply(Board board, int playerIdx);
    }

    public class AdvanceCard : Card
    {
        public int target;
        public AdvanceCard(int t) { target = t; }

        public override void Apply(Board board, int playerIdx)
        {
            Player p = board.players[playerIdx];

            if (p.position > target)
            {
                p.funds += Board.GO_BONUS;
            }

            p.position = target;

            board.ActivateTile();
        }
    }

    public class RewardCard : Card
    {
        public int amount;
        public RewardCard(int a) { amount = a; }

        public override void Apply(Board board, int playerIdx)
        {
            Player p = board.players[playerIdx];
            p.funds += amount;
        }
    }

    public class FineCard : Card
    {
        public int amount;
        public FineCard(int a) { amount = a; }

        public override void Apply(Board board, int playerIdx)
        {
            board.Payment(playerIdx, amount);
        }
    }

    public class BackThreeCard : Card
    {
        public override void Apply(Board board, int playerIdx)
        {
            Player p = board.players[playerIdx];
            p.position -= 3;
            // Defensive wrap: currently every Chance tile is at index >= 3
            // so this is a no-op, but the guard removes a latent fragility
            // if the tile layout ever changes.
            if (p.position < 0)
            {
                p.position += Board.BOARD_LENGTH;
            }

            board.ActivateTile();
        }
    }

    public class GetOutOfJailCard : Card
    {
        public override void Apply(Board board, int playerIdx)
        {
            Player p = board.players[playerIdx];
            p.card++;
        }
    }

    public class GoToJailCard : Card
    {
        public override void Apply(Board board, int playerIdx)
        {
            Player p = board.players[playerIdx];
            p.position = Board.JAIL_INDEX;
            p.doub = 0;
            p.state = Player.EState.JAIL;
        }
    }

    public class AdvanceToNearestTrainCard : Card
    {
        public override void Apply(Board board, int playerIdx)
        {
            board.AdvanceToTrain2();
        }
    }

    public class AdvanceToNearestUtilityCard : Card
    {
        public override void Apply(Board board, int playerIdx)
        {
            board.AdvanceToUtility10();
        }
    }

    public class ChairmanCard : Card
    {
        // Pay $50 to each non-retired other player.
        public override void Apply(Board board, int playerIdx)
        {
            for (int i = 0; i < Board.PLAYER_COUNT; i++)
            {
                if (i == playerIdx) continue;
                if (board.players[i].state == Player.EState.RETIRED) continue;

                board.PaymentToPlayer(playerIdx, i, 50);
            }
        }
    }

    public class BirthdayCard : Card
    {
        // Collect $10 from each non-retired other player.
        public override void Apply(Board board, int playerIdx)
        {
            for (int i = 0; i < Board.PLAYER_COUNT; i++)
            {
                if (i == playerIdx) continue;
                if (board.players[i].state == Player.EState.RETIRED) continue;

                board.PaymentToPlayer(i, playerIdx, 10);
            }
        }
    }

    // RepairsCard (Chance): $25/house, $100/hotel.
    // StreetRepairsCard (Chest): $40/house, $115/hotel.
    // Same shape, different rates — kept as two classes for clarity.
    public class RepairsCard : Card
    {
        public override void Apply(Board board, int playerIdx)
        {
            ApplyRepairs(board, playerIdx, 25, 100);
        }

        internal static void ApplyRepairs(Board board, int playerIdx, int perHouse, int perHotel)
        {
            int houseCount = 0;
            int hotelCount = 0;
            Player p = board.players[playerIdx];
            int itemCount = p.items.Count;

            for (int i = 0; i < itemCount; i++)
            {
                int index = p.items[i];

                if (board.houses[index] <= 4)
                {
                    houseCount += board.houses[index];
                }
                else
                {
                    hotelCount++;
                }
            }

            board.Payment(playerIdx, houseCount * perHouse + hotelCount * perHotel);
        }
    }

    public class StreetRepairsCard : Card
    {
        public override void Apply(Board board, int playerIdx)
        {
            RepairsCard.ApplyRepairs(board, playerIdx, 40, 115);
        }
    }

    // Static deck factories. The card lists are recreated for every Board.
    public static class Decks
    {
        public static List<Card> NewChance()
        {
            return new List<Card>
            {
                new AdvanceCard(39),
                new AdvanceCard(0),
                new AdvanceCard(24),
                new AdvanceCard(11),
                new AdvanceToNearestTrainCard(),
                new AdvanceToNearestTrainCard(),
                new AdvanceToNearestUtilityCard(),
                new RewardCard(50),
                new GetOutOfJailCard(),
                new BackThreeCard(),
                new GoToJailCard(),
                new RepairsCard(),
                new FineCard(15),
                new AdvanceCard(5),
                new ChairmanCard(),
                new RewardCard(150),
            };
        }

        public static List<Card> NewChest()
        {
            return new List<Card>
            {
                new AdvanceCard(0),
                new RewardCard(200),
                new FineCard(50),
                new RewardCard(50),
                new GetOutOfJailCard(),
                new GoToJailCard(),
                new RewardCard(100),
                new RewardCard(20),
                new BirthdayCard(),
                new RewardCard(100),
                new FineCard(100),
                new FineCard(50),
                new FineCard(25),
                new StreetRepairsCard(),
                new RewardCard(10),
                new RewardCard(100),
            };
        }
    }
}
