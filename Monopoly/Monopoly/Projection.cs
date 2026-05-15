using System;

namespace MONOPOLY
{
    // DecisionContext encodes what's being asked of a policy beyond the raw
    // game state: which tile(s) are "selected" for this decision, and any
    // trade money offset. Decisions that don't need either pass None.
    public struct DecisionContext
    {
        public int[] selectedTiles;
        public int moneyBalance;

        public static DecisionContext None
        {
            get { return new DecisionContext { selectedTiles = Array.Empty<int>(), moneyBalance = 0 }; }
        }

        public static DecisionContext Tile(int boardIdx)
        {
            return new DecisionContext { selectedTiles = new int[] { boardIdx }, moneyBalance = 0 };
        }

        public static DecisionContext Trade(int[] tiles, int money)
        {
            return new DecisionContext { selectedTiles = tiles ?? Array.Empty<int>(), moneyBalance = money };
        }
    }

    // Projection turns a Board snapshot + a viewing player + a DecisionContext
    // into the 127-float vector a NEAT.Phenotype expects as input.
    //
    // This replaces the old NetworkAdapter setter-and-accumulate pattern. The
    // network input is now derived from current Board state at decision time,
    // not mutated incrementally as the game progresses. Consequences:
    //
    //  * The "I forgot to update the adapter" class of bugs vanishes — most
    //    notably PROPERTY tile's old `adapter.SetOwner(index, owner)` line
    //    that passed the pre-purchase owner (-1) rather than the buyer.
    //  * The previously-buggy `ConvertCard` (always returned 1.0 due to a
    //    field/parameter shadowing typo) is replaced with the obviously-
    //    intended Clamp(cardCount, 0, 1).
    //  * Selection bits no longer leak between consecutive decisions. Each
    //    decision sees only the tiles it specifies in its DecisionContext.
    //
    // Trained networks may make slightly different decisions because of these
    // corrections; they were trained against the leaky, buggy inputs.
    public static class Projection
    {
        // Layout — kept identical to the old NetworkAdapter offsets so that
        // trained populations evolved against this encoding still match up.
        public const int OFF_TURN = 0;          // 0..3   (one-hot)
        public const int OFF_POS = 4;           // 4..7
        public const int OFF_MONEY = 8;         // 8..11
        public const int OFF_CARD = 12;         // 12..15
        public const int OFF_JAIL = 16;         // 16..19
        public const int OFF_OWN = 20;          // 20..47 (28 buyables)
        public const int OFF_MORT = 48;         // 48..75 (28 buyables)
        public const int OFF_HOUSE = 76;        // 76..97 (22 properties)
        public const int OFF_SELECT = 98;       // 98..125 (29: 28 buyables + 1 reserve)
        public const int OFF_MONEY_CTX = 126;
        public const int PACK_SIZE = 127;

        // PROPS: board index → 0..27 buyable slot. -1 for non-buyable spaces
        // (chance/chest/tax/jail). Order: in increasing board index.
        public static readonly int[] PROPS = new int[40]
        {
            -1, 0, -1, 1, -1, 2, 3, -1, 4, 5,
            -1, 6, 7, 8, 9, 10, 11, -1, 12, 13,
            -1, 14, -1, 15, 16, 17, 18, 19, 20, 21,
            -1, 22, 23, -1, 24, 25, -1, 26, -1, 27
        };

        // HOUSES: board index → 0..21 property-with-house slot.
        // -1 for trains, utilities, and non-buyable spaces.
        public static readonly int[] HOUSES = new int[40]
        {
            -1, 0, -1, 1, -1, -1, 2, -1, 3, 4,
            -1, 5, -1, 6, 7, -1, 8, -1, 9, 10,
            -1, 11, -1, 12, 13, -1, 14, 15, -1, 16,
            -1, 17, 18, -1, 19, -1, -1, 20, -1, 21
        };

        public static float ConvertMoney(int money)
        {
            float norm = money / 4000.0f;
            return Math.Clamp(norm, 0.0f, 1.0f);
        }

        public static float ConvertMoneyValue(float value)
        {
            return value * 4000.0f;
        }

