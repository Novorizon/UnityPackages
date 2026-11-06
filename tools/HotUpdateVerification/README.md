# HybridCLR headless checks

Compiles the maintained package runtime and editor source, the real Framework singleton,
and the pinned official HybridCLR runtime and analyzers. Unity import, compilation commands,
native installation, networking and IL2CPP execution are explicit API boundaries.

`Program.cs` checks explicit startup, acknowledgement, cancellation, timeout, cleanup and
code-based build configuration. `PlayerCompile.csproj` compiles the non-Editor loading path;
it does not execute a native HybridCLR player. Official sources are fetched into a temporary
verification cache, outside the repository and exported packages.

Run the repository's `python3 tools/workspace.py verify --dotnet /path/to/dotnet`.
