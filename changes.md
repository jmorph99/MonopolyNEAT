# Changes

A plain-language log of changes to the Monopoly NEAT trainer, newest first.
The trained-network files in this folder (`monopoly_population 162.txt` etc.)
continue to load as before across all changes below.

---

## Setup: switched to .NET 8

The project was set up for .NET 5, which Microsoft stopped supporting in 2022.
Bumped it to .NET 8 (the current long-term-support version) so it can be
built and run on a fresh machine without hunting down old runtimes. The
code itself didn't change — only the version label in the project file.

**What you might need to do:** install the .NET 8 SDK (`dotnet-sdk-8.0`) if
you don't already have it. Existing trained-network files load unchanged.
