# Resource ownership diagnostics regression

Run `dotnet run --project tools/DiagnosticsOwnershipVerification -c Release` from the repository root. Run again with `-p:OwnershipDevelopment=false` to verify that stack capture stays disabled in non-development code even when the API is set to true.

The isolated executable compiles the real resource adapter and diagnostic snapshot/comparison code, with the existing Unity and YooAsset API models. It verifies owner isolation, the original literal-default cancellation-token call, cancellation and failed loading cleanup, pending native-release visibility, scope/scene metadata, opt-in stack capture, immutable scalar records, session identity, and baseline changes. Compilation outputs are private to this project and do not rebuild shared verification projects.

These checks do not simulate native resource memory or replace Unity Editor acceptance. The Editor panel is separately compiled against real Unity Editor assemblies; native resource release must be verified with a YooAsset package in Unity.
