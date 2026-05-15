# Changes

A plain-language log of changes to the Monopoly NEAT trainer, newest first.
The trained-network files in this folder (`monopoly_population 162.txt` etc.)
continue to load as before across all changes below.

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
