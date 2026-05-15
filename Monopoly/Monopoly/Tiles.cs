namespace MONOPOLY
{
    // A Tile is one of the 40 spaces around the board. Activate runs the
    // rule for landing on it: buy/auction for purchasable tiles, rent
    // collection for owned ones, draw a card, pay tax, go to jail, etc.
    //
    // Behavior is preserved exactly from the original ActivateTile() switch.
    // That includes one subtle quirk in TrainTile: it has no "owner is me, do
    // nothing" branch, so landing on your own non-mortgaged train calls
    // PaymentToPlayer(self, self) — a net-zero round-trip that the original
    // code performs and the trained networks have learned around.
    public abstract class Tile
    {
        public int index;
        protected Tile(int i) { index = i; }
        public abstract void Activate(Board board, int playerIdx);
    }

    public class NoOpTile : Tile
    {
        public NoOpTile(int i) : base(i) { }
        public override void Activate(Board board, int playerIdx) { }
    }

    public class ChanceTile : Tile
    {
        public ChanceTile(int i) : base(i) { }
        public override void Activate(Board board, int playerIdx)
        {
            board.DrawChance();
        }
    }

    public class ChestTile : Tile
    {
        public ChestTile(int i) : base(i) { }
        public override void Activate(Board board, int playerIdx)
        {
            board.DrawChest();
        }
    }

    public class TaxTile : Tile
    {
        public TaxTile(int i) : base(i) { }
        public override void Activate(Board board, int playerIdx)
        {
            board.Payment(playerIdx, Board.COSTS[index]);
        }
    }

    public class GoToJailTile : Tile
    {
        public GoToJailTile(int i) : base(i) { }
        public override void Activate(Board board, int playerIdx)
        {
            Player p = board.players[playerIdx];
            p.position = Board.JAIL_INDEX;
            p.doub = 0;
            p.state = Player.EState.JAIL;
        }
    }

    public class PropertyTile : Tile
    {
        public PropertyTile(int i) : base(i) { }

        public override void Activate(Board board, int playerIdx)
        {
            int owner = board.owners[index];

            if (owner == Board.BANK_INDEX)
            {
                Player.EBuyDecision decision = board.policies[playerIdx].DecideBuy(board, playerIdx, index);

                if (decision == Player.EBuyDecision.BUY)
                {
                    if (board.players[playerIdx].funds < Board.COSTS[index])
                    {
                        AuctionRound.Run(board, index);
                    }
                    else
                    {
                        board.Payment(playerIdx, Board.COSTS[index]);
                        board.owners[index] = playerIdx;
                        if (board.original[index] == -1) board.original[index] = playerIdx;
                        board.players[playerIdx].items.Add(index);
                        // Preserved verbatim: PROPERTY sets the owner with the
                        // pre-purchase `owner` (BANK_INDEX = -1) rather than
                        // playerIdx — see comment on Tile.
                    }
                }
                else if (decision == Player.EBuyDecision.AUCTION)
                {
                    AuctionRound.Run(board, index);
                }
            }
            else if (owner == playerIdx)
            {
                // do nothing
            }
            else if (!board.mortgaged[index])
            {
                int rent = Board.PROPERTY_PENALTIES[board.property[index], board.houses[index]];
                board.PaymentToPlayer(playerIdx, owner, rent);
            }
        }
    }

    public class TrainTile : Tile
    {
        public TrainTile(int i) : base(i) { }

        public override void Activate(Board board, int playerIdx)
        {
            int owner = board.owners[index];

            if (owner == Board.BANK_INDEX)
            {
                Player.EBuyDecision decision = board.policies[playerIdx].DecideBuy(board, playerIdx, index);

                if (decision == Player.EBuyDecision.BUY)
                {
                    if (board.players[playerIdx].funds < Board.COSTS[index])
                    {
                        AuctionRound.Run(board, index);
                    }
                    else
                    {
                        board.Payment(playerIdx, Board.COSTS[index]);
                        board.owners[index] = playerIdx;
                        if (board.original[index] == -1) board.original[index] = playerIdx;
                        board.players[playerIdx].items.Add(index);
                    }
                }
                else if (decision == Player.EBuyDecision.AUCTION)
                {
                    AuctionRound.Run(board, index);
                }
            }
            else if (!board.mortgaged[index])
            {
                // No outer "owner == playerIdx → nothing" branch here, by
                // design (see Tile class comment): self-landing pays self.
                int trains = board.CountTrains(owner);
                if (trains >= 1 && trains <= 4)
                {
                    int rent = Board.TRAIN_PENALTIES[trains - 1];
                    board.PaymentToPlayer(playerIdx, owner, rent);
                }
            }
        }
    }

    public class UtilityTile : Tile
    {
        public UtilityTile(int i) : base(i) { }

        public override void Activate(Board board, int playerIdx)
        {
            int owner = board.owners[index];

            if (owner == Board.BANK_INDEX)
            {
                Player.EBuyDecision decision = board.policies[playerIdx].DecideBuy(board, playerIdx, index);

                if (decision == Player.EBuyDecision.BUY)
                {
                    if (board.players[playerIdx].funds < Board.COSTS[index])
                    {
                        AuctionRound.Run(board, index);
                    }
                    else
                    {
                        board.Payment(playerIdx, Board.COSTS[index]);
                        board.owners[index] = playerIdx;
                        if (board.original[index] == -1) board.original[index] = playerIdx;
                        board.players[playerIdx].items.Add(index);
                    }
                }
                else if (decision == Player.EBuyDecision.AUCTION)
                {
                    AuctionRound.Run(board, index);
                }
            }
            else if (owner == playerIdx)
            {
                // do nothing
            }
            else if (!board.mortgaged[index])
            {
                int utilities = board.CountUtilities(owner);
                if (utilities >= 1 && utilities <= 2)
                {
                    int rent = Board.UTILITY_PENALTIES[utilities - 1] * board.last_roll;
                    board.PaymentToPlayer(playerIdx, owner, rent);
                }
            }
        }
    }

    public static class TileFactory
    {
        // Build a Tile[] of length BOARD_LENGTH that mirrors the ETile[] TYPES
        // array on Board. Called once per Board construction.
        public static Tile[] BuildTiles()
        {
            int n = Board.BOARD_LENGTH;
            Tile[] tiles = new Tile[n];
            for (int i = 0; i < n; i++)
            {
                Board.ETile t = Board.TYPES[i];
                switch (t)
                {
                    case Board.ETile.PROPERTY: tiles[i] = new PropertyTile(i); break;
                    case Board.ETile.TRAIN:    tiles[i] = new TrainTile(i); break;
                    case Board.ETile.UTILITY:  tiles[i] = new UtilityTile(i); break;
                    case Board.ETile.CHANCE:   tiles[i] = new ChanceTile(i); break;
                    case Board.ETile.CHEST:    tiles[i] = new ChestTile(i); break;
                    case Board.ETile.TAX:      tiles[i] = new TaxTile(i); break;
                    case Board.ETile.JAIL:     tiles[i] = new GoToJailTile(i); break;
                    case Board.ETile.NONE:     tiles[i] = new NoOpTile(i); break;
                    default:                   tiles[i] = new NoOpTile(i); break;
                }
            }
            return tiles;
        }
    }
}