        public static float ConvertHouseValue(float value)
        {
            if (value <= 0.5f)
            {
                value = 0.0f;
            }
            return value * 15.0f;
        }

        public static float ConvertPosition(int position)
        {
            float norm = position / 39.0f;
            return Math.Clamp(norm, 0.0f, 1.0f);
        }

        public static float ConvertCard(int cards)
        {
            // Fixed: the old NetworkAdapter.ConvertCard read the offset field
            // instead of the parameter, so it always returned 1.0. The
            // obvious intent is "do you have any get-out-of-jail cards".
            return Math.Clamp((float)cards, 0.0f, 1.0f);
        }

        public static float ConvertHouse(int houses)
        {
            float norm = houses / 5.0f;
            return Math.Clamp(norm, 0.0f, 1.0f);
        }

        public static float ConvertOwner(int owner)
        {
            // BANK_INDEX = -1 → 0.0;  player 0..3 → 0.25, 0.5, 0.75, 1.0.
            return (owner + 1) / 4.0f;
        }

        // Project a Board state into the 127-float vector the network reads.
        // `viewingPlayer` is the player whose turn/perspective is being
        // encoded into the turn one-hot. `ctx` describes the question being
        // asked (which tiles are flagged + any money balance).
        //
        // 4-player games (player_count == 4): absolute seat encoding,
        // identical to the original layout. Existing trained networks see
        // exactly the input they were trained on.
        //
        // 5/6-player games (player_count > 4): ego-centric remap into the
        // 4 player slots. viewingPlayer occupies slot 0. The 3 richest
        // opponents (by funds + sum of property COSTS) take slots 1-3 in
        // descending order. Hidden opponents share slot 3 for ownership
        // encoding, but slot 3's per-player scalars belong to the single
        // richest seat assigned there. This keeps the network's 127-input
        // shape — trained checkpoints stay usable — while degrading
        // gracefully when more players exist than slots.
        public static float[] Project(Board board, int viewingPlayer, DecisionContext ctx)
        {
            float[] pack = new float[PACK_SIZE];
            int N = board.player_count;

            if (N <= 4)
            {
                ProjectAbsolute(pack, board, viewingPlayer, N);
            }
            else
            {
                ProjectEgocentric(pack, board, viewingPlayer);
            }

            // selection bits — board-state index, ego-invariant.
            if (ctx.selectedTiles != null)
            {
                for (int i = 0; i < ctx.selectedTiles.Length; i++)
                {
                    int propSlot = PROPS[ctx.selectedTiles[i]];
                    if (propSlot >= 0)
                    {
                        pack[OFF_SELECT + propSlot] = 1.0f;
                    }
                }
            }
            pack[OFF_MONEY_CTX] = ctx.moneyBalance;
            return pack;
        }

        // 4-player original layout — preserved exactly.
        private static void ProjectAbsolute(float[] pack, Board board, int viewingPlayer, int N)
        {
            pack[OFF_TURN + viewingPlayer] = 1.0f;

            for (int i = 0; i < N; i++)
            {
                Player p = board.players[i];
                pack[OFF_POS + i] = ConvertPosition(p.position);
                pack[OFF_MONEY + i] = ConvertMoney(p.funds);
                pack[OFF_CARD + i] = ConvertCard(p.card);
                pack[OFF_JAIL + i] = (p.state == Player.EState.JAIL) ? 1.0f : 0.0f;
            }

            for (int idx = 0; idx < Board.BOARD_LENGTH; idx++)
            {
                int propSlot = PROPS[idx];
                if (propSlot >= 0)
                {
                    pack[OFF_OWN + propSlot] = ConvertOwner(board.owners[idx]);
                    pack[OFF_MORT + propSlot] = board.mortgaged[idx] ? 1.0f : 0.0f;
                }
                int houseSlot = HOUSES[idx];
                if (houseSlot >= 0)
                {
                    pack[OFF_HOUSE + houseSlot] = ConvertHouse(board.houses[idx]);
                }
            }
        }

