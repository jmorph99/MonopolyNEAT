# Changes

A plain-language log of changes to the Monopoly NEAT trainer, newest first.
The trained-network files in this folder (`monopoly_population 162.txt` etc.)
continue to load as before across all changes below.

---

## Foundation for non-NEAT training methods

Up to now the simulator only knew about one kind of "brain": a NEAT
phenotype. That worked, but `models.md` describes four other training
methods (Evolution Strategies, PPO, Rainbow DQN, AlphaZero) — all of
which use a plain fixed-shape neural net instead of an evolved one. So
the game side has to be told "any kind of network is fine here," not
"NEAT only."

Two pieces were added:

- An `IEvaluator` seam — the only thing `NeuralPolicy` (the brain-to-
  game adapter) now requires of a network is "give me 9 numbers when I
  give you 127." NEAT phenotypes still slot in through a thin wrapper;
  new network types can plug in the same way without touching any game
  code.
- A standard `MLP` class — a regular 127 → 64 → 64 → 9 feed-forward
  network whose weights live in one flat array. This is the shape the
  evolution-strategies and gradient-based methods want, and it sits
  next to the NEAT phenotype as a peer implementation of `IEvaluator`.

No behavioural change yet — existing trained NEAT populations still
play through `NeuralPolicy` exactly as before. This commit is just the
plumbing the upcoming training-method commits need to plug into.

---

## Rule fix: the bank only has 32 houses and 12 hotels

Real Monopoly ships exactly 32 little green houses and 12 red hotels.
Once the bank runs out, no one can build until someone else sells back.
Players who control the supply can deliberately starve opponents out of
the hotel market — a real strategic lever.

The simulator was pretending the bank had infinite resources: every
build request was granted regardless of how many houses had already been
placed. So networks could happily put hotels on every property they
owned no matter what the rest of the table was doing.

Fixed: each Board now has its own `houseSupply` (starts at 32) and
`hotelSupply` (starts at 12). Building decrements them; selling and
bankruptcy return them. If a build request exceeds available supply, the
build loop stops early and the player pays only for what was actually
built.

The network input doesn't currently see the supply numbers, so trained
networks may over-bid when the bank is dry; they just won't get the
houses they asked for and won't be charged for them either. New training
will get to learn the constraint.

---

## Rule fix: Get Out of Jail Free cards are now real physical cards

There are only two Get Out of Jail Free cards in the game — one in
Chance, one in Community Chest. In real Monopoly, when a player draws
one, they take physical possession of the card; the deck loses it; no
one else can draw it until that player uses or surrenders it.

The simulator was leaving the card in the deck. The drawing player got
a "+1 jail card" counter, but the same card kept cycling through the
deck and the same player could keep drawing more and more of them.
Players could even end the game with multiple "uses" of the same
physical card.

Fixed end-to-end:

- The card now knows which deck it came from (Chance or Community Chest).
- When drawn, it's removed from its deck and added to the player's hand
  rather than rotated to the bottom.
- When used to leave jail, the card is returned to the bottom of its
  source deck.
- If a player goes bankrupt to the bank, their held cards return to
  their source decks.
- If a player goes bankrupt to another player, their held cards transfer
  to the creditor.

The network input still reads "do you have any jail cards" as a 0/1
signal exactly as before — same network compatibility.

---

## Defensive fix: "Go Back 3 Spaces" card no longer relies on tile layout

The Chance card "Go Back 3 Spaces" was subtracting 3 from the player's
position with no wrap-around. The simulator was getting away with it
because all three Chance tiles sit at positions 7, 22, and 36 — every
one of them is at least 3 spaces past GO, so going back 3 lands on a
valid tile. If the tile layout were ever edited, this would silently
produce a negative position and crash the next array lookup.

Added a wrap-around guard. Behavior unchanged with the current layout.

---

## Rule fix: "Advance to nearest Utility" Chance card now rolls fresh dice

The Chance card "Advance to the nearest Utility — throw the dice and
pay the owner ten times the amount thrown" was paying based on the
**original** roll that brought the player onto the Chance tile in the
first place. The card explicitly says to roll again.

Fixed: when the card sends the player to an owned, non-mortgaged
utility, two fresh dice are rolled and the fine is 10× their sum. The
"Advance to the nearest Railroad" card was already correct (it pays
twice the normal train rent, not roll-dependent).

---

## Rule fix: properties go to auction when a player goes bankrupt to the bank

When a player went bankrupt owing money to the bank (a tax tile, a Chance
fine, etc., rather than to another player), the simulator simply dropped
all their properties into the bank's pile and left them there for the
rest of the game. The rulebook says these properties go straight to
auction — the bank wants the cash; the remaining players bid; the
highest bidder takes it.

Fixed: after the bankrupt player is retired, each of their former
properties is auctioned in turn. The same auction code that runs when a
landed-on tile is declined now runs here too. Mortgaged status carries
into the auction unchanged (the simulator doesn't model the 10%
unmortgage obligation either way).

This is a real strategic shift: properties that previously sat
permanently dead now flow back into circulation. Networks that learned
to "bankrupt out" an opponent to lock those tiles away will have to
rethink that line.

