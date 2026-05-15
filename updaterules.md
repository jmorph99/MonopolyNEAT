# Monopoly rule-fidelity audit — conclusions

Comparison of the simulator (`Monopoly/Monopoly/Board.cs`, `Tiles.cs`, `Cards.cs`, `Player.cs`) against the official Monopoly rules.

## Data bugs (likely typos)

- **`COSTS[37]` = 250** (`Board.cs:88`) — Park Place should be **$350**.
- **`PROPERTY_PENALTIES[11,0]` = 22** (`Board.cs:68`) — Marvin Gardens base rent should be **24**.

## Missing rules — significant strategic impact

1. **No monopoly rent doubling.** `PropertyTile.Activate` (`Tiles.cs:108`) never checks set ownership. Owning all 3 of a color with 0 houses should double base rent; in this sim it does not. Removes the core reason to complete a group before building.
2. **`doub` counter is never reset between turns** (`Board.cs:262`). Doubles accumulate across the whole game, so three doubles *total* (not three in one turn) sends a player to jail. Distorts jail dynamics heavily.
3. **Bankruptcy to the bank skips the auction** (`Board.cs:752`). Properties drift back to `BANK_INDEX` instead of being auctioned immediately.
4. **No house/hotel scarcity.** Real game caps the supply at 32 houses / 12 hotels; sim has no global counter.
5. **Mortgage allowed with houses still on the property** (`Board.cs:876`).
6. **Building allowed on a set containing a mortgaged property** (`Board.cs:397`).

## Card-deck deviations

7. **`AdvanceToUtility10` uses `last_roll`** (`Board.cs:1086`) — official rule requires a *fresh* dice roll for 10× utility rent.
8. **Get Out of Jail Free cards never leave the deck** (`Cards.cs:77`). Multiple players can simultaneously "hold" the same card; used cards never get re-inserted (because they never left).
9. **Paying / using a card to leave jail still grants a doubles re-roll** (`doubleInJail` flag set only on the ROLL branch, `Board.cs:175`). Rulebook is ambiguous; flag for awareness.

## Trade / auction simplifications

10. **Auctions are sealed-bid one-shot** (`Board.cs:566`) — real auction is iterative open-ascending.
11. **Trades cannot be pure cash-for-property** (`Board.cs:488`: both sides must include ≥1 item).
12. **Trades only fire on the current player's turn, 4 random samples** — real game has no such constraint.
13. **Mortgaged properties received in trade don't carry the 10% obligation.**

## Minor / cosmetic

14. **`TrainTile` has no "owner == me" branch** (`Tiles.cs:148`) — self-landing on a train calls `PaymentToPlayer(self, self)`. Net zero, but can trigger the liquidation cascade if the player is already in debt. Flagged as preserved on purpose.
15. **`BackThreeCard` unsafe for `position < 3`** (`Cards.cs:68`). Currently safe only because Chance tiles are at 7/22/36.
16. **`PropertyTile` writes pre-purchase owner `-1` to the adapter** instead of `playerIdx` (`Tiles.cs:94`). Inconsistent with Train/Utility tiles. Flagged as preserved on purpose.
17. **Income Tax** uses flat $200 (matches post-2008 rules; pre-2008 allowed 10% of total worth).
18. **Luxury Tax** is $100 (matches classic; post-2008 editions reduced it to $75).
19. **Free Parking** is a true no-op — matches official rules, not the popular house rule.

## Correct behavior worth confirming

- Rent tables, property costs (except Park Place), build costs, color groups, railroad/utility ladders.
- $200 for passing OR landing on GO (not $400).
- 16 Chance + 16 Community Chest cards, correct effects and proportions (school fees $50 vs $150 is edition-dependent).
- Even building / even selling order in `BuildHouses`/`SellHouses`.
- Three doubles in a single turn → jail (modulo the cumulative-counter bug).
- Rent is zero on mortgaged properties.
- "Advance to Nearest Railroad" pays 2× rent; "Advance to Nearest Utility" pays 10× (just sources the roll incorrectly).
- Chairman / Birthday cards skip retired players.

## Recommended fix priority

For maximum behavioral impact on the trained populations, fix in this order:

1. **Reset `doub = 0` at the start of each turn** — one line in `IncrementTurn` or `BeforeTurn`. Currently the single largest divergence from real Monopoly.
2. **Add 2× rent multiplier for unimproved monopolies** in `PropertyTile.Activate`. Without this, holding a set without houses is strategically underweighted.
3. **Fix Park Place cost (250 → 350) and Marvin Gardens base rent (22 → 24)**.
4. **Enforce mortgage/build interactions**: no building on sets with mortgaged members; sell houses before mortgaging.

Items 1 and 2 are the only ones likely to materially change which strategies the NEAT population converges on. Items 3–4 are correctness fixes with smaller behavioral impact. Trade/auction simplifications (10–12) are deliberate design choices for self-play tractability and are best left alone unless you specifically want to expand the policy space.

Note: any fix that alters network inputs (e.g. via `NetworkAdapter`) or the meaning of decisions invalidates the existing checkpoints. The four priority fixes above are all internal to `Board`/`Tiles`/`Cards` and don't touch the 126→9 I/O contract, so they're safe to apply against existing trained populations.
