# HybridCLR headless checks

Compiles the maintained package runtime and editor source, the real Framework singleton,
and the pinned official HybridCLR runtime and analyzers. Runtime, Editor, Core and official HybridCLR APIs compile as separate DLLs. Project references are direct only, with transitive project references disabled; workspace validation checks them against both production asmdefs. Unity import, compilation commands,
native installation, networking and IL2CPP execution are explicit API boundaries.

`Program.cs` checks explicit startup, acknowledgement, cancellation, timeout, cleanup and
code-based build configuration. `PlayerCompile.csproj` compiles the non-Editor loading path;
it does not execute a native HybridCLR player. Official sources are fetched into a temporary
verification cache, outside the repository and exported packages.

Run the repository's `python3 tools/workspace.py verify --dotnet /path/to/dotnet`.

The Editor package must reference Game.Framework.Core directly when accessing the inherited ConfigurableSingleton API. A Runtime reference alone is insufficient in Unity. Unity engine/editor APIs remain explicit headless models; this does not claim native Unity compilation.
