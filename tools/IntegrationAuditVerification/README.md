# Resource, release and installer audit

Run these isolated executables from the repository root:

```sh
dotnet run --project tools/IntegrationAuditVerification/IntegrationAuditVerification.csproj -c Release
dotnet run --project tools/IntegrationAuditVerification/ReleaseAudit/ReleaseAudit.csproj -c Release
dotnet run --project tools/IntegrationAuditVerification/InstallerAudit/InstallerAudit.csproj -c Release
```

All compile the real owned package sources with finite Unity/YooAsset/UPM API models; no shared verification projects are rebuilt. The resource executable covers exact-version concurrency, immutable option snapshots, cancellation/native retirement, full-download mode, main-thread cancellation, Play generation isolation and cache-hit allocation. The release executable uses the actual coordinator/policy with an in-memory transport to check integrity rejection, off-thread verification and cancellation at its completion/publication boundaries. The installer executable holds list/add/embed requests past their deadlines and checks that retry cannot overlap a live native request.

The cache benchmark warms the query and measures 20 batches of 10,000 actual `TryGetLoaded` calls. Its allocation check is enforced; latency is diagnostic data without a device-performance threshold. On the current .NET Linux host this recorded 0 allocated bytes for 200,000 calls, with a median around 50 ns per query. Unity's native handle/object checks and real Bundle I/O are not represented by that latency.

For the release sample, an 8 MiB in-memory packaged payload is prepared 12 times after warmup. The preview.34 coordinator hashes that payload twice on the caller thread; the corrected path hashes the exact returned buffer once on a CPU worker. A local comparison recorded median caller/total times of 16.210/16.210 ms before and 0.137/7.563 ms after. These are managed-host measurements under the session's load, not a Unity frame-time or shipping-device guarantee. The worker-affinity and integrity/cancellation regressions are enforced without wall-clock timing thresholds.

A separate `YooAssetAuditTests` PlayMode test constructs the pinned upstream 2.3.18 empty downloader and verifies that an already-cancelled registration boundary still enters and retires through its real operation pump. It needs no Bundle or network; it does not validate real Bundle memory/refcounts, download throughput or platform scene ownership.
