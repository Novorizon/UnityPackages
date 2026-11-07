# Story core verification

Run `dotnet run --project tools/StoryVerification/StoryVerification.csproj` from the repository root. The isolated executable compiles the real pure-C# story runtime and exercises 28 scenarios covering immutable graph validation, branches, checkpoints, command identity, cancellation, skip, timeouts, busy/cleanup boundaries and control-call races.

Run `dotnet build tools/StoryVerification/Compatibility/RuntimeCompatibility.csproj` to verify the same runtime against netstandard2.1. Both commands run in the workspace verification workflow.

This does not execute Unity. Real TMP, authoring asset reimport, owned leases and video decoding are covered separately by `tools/NativeAcceptance`.
