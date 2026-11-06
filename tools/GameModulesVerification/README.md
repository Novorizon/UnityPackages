# Game modules verification

Compile the actual Framework Core and Unity runtime, audio, config and YooAsset adapter into separate assemblies with transitive project references disabled. Engine references and YooAsset APIs are explicit test-only boundary models; no third-party implementation is shipped in Packages.

The executable exercises typed data ownership, reverse/failing cleanup, real temporary-file replacement/backup/migration, transactional config reloads and cancellation, scene queue ownership and cancellation, audio handle reuse/pause/volume/reentrancy, and resource shutdown regressions. JSON uses a test serializer, not native JsonUtility. Scene/audio models do not verify rendering, audio output or native asset I/O.

```sh
dotnet build tools/GameModulesVerification/GameModulesVerification.csproj -c Release -m:1 --disable-build-servers
dotnet tools/GameModulesVerification/bin/Release/net8.0/GameModulesVerification.dll
```

Native Unity 2022.3 import/compilation, player JsonUtility generics, streamed clips, scene bundles and platform file-system persistence are separate acceptance checks.
