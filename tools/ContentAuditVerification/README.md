# Content package audit

Run from the repository root:

```sh
dotnet run --project tools/ContentAuditVerification/ContentAuditVerification.csproj -c Release -- --output /tmp/content-audit.json
```

This independent executable compiles the actual pure-C# Localization catalog/parser, Story core and Diagnostics probe/comparer sources. It checks placeholder indices 0–63 and mismatches, malformed formats, escaped braces, table snapshots, a 20,001-node story graph, cycle/unreachable rejection, private-getter refusal, and independent lease/session differences.

It also reports nine descriptive samples after warmup for catalog construction, parent-language queries, large lease captures/differences and immediate story transitions. Allocation counters measure the executing managed thread. The resource provider is a finite public-API fixture; there is no native loading or memory simulation. Wall-clock times on a shared VM are noisy and are not CI pass/fail thresholds. The immediate presenter is deliberately synthetic; its 1,000 transitions are not a Unity frame workload.

The sparse-index catalog workload uses 10,000 rows in two languages containing placeholders 0 and 63. Compared with the previous implementation, its validation uses a 64-bit argument mask and a private shared stateless probe array, preserving the existing composite-format checks while avoiding per-cell sets, arrays and probe objects. Repeated authored lookups perform no managed allocations after warmup.

Real TMP/Video/Unity lifecycle acceptance remains in `tools/NativeAcceptance`; the native diagnostics suite also checks private-getter refusal on Unity's runtime.
