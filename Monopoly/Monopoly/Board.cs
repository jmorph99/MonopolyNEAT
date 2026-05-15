using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MONOPOLY
{
    public class Board
    {
        public enum EOutcome
        {
            ONGOING,
            DRAW,
            WIN1,
            WIN2,
            WIN3,
            WIN4,
            WIN5,
            WIN6,
        }

        // Map seat index 0..5 to EOutcome.WIN1..WIN6.
        public static EOutcome WinOutcomeForSeat(int seat)
        {
            switch (seat)
            {
                case 0: return EOutcome.WIN1;
                case 1: return EOutcome.WIN2;
                case 2: return EOutcome.WIN3;
                case 3: return EOutcome.WIN4;
                case 4: return EOutcome.WIN5;
                case 5: return EOutcome.WIN6;
                default: return EOutcome.DRAW;
            }
        }

        public static int SeatForWinOutcome(EOutcome o)
        {
            switch (o)
            {
                case EOutcome.WIN1: return 0;
                case EOutcome.WIN2: return 1;
                case EOutcome.WIN3: return 2;
                case EOutcome.WIN4: return 3;
                case EOutcome.WIN5: return 4;
                case EOutcome.WIN6: return 5;
                default: return -1;
            }
        }

        public enum EMode
        {
            ROLL,
        }

        public enum ETile
        {
            NONE,
            PROPERTY,
            TRAIN,
            UTILITY,
            CHANCE,
            CHEST,
            TAX,
            JAIL,
        }

        //constants
        //--------------------
        // Default player count for backward compatibility. Individual Boards
        // can override via the constructor; see `player_count` below.
        public static int PLAYER_COUNT = 4;

        public static int BANK_INDEX = -1;

        public static int BOARD_LENGTH = 40;
        public static int STALEMATE_TURN = 300;

        public static int GO_BONUS = 200;
        public static int GO_LANDING_BONUS = 200;

        public static int JAIL_INDEX = 10;
        public static int JAIL_PENALTY = 50;

        public static float MORTGAGE_INTEREST = 1.1f;

        //penalties for landing on a property (all circumstances)
        public static int[,] PROPERTY_PENALTIES = new int[16, 6]
        {{2, 10, 30, 90, 160, 250 },
        { 4, 20, 60, 180, 320, 450 },
        { 6, 30, 90, 270, 400, 550 },
        { 8, 40, 100, 300, 450, 600 },
        { 10, 50, 150, 450, 625, 750 },
        { 12, 60, 180, 500, 700, 900 },
        { 14, 70, 200, 550, 750, 950 },
        { 16, 80, 220, 600, 800, 1000 },
        { 18, 90, 250, 700, 875, 1050 },
        { 20, 100, 300, 750, 925, 1100 },
        { 22, 110, 330, 800, 975, 1150 },
        { 24, 120, 360, 850, 1025, 1200 },
        { 26, 130, 390, 900, 1100, 1275 },
        { 28, 150, 450, 1000, 1200, 1400 },
        { 35, 175, 500, 1100, 1300, 1500 },
        { 50, 200, 600, 1400, 1700, 2000 }};

        //penalities for landing on utilities (needs to be multiplied by roll)
        public static int[] UTILITY_POSIIONS = new int[2] { 12, 28 };
        public static int[] UTILITY_PENALTIES = new int[2] { 4, 10 };

        //penalties for landing on trains
        public static int[] TRAIN_POSITIONS = new int[4] { 5, 15, 25, 35 };
        public static int[] TRAIN_PENALTIES = new int[4] { 25, 50, 100, 200 };

        public static ETile[] TYPES = new ETile[40] 
        {ETile.NONE, ETile.PROPERTY, ETile.CHEST, ETile.PROPERTY, ETile.TAX, ETile.TRAIN, ETile.PROPERTY, ETile.CHANCE, ETile.PROPERTY, ETile.PROPERTY, ETile.NONE,
         ETile.PROPERTY, ETile.UTILITY, ETile.PROPERTY, ETile.PROPERTY, ETile.TRAIN, ETile.PROPERTY, ETile.CHEST, ETile.PROPERTY, ETile.PROPERTY, ETile.NONE,
         ETile.PROPERTY, ETile.CHANCE, ETile.PROPERTY, ETile.PROPERTY, ETile.TRAIN, ETile.PROPERTY, ETile.PROPERTY, ETile.UTILITY, ETile.PROPERTY, ETile.JAIL,
         ETile.PROPERTY, ETile.PROPERTY, ETile.CHEST, ETile.PROPERTY, ETile.TRAIN, ETile.CHANCE, ETile.PROPERTY, ETile.TAX, ETile.PROPERTY};

        public static int[] COSTS = new int[40] { 0, 60, 0, 60, 200, 200, 100, 0, 100, 120, 0, 140, 150, 140, 160, 200, 180, 0, 180, 200, 0, 220, 0, 220, 240, 200, 260, 260, 150, 280, 0, 300, 300, 0, 320, 200, 0, 350, 100, 400 };
        public static int[] BUILD = new int[16] { 50, 50, 50, 50, 100, 100, 100, 100, 150, 150, 150, 150, 200, 200, 200, 200 };

        public static int[,] SETS = new int[8, 3]
        {{1, 3, -1},
        { 6, 8, 9},
        { 11, 13, 14 },
        { 16, 18, 19 },
        { 21, 23, 24},
        { 26, 27, 29 },
        { 31, 32, 34 },
        { 37, 39, -1 }};
        //--------------------

        //board states
        //--------------------
        public bool[] mortgaged = new bool[40] { false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false };

        public int[] owners = new int[40] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };

        public int[] property = new int[40] { -1, 0, -1, 1, -1, -1, 2, -1, 2, 3, -1, 4, -1, 4, 5, -1, 6, -1, 6, 7, -1, 8, -1, 8, 9, -1, 10, 10, -1, 11, -1, 12, 12, -1, 13, -1, -1, 14, -1, 15 };

        public int[] houses = new int[40] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};

        public int[] original = new int[40] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        //--------------------

        public EMode mode = EMode.ROLL;

        public Player[] players;
        public IPolicy[] policies;
        public RNG random;

        // Per-instance player count. Defaults to 4 for backward compatibility
        // with the existing trainers; the new 6-player showdown uses 6.
        // Whenever code needs "how many seats", read board.player_count, NOT
        // the legacy static Board.PLAYER_COUNT.
        public int player_count = PLAYER_COUNT;

        public int turn = 0;
        public int count = 0;
        public int remaining = 0;

        public int last_roll = 0;

        // Per-Board bank supply. Real Monopoly ships 32 houses and 12 hotels;
        // builds are refused once these run out. Hotels "consume" 1 hotel but
        // return 4 houses to the bank (a hotel replaces 4 houses on a
        // property). Selling reverses. The network doesn't currently see
        // these counts, so the trained policies may try to overbuild during
        // shortages — BuildHouses just refuses and they pay only for what
        // actually got built.
        public int houseSupply = 32;
        public int hotelSupply = 12;

        //card stacks
        //--------------------
        public List<Card> chance;
        public List<Card> chest;
        //--------------------

        // tile table — built once per Board, owns its own rules
        public Tile[] tiles;

        public Board(IPolicy[] _policies) : this(_policies, new RNG())
        {
        }

        // Construct a Board with a caller-supplied RNG. Used for
        // deterministic-replay scenarios — pass `new RNG(seed)` and the dice,
        // card shuffles, trade proposals and auction tie-breaks become
        // reproducible.
        public Board(IPolicy[] _policies, RNG rng)
        {
            // Player count is inferred from the policies array (one IPolicy
            // per seat) and clamped to [2, 6]. Existing 4-player call sites
            // are unchanged; 6-player showdown passes 6 policies.
            int n = _policies != null ? _policies.Length : PLAYER_COUNT;
            if (n < 2) n = 2;
            if (n > 6) n = 6;
            player_count = n;

            players = new Player[player_count];
            policies = _policies;
            random = rng;

            for (int i = 0; i < player_count; i++)
            {
                players[i] = new Player();
            }

            remaining = player_count;

            chance = random.Shuffle(Decks.NewChance());
            chest = random.Shuffle(Decks.NewChest());

            tiles = TileFactory.BuildTiles();
        }

        // Snapshot the current board state into a fresh Board with the
        // supplied policies and a fresh (or supplied) RNG. Used by MCTS
        // to "branch" and play out hypothetical continuations.
        //
        // What gets deep-copied:
        //  - all tile arrays (owners, mortgaged, houses, ...)
        //  - per-player records (position, funds, items list, held cards)
        //  - the chance and chest decks (Card objects are functionally
        //    immutable so list-only copy is sufficient)
        //  - turn / mode / supplies / counters
        // What gets shared (safe):
        //  - tiles array (Tile objects are stateless rule strategies)
        //  - Card objects themselves
        //  - the static tables (PROPS, COSTS, ...)
        // What gets replaced:
        //  - policies (caller-supplied, so the clone can run any seats)
        //  - random (a clone with the original RNG would couple their dice;
        //    a fresh RNG keeps the simulation independent)
        public Board Clone(IPolicy[] newPolicies, RNG newRng = null)
        {
            Board b = (Board)MemberwiseClone();
            b.policies = newPolicies;
            b.random = newRng ?? new RNG();
            b.player_count = player_count;

            b.mortgaged = (bool[])mortgaged.Clone();
            b.owners = (int[])owners.Clone();
            b.property = (int[])property.Clone();
            b.houses = (int[])houses.Clone();
            b.original = (int[])original.Clone();

            b.players = new Player[player_count];
            for (int i = 0; i < player_count; i++)
            {
                Player src = players[i];
                Player dst = new Player();
                dst.state = src.state;
                dst.position = src.position;
                dst.funds = src.funds;
                dst.jail = src.jail;
                dst.doub = src.doub;
                dst.items = new List<int>(src.items);
                dst.heldCards = new List<GetOutOfJailCard>(src.heldCards);
                b.players[i] = dst;
            }

            b.chance = new List<Card>(chance);
            b.chest = new List<Card>(chest);

            return b;
        }

        public EOutcome Step()
        {
            switch (mode)
            {
                case EMode.ROLL: return Roll();
            }

            return EOutcome.ONGOING;
        }

        public EOutcome Roll()
        {
            BeforeTurn();

            int d1 = random.gen.Next(1, 7);
            int d2 = random.gen.Next(1, 7);

            last_roll = d1 + d2;

            bool isDouble = d1 == d2;
            bool doubleInJail = false;

            if (players[turn].state == Player.EState.JAIL)
            {
                Player.EJailDecision decision = policies[turn].DecideJail(this, turn);

                if (decision == Player.EJailDecision.ROLL)
                {
                    //regular jail state
                    if (isDouble)
                    {
                        players[turn].jail = 0;
                        players[turn].state = Player.EState.NORMAL;


                        doubleInJail = true;
                    }
                    else
                    {
                        players[turn].jail++;

                        if (players[turn].jail >= 3)
                        {
                            Payment(turn, JAIL_PENALTY);

                            players[turn].jail = 0;
                            players[turn].state = Player.EState.NORMAL;

                        }
                    }
                }
                else if (decision == Player.EJailDecision.PAY)
                {
                    Payment(turn, JAIL_PENALTY);

                    players[turn].jail = 0;
                    players[turn].state = Player.EState.NORMAL;

                }
                else if (decision == Player.EJailDecision.CARD)
                {
                    if (players[turn].heldCards.Count > 0)
                    {
                        // Return the card to the bottom of the deck it
                        // came from.
                        GetOutOfJailCard jc = players[turn].heldCards[0];
                        players[turn].heldCards.RemoveAt(0);
                        if (jc.origin == GetOutOfJailCard.EOrigin.CHANCE)
                        {
                            chance.Add(jc);
                        }
                        else
                        {
                            chest.Add(jc);
                        }

                        players[turn].jail = 0;
                        players[turn].state = Player.EState.NORMAL;

                    }
                    else
                    {
                        //run regular jail state
                        if (isDouble)
                        {
                            players[turn].jail = 0;
                            players[turn].state = Player.EState.NORMAL;

                        }
                        else
                        {
                            players[turn].jail++;

                            if (players[turn].jail >= 3)
                            {
                                Payment(turn, JAIL_PENALTY);

                                players[turn].jail = 0;
                                players[turn].state = Player.EState.NORMAL;

                            }
                        }
                    }
                }
            }

            if (players[turn].state == Player.EState.NORMAL)
            {
                bool notFinalDouble = (!isDouble) || (players[turn].doub <= 1);

                if (notFinalDouble)
                {
                    Movement(d1 + d2, isDouble);
                }
            }

            //start turn again (unless retired or the double was from jail)
            if (players[turn].state != Player.EState.RETIRED && isDouble && !doubleInJail)
            {
                players[turn].doub++;

                if (players[turn].doub >= 3)
                {
                    players[turn].position = JAIL_INDEX;
                    players[turn].doub = 0;
                    players[turn].state = Player.EState.JAIL;

                }
            }

            EOutcome outcome = EndTurn((!isDouble || players[turn].state == Player.EState.RETIRED || players[turn].state == Player.EState.JAIL));
            return outcome;
        }

        public EOutcome EndTurn(bool increment = true)
        {
            if (increment)
            {
                // Doubles count resets when a player's turn ends; the
                // three-doubles-in-a-row → jail rule applies within a single
                // turn sequence, not across the whole game.
                players[turn].doub = 0;
                IncrementTurn();

                int count = 0;

                while (players[turn].state == Player.EState.RETIRED && count <= player_count * 2)
                {
                    IncrementTurn();
                    count++;
                }

                if (remaining <= 1)
                {
                    return WinOutcomeForSeat(turn);
                }
            }

            count++;

            if (count >= STALEMATE_TURN)
            {
                return EOutcome.DRAW;
            }

            return EOutcome.ONGOING;
        }

        public void IncrementTurn()
        {
            turn++;

            if (turn >= player_count)
            {
                turn = 0;
            }
        }

        public void BeforeTurn()
        {
            if (players[turn].state == Player.EState.RETIRED)
            {
                return;
            }

            int itemCount = players[turn].items.Count;

            for (int j = 0; j < itemCount; j++)
            {
                int index = players[turn].items[j];

                if (mortgaged[index])
                {
                    int advancePrice = (int)(COSTS[index] * MORTGAGE_INTEREST);

                    if (advancePrice > players[turn].funds)
                    {
                        continue;
                    }



                    Player.EDecision decision = policies[turn].DecideAdvance(this, turn, index);


                    if (decision == Player.EDecision.YES)
                    {
                        Advance(index);
                    }
                }
                else
                {


                    Player.EDecision decision = policies[turn].DecideMortgage(this, turn, index);


                    if (decision == Player.EDecision.YES)
                    {
                        //Mortgage(index);
                    }
                }
            }

            int[] sets = FindSets(turn);
            int setCount = sets.GetLength(0);

            for (int j = 0; j < setCount; j++)
            {
                int houseTotal = houses[SETS[sets[j], 0]] + houses[SETS[sets[j], 1]];

                if (sets[j] != 0 && sets[j] != 7)
                {
                    houseTotal += houses[SETS[sets[j], 2]];
                }

                int sellMax = houseTotal;



                int decision = policies[turn].DecideSellHouse(this, turn, sets[j]);


                decision = Math.Min(decision, sellMax);

                if (decision > 0)
                {
                    int sold = SellHouses(sets[j], decision);
                    players[turn].funds += (int)(sold * BUILD[property[SETS[sets[j], 0]]] * 0.5f);
                }
            }

            sets = FindSets(turn);
            setCount = sets.GetLength(0);

            for (int j = 0; j < setCount; j++)
            {
                // Rule: cannot build on a color group with any mortgaged
                // property in it.
                int s = sets[j];
                bool anyMortgaged = mortgaged[SETS[s, 0]] || mortgaged[SETS[s, 1]];
                if (s != 0 && s != 7)
                {
                    anyMortgaged = anyMortgaged || mortgaged[SETS[s, 2]];
                }
                if (anyMortgaged)
                {
                    continue;
                }

                int maxHouse = 10;
                int houseTotal = houses[SETS[sets[j], 0]] + houses[SETS[sets[j], 1]];

                if (sets[j] != 0 && sets[j] != 7)
                {
                    maxHouse = 15;
                    houseTotal += houses[SETS[sets[j], 2]];
                }

                int buildMax = maxHouse - houseTotal;
                int affordMax = (int)Math.Floor(players[turn].funds / (float)BUILD[property[SETS[sets[j], 0]]]);

                if (affordMax < 0)
                {
                    affordMax = 0;
                }

                buildMax = Math.Min(buildMax, affordMax);



                int decision = policies[turn].DecideBuildHouse(this, turn, sets[j]);


                decision = Math.Min(decision, buildMax);

                if (decision > 0)
                {
                    int built = BuildHouses(sets[j], decision);
                    Payment(turn, built * BUILD[property[SETS[sets[j], 0]]]);
                }
            }

            TradeRound.Run(this);
        }

        public void Movement(int roll, bool isDouble)
        {
            players[turn].position += roll;

            //wrap around
            if (players[turn].position >= BOARD_LENGTH)
            {
                players[turn].position -= BOARD_LENGTH;

                if (players[turn].position == 0)
                {
                    players[turn].funds += GO_BONUS;
                }
                else
                {
                    players[turn].funds += GO_LANDING_BONUS;
                }
            }


            ActivateTile();
        }

        public void ActivateTile()
        {
            int index = players[turn].position;
            tiles[index].Activate(this, turn);
        }

        public void Payment(int owner, int fine)
        {
            players[owner].funds -= fine;

            int original = players[owner].funds;

            //prompt for selling sets
            if (players[owner].funds < 0)
            {
                int[] sets = FindSets(turn);
                int setCount = sets.GetLength(0);

                for (int j = 0; j < setCount; j++)
                {
                    int houseTotal = houses[SETS[sets[j], 0]] + houses[SETS[sets[j], 1]];

                    if (sets[j] != 0 && sets[j] != 7)
                    {
                        houseTotal += houses[SETS[sets[j], 2]];
                    }

                    int sellMax = houseTotal;



                    int decision = policies[turn].DecideSellHouse(this, turn, sets[j]);


                    decision = Math.Min(decision, sellMax);

                    if (decision > 0)
                    {
                        int sold = SellHouses(sets[j], decision);

                        players[owner].funds += (int)(sold * BUILD[property[SETS[sets[j], 0]]] * 0.5f);
                    }
                }
            }

            //prompt for mortgages once
            if (players[owner].funds < 0)
            {
                int itemCount = players[owner].items.Count;

                for (int i = 0; i < itemCount; i++)
                {
                    int item = players[owner].items[i];


                    Player.EDecision decision = policies[owner].DecideMortgage(this, owner, players[owner].items[i]);


                    if (decision == Player.EDecision.YES)
                    {
                        Mortgage(item);
                    }
                }
            }

            //bankrupt
            if (players[owner].funds < 0)
            {
                int regained = players[owner].funds - original;

                int itemCount = players[owner].items.Count;
                int[] toAuction = new int[itemCount];

                int housemoney = 0;

                for (int i = 0; i < itemCount; i++)
                {
                    int item = players[owner].items[i];
                    toAuction[i] = item;
                    owners[item] = BANK_INDEX;

                    if (houses[item] > 0)
                    {
                        int liquidated = houses[item];
                        int sell = (liquidated * BUILD[property[item]]) / 2;
                        housemoney += sell;

                        ReturnHousesToBank(liquidated);
                        houses[item] = 0;
                    }
                }

                players[owner].items.Clear();

                // Held Get-Out-of-Jail-Free cards return to the bottom of
                // their source decks when a player goes bankrupt to the bank.
                for (int i = 0; i < players[owner].heldCards.Count; i++)
                {
                    GetOutOfJailCard jc = players[owner].heldCards[i];
                    if (jc.origin == GetOutOfJailCard.EOrigin.CHANCE) chance.Add(jc);
                    else chest.Add(jc);
                }
                players[owner].heldCards.Clear();

                //give money to other
                players[owner].state = Player.EState.RETIRED;
                remaining--;

                // Rule: when a player goes bankrupt to the bank, the bank
                // auctions each of their former properties immediately.
                // Mortgaged status is preserved; the new owner inherits it.
                for (int i = 0; i < toAuction.Length; i++)
                {
                    AuctionRound.Run(this, toAuction[i]);
                }
            }
        }

        public void PaymentToPlayer(int owner, int recipient, int fine)
        {
            players[owner].funds -= fine;

            players[recipient].funds += fine;

            int original = players[owner].funds;

            //prompt for selling sets
            if (players[owner].funds < 0)
            {
                int[] sets = FindSets(turn);
                int setCount = sets.GetLength(0);

                for (int j = 0; j < setCount; j++)
                {
                    int houseTotal = houses[SETS[sets[j], 0]] + houses[SETS[sets[j], 1]];

                    if (sets[j] != 0 && sets[j] != 7)
                    {
                        houseTotal += houses[SETS[sets[j], 2]];
                    }

                    int sellMax = houseTotal;



                    int decision = policies[turn].DecideSellHouse(this, turn, sets[j]);


                    decision = Math.Min(decision, sellMax);

                    if (decision > 0)
                    {
                        int sold = SellHouses(sets[j], decision);
                        players[owner].funds += (int)(sold * BUILD[property[SETS[sets[j], 0]]] * 0.5f);

                    }
                }
            }

            //prompt for mortgages once
            if (players[owner].funds < 0)
            {
                int itemCount = players[owner].items.Count;

                for (int i = 0; i < itemCount; i++)
                {
                    int item = players[owner].items[i];


                    Player.EDecision decision = policies[owner].DecideMortgage(this, owner, players[owner].items[i]);


                    if (decision == Player.EDecision.YES)
                    {
                        Mortgage(item);
                    }
                }
            }

            //bankrupt
            if (players[owner].funds < 0)
            {
                players[recipient].funds += players[owner].funds;

                int itemCount = players[owner].items.Count;

                int housemoney = 0;

                for (int i = 0; i < itemCount; i++)
                {
                    //give to other player
                    players[recipient].items.Add(players[owner].items[i]);


                    int item = players[owner].items[i];
                    owners[item] = recipient;

                    if (houses[item] > 0)
                    {
                        int liquidated = houses[item];
                        int sell = (liquidated * BUILD[property[item]]) / 2;
                        housemoney += sell;

                        ReturnHousesToBank(liquidated);
                        houses[item] = 0;
                    }
                }

                players[recipient].funds += housemoney;

                players[owner].items.Clear();

                // Held Get-Out-of-Jail-Free cards transfer to the creditor
                // along with the rest of the bankrupt player's assets.
                for (int i = 0; i < players[owner].heldCards.Count; i++)
                {
                    players[recipient].heldCards.Add(players[owner].heldCards[i]);
                }
                players[owner].heldCards.Clear();

                //give money to other
                players[owner].state = Player.EState.RETIRED;
                remaining--;
            }
        }

        public int Owner(int index)
        {
            return owners[index];
        }

        public void Mortgage(int index)
        {
            // Rule: a property must have no houses on it (or anywhere in its
            // color group, for that matter) before it can be mortgaged. The
            // policy is expected to sell houses first; if it asks to mortgage
            // anyway, refuse silently.
            if (houses[index] > 0)
            {
                return;
            }

            mortgaged[index] = true;

            players[owners[index]].funds += COSTS[index] / 2;
        }

        public void Advance(int index)
        {
            mortgaged[index] = false;

            int cost = (int)(COSTS[index] * MORTGAGE_INTEREST);
            Payment(owners[index], cost);
        }

        // True if `playerIdx` owns every property in the color group that
        // contains `boardIdx`. Used by PropertyTile to apply the unimproved-
        // monopoly 2x rent multiplier. Returns false if boardIdx isn't a
        // property tile (e.g. railroad or utility).
        public bool OwnsCompleteSet(int playerIdx, int boardIdx)
        {
            for (int s = 0; s < 8; s++)
            {
                int a = SETS[s, 0];
                int b = SETS[s, 1];
                int c = SETS[s, 2];

                if (a != boardIdx && b != boardIdx && c != boardIdx)
                {
                    continue;
                }

                if (owners[a] != playerIdx) return false;
                if (owners[b] != playerIdx) return false;
                if (c != -1 && owners[c] != playerIdx) return false;
                return true;
            }
            return false;
        }

        public int CountTrains(int player)
        {
            int itemCount = players[player].items.Count;

            int count = 0;

            for (int i = 0; i < itemCount; i++)
            {
                if (TRAIN_POSITIONS.Contains(players[player].items[i]))
                {
                    count++;
                }
            }

            return count;
        }

        public int CountUtilities(int player)
        {
            int itemCount = players[player].items.Count;

            int count = 0;

            for (int i = 0; i < itemCount; i++)
            {
                if (UTILITY_POSIIONS.Contains(players[player].items[i]))
                {
                    count++;
                }
            }

            return count;
        }

        public void DrawChance()
        {
            Card card = chance[0];
            chance.RemoveAt(0);
            // Get-Out-of-Jail-Free cards leave the deck and are kept by the
            // drawing player. Every other card rotates to the bottom.
            if (!(card is GetOutOfJailCard))
            {
                chance.Add(card);
            }
            card.Apply(this, turn);
        }

        public void DrawChest()
        {
            Card card = chest[0];
            chest.RemoveAt(0);
            if (!(card is GetOutOfJailCard))
            {
                chest.Add(card);
            }
            card.Apply(this, turn);
        }

        public void AdvanceToTrain2()
        {
            int index = players[turn].position;

            if (index < TRAIN_POSITIONS[0])
            {
                players[turn].position = TRAIN_POSITIONS[0];
            }
            else if (index < TRAIN_POSITIONS[1])
            {
                players[turn].position = TRAIN_POSITIONS[1];
            }
            else if (index < TRAIN_POSITIONS[2])
            {
                players[turn].position = TRAIN_POSITIONS[2];
            }
            else if (index < TRAIN_POSITIONS[3])
            {
                players[turn].position = TRAIN_POSITIONS[3];
            }
            else
            {
                players[turn].position = TRAIN_POSITIONS[0];
                players[turn].funds += GO_BONUS;
            }


            index = players[turn].position;

            int owner = Owner(index);

            if (owner == BANK_INDEX)
            {


                Player.EBuyDecision decision = policies[turn].DecideBuy(this, turn, index);


                if (decision == Player.EBuyDecision.BUY)
                {
                    if (players[turn].funds < COSTS[index])
                    {
                        AuctionRound.Run(this, index);
                    }
                    else
                    {
                        Payment(turn, COSTS[index]);

                        owners[index] = turn;

                        if (original[index] == -1)
                        {
                            original[index] = turn;
                        }

                        players[turn].items.Add(index);

                    }


                }
                else if (decision == Player.EBuyDecision.AUCTION)
                {
                    AuctionRound.Run(this, index);
                }
            }
            else if (owner == turn)
            {
                //do nothing
            }
            else if (!mortgaged[index])
            {
                //payment train
                int trains = CountTrains(owner);

                if (trains >= 1 && trains <= 4)
                {
                    int fine = TRAIN_PENALTIES[trains - 1];

                    PaymentToPlayer(turn, owner, fine * 2);
                }
            }
        }

        public void AdvanceToUtility10()
        {
            int index = players[turn].position;

            if (index < UTILITY_POSIIONS[0])
            {
                players[turn].position = UTILITY_POSIIONS[0];
            }
            else if (index < UTILITY_POSIIONS[1])
            {
                players[turn].position = UTILITY_POSIIONS[1];
            }
            else
            {
                players[turn].position = UTILITY_POSIIONS[0];
                players[turn].funds += GO_BONUS;

            }


            index = players[turn].position;

            int owner = Owner(index);

            if (owner == BANK_INDEX)
            {
                Player.EBuyDecision decision = policies[turn].DecideBuy(this, turn, index);

                if (decision == Player.EBuyDecision.BUY)
                {
                    if (players[turn].funds < COSTS[index])
                    {
                        AuctionRound.Run(this, index);
                    }
                    else
                    {
                        Payment(turn, COSTS[index]);

                        owners[index] = turn;

                        if (original[index] == -1)
                        {
                            original[index] = turn;
                        }

                        players[turn].items.Add(index);

                    }   
                }
                else if (decision == Player.EBuyDecision.AUCTION)
                {
                    AuctionRound.Run(this, index);
                }
            }
            else if (owner == turn)
            {
                //do nothing
            }
            else if (!mortgaged[index])
            {
                //payment utility
                // Rule: "Throw the dice and pay the owner 10 times the
                // amount shown" — a fresh roll, not the original roll that
                // led the player onto the Chance tile.
                int d1 = random.gen.Next(1, 7);
                int d2 = random.gen.Next(1, 7);
                int fine = 10 * (d1 + d2);

                PaymentToPlayer(turn, owner, fine);
            }
        }

        public int[] FindSets(int owner)
        {
            List<int> sets = new List<int>();
            List<int> items = players[owner].items;

            for (int i = 0; i < 8; i++)
            {
                //two piece sets
                if (i == 0 || i == 7)
                {
                    if (items.Contains(SETS[i,0]) && items.Contains(SETS[i, 1]))
                    {
                        sets.Add(i);
                    }

                    continue;
                }

                //three piece sets
                if (items.Contains(SETS[i, 0]) && items.Contains(SETS[i, 1]) && items.Contains(SETS[i, 2]))
                {
                    sets.Add(i);
                }
            }

            return sets.ToArray();
        }

        // Returns the number of houses/hotels actually built. If the bank's
        // supply runs out mid-batch the loop stops early; the caller pays
        // only for what was built.
        public int BuildHouses(int set, int amount)
        {
            int last = 2;

            if (set == 0 || set == 7)
            {
                last = 1;
            }

            int built = 0;
            for (int i = 0; i < amount; i++)
            {
                //find smallest house number from back
                int bj = last;

                for (int j = last - 1; j >= 0; j--)
                {
                    if (houses[SETS[set, bj]] > houses[SETS[set, j]])
                    {
                        bj = j;
                    }
                }

                int curr = houses[SETS[set, bj]];
                if (curr < 4)
                {
                    if (houseSupply <= 0) break;
                    houseSupply--;
                }
                else // curr == 4: building hotel
                {
                    if (hotelSupply <= 0) break;
                    hotelSupply--;
                    // The 4 houses on this lot return to the bank's supply.
                    houseSupply = System.Math.Min(houseSupply + 4, 32);
                }

                houses[SETS[set, bj]]++;
                built++;
            }
            return built;
        }

        // Returns the number of houses/hotels actually sold. Hotels return
        // 1 hotel and up to 4 houses to the bank's supply; the simulator
        // doesn't model the rulebook's "if the bank lacks houses to make
        // change, sell out completely" corner case.
        public int SellHouses(int set, int amount)
        {
            int last = 2;

            if (set == 0 || set == 7)
            {
                last = 1;
            }

            int sold = 0;
            for (int i = 0; i < amount; i++)
            {
                //find smallest house number from back
                int bj = 0;

                for (int j = 0; j <= last; j++)
                {
                    if (houses[SETS[set, bj]] < houses[SETS[set, j]])
                    {
                        bj = j;
                    }
                }

                int curr = houses[SETS[set, bj]];
                if (curr == 0) break;

                if (curr == 5)
                {
                    hotelSupply = System.Math.Min(hotelSupply + 1, 12);
                    houseSupply = System.Math.Min(houseSupply + 4, 32);
                }
                else
                {
                    houseSupply = System.Math.Min(houseSupply + 1, 32);
                }

                houses[SETS[set, bj]]--;
                sold++;
            }
            return sold;
        }

        // Drop all houses on a property back to the bank's supply.
        // Used by the two bankruptcy paths.
        public void ReturnHousesToBank(int level)
        {
            if (level == 5)
            {
                hotelSupply = System.Math.Min(hotelSupply + 1, 12);
                houseSupply = System.Math.Min(houseSupply + 4, 32);
            }
            else if (level >= 1)
            {
                houseSupply = System.Math.Min(houseSupply + level, 32);
            }
        }
    }
}
