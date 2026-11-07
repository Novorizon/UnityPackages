# Algorithm correctness and allocation audit

Run from the repository root:

```sh
dotnet run --project tools/AlgorithmsAuditVerification/AlgorithmsAuditVerification.csproj -c Release
dotnet run --project tools/AlgorithmsAuditVerification/AlgorithmsAuditVerification.csproj -c Release -- --measure
```

The isolated executable compiles the actual Ability and RTree runtime sources. The Unity math/serialization boundary comes from the existing portable verification model; it is not Unity or IL2CPP execution. There are no shared project outputs or third-party test dependencies.

The 27 regression checks cover finite large/tiny/exact cast ranges, overflowing float subtraction, long projectile sweeps with off-axis candidates, tracking without premature arrival, large direction normalization, aura coverage after moving out of range, independent nested snapshots, duplicate returns, capacity-based pool retention, and warmed stable updates at 1000/4096/4097/10000 abilities. The pre-existing RTree differential suites remain the comprehensive spatial-query correctness oracle.

`--measure` additionally measures seven batches after 100 warmup operations. Each Ability batch has 40 updates; each spatial batch has 1000 queries. Timing output is the median batch mean and maximum batch mean, not p95 latency. Allocation uses `GC.GetAllocatedBytesForCurrentThread`; output formatting and setup are outside the measured windows. The spatial workload contains 10000 grid AABBs and returns 441 intersections per query.

In the managed audit against the preview.34 baseline, idle Ability Update allocated:

| Abilities | Before, bytes/update | After, bytes/update |
| --- | ---: | ---: |
| 1000 | 24 | 0 |
| 4096 | 24 | 0 |
| 4097 | 393464 | 0 |
| 10000 | 786704 | 0 |

The empty queued-order array caused the fixed allocation. Per-list snapshot rejection above 4096 capacity caused the larger discontinuity. The revised pool retains at most eight lists and 32768 total elements per thread/type, matching the previous aggregate element ceiling. Lists consume their `Capacity`, including empty lists; active borrows remain independent and are not counted as retained. Snapshots above the aggregate budget, creation, cold growth, callbacks and emitted gameplay events may still allocate.

StaticBvh and RTreeIndex reusable queries both measured zero bytes per query with pre-sized output/context. Timing varies with JIT, CPU contention and workload; shared-host measurements must not be used as Unity, mobile-device or shipping-frame budgets. ECS and Ability native stable-update checks are in `tools/NativeAcceptance/PlayMode/AlgorithmAuditTests.cs` and require the real Editor acceptance runner.
