# Game modules verification

Compile the actual Framework Core and Unity runtime, input, audio, config and YooAsset adapter into separate assemblies with transitive project references disabled. Engine references and YooAsset APIs are explicit test-only boundary models; no third-party implementation is shipped in Packages.

The executable exercises typed data ownership, reverse/failing cleanup, real temporary-file replacement/backup/migration, transactional config reloads and cancellation, scene queue ownership and cancellation, audio handle reuse/pause/volume/reentrancy, and resource shutdown regressions. JSON uses a test serializer, not native JsonUtility. Scene/audio models do not verify rendering, audio output or native asset I/O.

```sh
python3 tools/workspace.py verify --dotnet /path/to/dotnet
```

The verifier fetches pinned official Luban 1.2.0 binary runtime sources and builds the 4.12.0 CLI into an external temporary cache. It generates the bundled Excel template before compilation, compiles the actual generated C# and ByteBuf, then exercises loading plus the production editor exporter (including paths with spaces, metadata retention and failed-export retention). Input System 1.11.2 is an explicit API model; it does not simulate native controls or interaction timing.

To run only these scenarios, call `interaction_dependencies.prepare` from Python to populate the build/run environment (`LubanPackageRoot`, `LubanFixtureRoot`, `LUBAN_CLI`, `NOVORIZON_DOTNET`), then build and run GameModulesVerification. No third-party implementation is committed to this repository.

Native Unity 2022.3 import/compilation, input devices, player JsonUtility generics, audio output/streaming, scene bundles and platform file-system persistence remain separate acceptance checks.
