# Changes

A plain-language log of changes to the Monopoly NEAT trainer, newest first.
The trained-network files in this folder (`monopoly_population 162.txt` etc.)
continue to load as before across all changes below.

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
