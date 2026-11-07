# Game modules verification

Builds actual Framework Core/Unity, input, audio, DataManager and YooAsset adapter sources in separate assemblies with direct references. The pinned official Luban CLI generates the Excel fixture; the actual generated Tables and ByteBuf run in the tests.

Coverage includes file replacement/backup/migration, table snapshots and cancellation, scene queues, audio reuse/fades/routing, input rebinding, exact resource versions, independent leases and shutdown. Unity, JsonUtility, Input System and YooAsset APIs use explicit boundary models; native I/O, rendering, audio and IL2CPP are separate checks.

`ResourcePackageScenarios.cs` checks the separate filesystem initialization stage: `InitializePackageAsync` performs no version request, manifest read or download, publishes only `PackageInitialized`, and leaves content APIs unavailable until exact-version `InitializeAsync` completes. It also covers shared native initialization, caller cancellation, failure/retry, shutdown waits and stale completion across Play generations. These remain boundary-model scenarios rather than claims of native YooAsset execution.

The intended project order is filesystem initialization in GameEntry, configuration-only `HotUpdateManager.Initialize`, then explicit `StartAsync` from the project update module. HybridCLR's manager configuration/start boundary is covered separately by `tools/HotUpdateVerification`.

Run `python3 tools/workspace.py verify --dotnet /path/to/dotnet` from the repository root. Third-party verification dependencies are cached outside the repository. The current native workflow is documented in GameStarter's Documentation/UnityValidation.zh-CN.md.