---

## Rule fix: no building on mortgaged sets, no mortgaging with houses still on the property

Two related rulebook constraints that the simulator wasn't enforcing:

- **Building.** You can't put a house on a property if any property in
  the same color group is mortgaged. The simulator previously let the
  policy build anyway. Now the build loop skips any set that has a
  mortgaged member.
- **Mortgaging.** You can't mortgage a property that still has houses on
  it; you must sell the houses first. The simulator was silently allowing
  it, leaving houses sitting on a "mortgaged" tile (which earned no rent
  but was visible in the network's input). Now `Mortgage(index)` refuses
  to flip the mortgage flag when houses[index] > 0.

Trained networks that learned to mortgage-with-houses-up as a cash trick
will lose that option. Same goes for trained networks that built on top
of partially-mortgaged sets. Most policies probably didn't depend on
either of these in any reliable way.

---

## Rule fix: corrected Park Place cost and Marvin Gardens base rent

Two number typos in the data tables:

- **Park Place** cost was $250; the real game's is **$350**.
- **Marvin Gardens** base rent (no houses) was $22; should be **$24**.

The other rents on Marvin Gardens (with houses / hotel) and Park Place's
rent ladder were already correct. Networks trained against the old
numbers may slightly mis-value Park Place (it was cheap and they bought
it eagerly) — expect small drift.

---

## Rule fix: owning a complete color set without houses doubles rent

If you own all three of a color group (or both of brown/dark blue) and
nobody's built a house yet, the rulebook says landing on any of those
properties pays double the base rent. This is the core reason completing
a set matters before you can afford to build.

The simulator wasn't applying the doubling: rent was always read straight
from the rent table at whatever house count the property had. So
collecting a full set with zero houses was strategically equivalent to
owning two-out-of-three from the network's point of view — same rent
income, same incentive to trade for the third.

Fixed in `PropertyTile`: when rent is being computed, the new
`Board.OwnsCompleteSet` helper checks whether the owner holds the whole
color group. If they do **and** this property has zero houses, the base
rent is doubled.

Together with the doubles-counter fix, this is the second of the two
changes most likely to push the trained networks toward different play
styles (more interest in completing sets even before they can build).

---

## Rule fix: rolling doubles no longer carries over between turns

In real Monopoly, "three doubles in a row" means three doubles **in the
same turn sequence** — a single turn that keeps going because each roll
was doubles. The simulator was counting doubles cumulatively across the
**whole game**: every double you ever rolled was added to one counter,
and you went to jail when that counter hit three regardless of when the
doubles happened. A player could roll one double in turn 4, another in
turn 9, a third in turn 17, and get jailed on a cold streak.

Fixed. The counter resets at the end of each turn, so the rule now
matches the rulebook: three doubles in *this* turn → jail; rolling a
non-double ends your turn and clears the counter.

This is the single biggest rule divergence in the codebase. Trained
networks were quietly avoiding doubles even on their first roll of a
turn (because their lifetime double counter mattered). They may
re-adapt; expect some training churn.

---

## Saving and loading the population now lives with the population code

Reading and writing the `monopoly_population *.txt` files used to be one
200-line function in the project's entry point. It reached deep into the
internals of several other classes and used a custom mix of five
delimiter characters. Adding a field to the saved data meant editing the
entry-point file rather than the class that owned the data.

Each class now knows how to write itself: `Marking`, `Genotype`,
`Species`, and `Population` each have their own `WriteTo` / `Parse`
methods. The single delimiter set lives in one tiny `SerialDelim` class
so the on-disk format has one source of truth. The entry-point file
collapses to two lines for save and load.

**Verified byte-identical:** both shipped checkpoint files
(`monopoly_population 162.txt` and `monopoly_population champions.txt`)
load, save back out, and produce files identical to the originals — no
drift in floating-point formatting or delimiter placement.

---

## Trading and auction logic moved out of the main game file; games can now be replayed

Two chunks of game logic that used to live tangled inside the main `Board`
class — the speculative trade round at the start of each turn, and the
auction that runs when a player passes on a property — now have their
own files (`TradeRound.cs` and `AuctionRound.cs`). Same rules, same
random proposals, same tie-break behavior; just isolated so each can be
exercised on its own.

In the same step, `Board`'s random-number generator is now something the
caller can supply: `new Board(policies, new RNG(42))` constructs a Board
whose dice rolls, card shuffles, trade proposals, and auction tie-breaks
are all reproducible from that seed. The default `new Board(policies)`
constructor still seeds from the system clock as before, so tournament
play is unchanged.

This is the foundation for deterministic-replay debugging — when a
trained network behaves strangely, you can save the seed, re-run that
exact game, and step through what happened.

---

## Network input now computed fresh at each decision, not patched incrementally

The neural network sees the game state as a 127-number vector that says
things like "how much money does player 2 have," "is property 17
mortgaged," "which property is being considered for purchase right now."
The old code maintained this vector incrementally: every time something
changed in the game (a payment, a position update, a mortgage flip), the
code called a corresponding "set" method on a `NetworkAdapter` object.
There were about a hundred of these calls scattered across the game
logic. Forgetting one, or calling one with the wrong argument, silently
corrupted the network's view of the world.

That whole adapter has been replaced with one function — `Projection.Project`
— that builds the 127-number vector from the current game state on
demand, right before each decision. The game logic no longer manages a
network-view buffer at all; it just changes the game state.

**Heads-up on training:** this fixes a couple of small bugs in the old
adapter that were silently shaping how trained networks read their input.
Specifically:

1. When a player bought a property, the network was told the property was
   still unowned (the buyer's index was sent as -1 instead of their seat
   number). Now it correctly says "owned by seat N."
2. The "do you hold a get-out-of-jail-free card" bit was hard-stuck at 1
   due to a typo. Now it reads 1 if you actually hold a card, 0 if not.
3. Selection bits from a previous decision could leak into the next one
   (e.g. flags set during a property buy were still active when the next
   mortgage decision was asked). Now each decision sees a clean slate.

The trained-network files load and play, and the layout of the 127-number
vector is unchanged so the networks' weights still apply. But because the
inputs they see are now slightly different (more correct) than the inputs
they were trained against, the **trained networks may make slightly
different choices on the same game state**. If you compare results to
previous runs you might notice small drift; this is expected. New
training generations will quickly re-optimize against the corrected
inputs.

---

## Each board space owns its own "what happens when you land here" rule

The 40 board spaces used to share one giant 180-line block that walked
through every space type with nested `if` statements: properties had buy /
own / pay-rent paths, trains had their own with a slightly different
shape, utilities yet another with a different rent formula, plus tax,
Chance, Community Chest, and Go-To-Jail. The result was a single function
that nobody could safely change without re-reading the whole thing.

Each space is now its own small class — `PropertyTile`, `TrainTile`,
`UtilityTile`, `TaxTile`, `ChanceTile`, `ChestTile`, `GoToJailTile`, plus
no-op spaces like GO and Free Parking. Landing on a space is one call into
that tile's `Activate` method. Adjusting a rent rule or a tax rate now
touches one class, not a buried branch.

Behavior is preserved verbatim, including a small quirk specific to
trains that already existed in the original code (noted as a comment in
the new file).

---

## Chance and Community Chest cards now live in their own files

Each Chance / Community Chest card used to be a generic "card type + number"
pair, with the actual effects of every card buried in two huge `if/else`
blocks inside the board file. Five effect types — collect money, pay money,
advance to a space, go to jail, get-out-of-jail-free — appeared in *both*
blocks with subtly different copies of the same logic, which is the kind of
duplication that drifts over time.

Each card is now its own small class with an `Apply` method that says what
the card does. Drawing a card is one line: rotate the top of the deck and
call its `Apply`. The deck contents are listed in a single readable place
(`Cards.cs`). Card effects, deck contents, and the cards' presence in each
deck are now obvious from one file each.

No rule changes. Same 16 cards in each deck, same effects, same shuffle.

---

## Separated a player's game state from how they make decisions

A "player" in the code used to mean two different things stitched together:
their game-state (where their token is, how much money they have, what
properties they own) and their decision-making (whether to buy, mortgage,
trade, etc.). The neural-network version inherited from the basic version
and overrode every decision method. This made the project hard to extend:
you couldn't, for instance, pit a learned network against the simple
hard-coded "always buy" baseline without subclass gymnastics, and you
couldn't test a network's decisions without standing up an entire game.

Now those two things are separate. `Player` is just the in-game record
(position, funds, items, etc.). `IPolicy` is the decision-making
interface, with `NeuralPolicy` for trained networks and `ScriptedPolicy`
for the hard-coded baseline. The board holds a player **and** a policy
for each seat.

For tournaments this changes nothing visible — the same four trained
networks play the same games, get the same scores, and the same network
files load. It opens the door to evaluating a trained network against the
scripted baseline as a sanity-check opponent, or to writing tests that
ask "what would this network do in this exact situation?" without
simulating a full game.

---

## Fixed a threading hazard in the network evaluator

Tournament play runs 20 game-playing threads in parallel, and the same
trained network can be in several games at once. The evaluator was storing
intermediate calculations *inside* the network object, which meant two
threads scribbling on the same memory at the same time. It mostly produced
plausible-looking numbers, but the results were not reproducible from one
run to the next and a same-network-vs-itself game could behave differently
depending on which threads happened to interleave.

The intermediate values now live in a per-call scratch buffer instead, so
each thread keeps its own working memory and the trained network itself is
read-only during play. No change to the trained networks' weights or to
how decisions are made — only to how cleanly the computation runs in
parallel.

---

## Setup: switched to .NET 8

The project was set up for .NET 5, which Microsoft stopped supporting in 2022.
Bumped it to .NET 8 (the current long-term-support version) so it can be
built and run on a fresh machine without hunting down old runtimes. The
code itself didn't change — only the version label in the project file.

**What you might need to do:** install the .NET 8 SDK (`dotnet-sdk-8.0`) if
you don't already have it. Existing trained-network files load unchanged.