        // Ego-centric layout for player_count > 4. Always emits 127 floats.
        private static void ProjectEgocentric(float[] pack, Board board, int viewingPlayer)
        {
            // egoSlot[seat] maps absolute seat to ego slot 0..3.
            // slotRep[slot] is which seat owns each ego slot's per-player scalars.
            int[] egoSlot;
            int[] slotRep;
            BuildEgoMap(board, viewingPlayer, out egoSlot, out slotRep);

            // Turn one-hot: viewer (slot 0) is always the player being polled.
            pack[OFF_TURN + 0] = 1.0f;

            // Per-player scalars: one representative seat per ego slot.
            for (int slot = 0; slot < 4; slot++)
            {
                int seat = slotRep[slot];
                if (seat < 0) continue;
                Player p = board.players[seat];
                pack[OFF_POS + slot] = ConvertPosition(p.position);
                pack[OFF_MONEY + slot] = ConvertMoney(p.funds);
                pack[OFF_CARD + slot] = ConvertCard(p.card);
                pack[OFF_JAIL + slot] = (p.state == Player.EState.JAIL) ? 1.0f : 0.0f;
            }

            // Per-tile state: ownership remapped through egoSlot, mortgage
            // and house counts are seat-independent.
            for (int idx = 0; idx < Board.BOARD_LENGTH; idx++)
            {
                int propSlot = PROPS[idx];
                if (propSlot >= 0)
                {
                    int owner = board.owners[idx];
                    int egoOwner = owner < 0 ? -1 : egoSlot[owner];
                    pack[OFF_OWN + propSlot] = ConvertOwner(egoOwner);
                    pack[OFF_MORT + propSlot] = board.mortgaged[idx] ? 1.0f : 0.0f;
                }
                int houseSlot = HOUSES[idx];
                if (houseSlot >= 0)
                {
                    pack[OFF_HOUSE + houseSlot] = ConvertHouse(board.houses[idx]);
                }
            }
        }

        // Build the ego map and per-slot representative seats. Sorting is
        // by approximate net worth: funds + sum of COSTS[items] (ignores
        // mortgage discount and house value — close enough for ranking).
        private static void BuildEgoMap(Board board, int viewingPlayer, out int[] egoSlot, out int[] slotRep)
        {
            int N = board.player_count;
            egoSlot = new int[N];
            slotRep = new int[] { -1, -1, -1, -1 };
            slotRep[0] = viewingPlayer;
            egoSlot[viewingPlayer] = 0;

            int[] oppSeats = new int[N - 1];
            int[] oppWorth = new int[N - 1];
            int oppCount = 0;
            for (int i = 0; i < N; i++)
            {
                if (i == viewingPlayer) continue;
                int w = board.players[i].funds;
                for (int k = 0; k < board.players[i].items.Count; k++)
                {
                    int pidx = board.players[i].items[k];
                    w += Board.COSTS[pidx];
                }
                oppSeats[oppCount] = i;
                oppWorth[oppCount] = w;
                oppCount++;
            }

            // Simple selection sort by descending worth — N is tiny.
            for (int i = 0; i < oppCount - 1; i++)
            {
                int maxIdx = i;
                for (int j = i + 1; j < oppCount; j++)
                {
                    if (oppWorth[j] > oppWorth[maxIdx]) maxIdx = j;
                }
                if (maxIdx != i)
                {
                    (oppWorth[i], oppWorth[maxIdx]) = (oppWorth[maxIdx], oppWorth[i]);
                    (oppSeats[i], oppSeats[maxIdx]) = (oppSeats[maxIdx], oppSeats[i]);
                }
            }

            // Assign top 3 opponents to ego slots 1, 2, 3.
            for (int i = 0; i < oppCount; i++)
            {
                int slot = (i < 3) ? i + 1 : 3;        // overflow collides at slot 3
                egoSlot[oppSeats[i]] = slot;
                if (slotRep[slot] == -1) slotRep[slot] = oppSeats[i];  // first to claim wins
            }
        }

        // Convenience: project with no selection context.
        public static float[] Project(Board board, int viewingPlayer)
        {
            return Project(board, viewingPlayer, DecisionContext.None);
        }
    }
}
