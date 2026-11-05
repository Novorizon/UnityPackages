# Portable editor verification

Run from the source bundle root with .NET 8:

```sh
dotnet build Tools/AbilityVerification/Editor/EditorTests.csproj --disable-build-servers -m:1 -nr:false
dotnet Tools/AbilityVerification/Editor/bin/Debug/net8.0/EditorTests.dll
```

The runner requires CodecTests, WorkspaceTests, SandboxTests and PreflightTests; a missing suite fails explicitly. Tests and production editor Core/Sandbox sources compile into separate assemblies. They exercise real JSON fixtures, deterministic export, default/null semantics, conflicts and safe saves, cross-file publication, nonrecursive long-reference preflight, and isolated runtime budget diagnostics. Runtime math/JSON compatibility comes from the existing portable host, not Unity itself. Keep builds sequential when several agents or terminals use the same checkout.

## Odin API compilation

The additional compile check uses **the project's actual Odin reference assemblies**, supplied locally, and small Unity API substitutes:

```sh
dotnet build Tools/AbilityVerification/Editor/EditorUiCompile.csproj --disable-build-servers -m:1 -nr:false -p:OdinReferencePath=/absolute/path/to/Sirenix/Assemblies
dotnet build Tools/AbilityVerification/Editor/EditorUiCompile.csproj --disable-build-servers -m:1 -nr:false -p:OdinEnabled=false
```

The local reference directory must provide `Sirenix.OdinInspector.Editor.dll`, `Sirenix.OdinInspector.Attributes.dll`, `Sirenix.Utilities.Editor.dll`, `Sirenix.Utilities.dll`, `Sirenix.Serialization.dll` and `Sirenix.Serialization.Config.dll`. These files are not distributed with this repository or its skill/editor ZIPs. Use the Odin installation licensed for your project.

`UiCompile/UnityCoreReference`, `UnityImGuiReference` and `UnityEditorReference` deliberately use separate Unity assembly names so references in the real Odin metadata resolve consistently. The build can detect Odin method/override/attribute mismatches and C# integration errors. It **cannot establish real Unity API correctness**, Odin drawing/serialization lifecycle, Undo/Redo behavior, GUI layout or visual quality: the substitute API is only the explicit subset required to compile these sources. It never enters `Assets`, `Packages`, Unity or a Player.

Before release, run the editor with the actual Unity 2022.3 and selected Odin 3.x installation. Verify import, repeated window open/close, selection/reload, null fields remaining untouched during repaint, list editing, Undo/Redo, dirty/recovery state, external-file conflicts, save failures, compilation reload and dark/light layout at the intended window sizes. Then repeat the JSON round-trip/runtime scenario assertions using Unity's real Test Runner. A successful portable or reference build does not replace those checks.
