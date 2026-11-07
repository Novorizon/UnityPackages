# UI, Audio and Input audit regressions

Run `dotnet run --project tools/UiAudioInputAuditVerification/UiAudioInputAuditVerification.csproj -c Release`.

This isolated executable compiles actual package runtime sources with the repository's finite Unity and Input System models. It does not rebuild the shared verification projects. Its 21 scenarios cover:

- Grid Unbind callbacks refreshing the same row at each column, including interrupted Clear and deferred native destruction; every old/new binding releases exactly once.
- Input action cancellation reentering RebindAsync, attempting to replace bindings, disposing its manager or canceling before native operation construction.
- Fade-curve validation and warmed allocation checks for fade setup, ticks over 32 fading voices and pooled source reuse.

Before the fixes, the basic reentrant grid refresh retained only 49 bindings for 52 active cells; action cancellation could start two interactive rebinding operations; and 8,192 warmed fade-setup calls allocated 196,608 bytes (24 bytes per call from enum boxing). The corresponding checks now pass, and those warmed audio paths allocate zero managed bytes in this model.

Real Unity checks live in `tools/NativeAcceptance/PlayMode/UiAudioInputAuditTests.cs`, `InputRebindAuditTests.cs` and `AudioPerformanceAuditTests.cs`. They cover the grid lifecycle regression, a performed keyboard action's cancellation callback, fixed-list/grid bounds and timing, and fade setup against real Unity APIs. Performance records use `NOVORIZON_PERF` JSON with measured p50/p95 time and managed allocations. Empty UI cells isolate virtualization overhead; they do not measure TMP rebuilding, user binding callbacks, physical audio, touch/gamepad hardware, iPhone/Metal or IL2CPP.
