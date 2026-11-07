# Foundation correctness and allocation audit

This isolated executable compiles the real Framework Core and Network sources. It does not reference the shared verification projects or their Unity boundary models.

```sh
dotnet run --project tools/FoundationAuditVerification/FoundationAuditVerification.csproj -c Release
DOTNET_TieredCompilation=0 dotnet run --project tools/FoundationAuditVerification/FoundationAuditVerification.csproj -c Release -- --benchmark
```

The default run checks eight scenarios: a held disconnect cannot reuse retired successful connection readiness; connection-loop dispatch failure reaching all connection waiters and releasing the transport; a seeded 4,000-timer heap differential test; event snapshot invalidation; typed value/reference/filtered/erased-topic event compatibility with zero allocation; worker-thread typed snapshots; reference-pool ownership and generations; and main-thread host replacement isolation. A controlled transport is used for the failure regression; this executable does not replace the real TCP/crypto/loopback suite.

`--benchmark` emits one `FOUNDATION_PERFORMANCE_JSON` line. Each metric has 10,000 warm-up operations, nine measured samples, per-operation timing min/median/max, and current-thread managed allocation counts. All measured warmed operations are required to allocate zero bytes, including typed struct `EventManager.Notify`; the previous object-payload path boxed that struct at 24 bytes per publication on .NET 8 x64. Do not infer a target-device timing improvement by comparing independently loaded cloud runs.

Timer measurements separate an empty heap, 65,536 future timers (no due callbacks), cached scheduling/cancellation, and scheduling/cancelling a new earliest timer among 65,535 pending entries. The 64-item UnityThread metric describes one whole posting/draining batch, not one callback. Setup, cold capacity growth, registration/unregistration, worker-post closures, delegate creation, I/O, Unity rendering and device behavior are outside these allocation measurements. Passing a struct already erased to `IMessage`, or a topic freshly boxed to `Enum` by the caller, still allocates at that caller boundary; use the fully generic overload for high-frequency value messages.

Timings are informational and have no hardware-specific pass threshold. They are .NET measurements of pure runtime logic; Unity Mono/IL2CPP, native frame allocations, scene loading, storage filesystems, graphics and physical devices require their own acceptance.
