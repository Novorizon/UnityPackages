# Localization, diagnostics, grid and URP verification

`python3 tools/workspace.py verify --dotnet /path/to/dotnet` also generates the localization template using the pinned official Luban CLI, builds the generated C# and ByteBuf, and exercises the actual catalog/service/diagnostic probe. UI.Tests covers actual grid and uGUI localization components using the existing engine boundary model.

The grid tests exercise 10,003 cells, a partial last row, bounded row reuse, source replacement from callbacks, cleanup and prewarmed allocations. Localization covers copied tables, indexed format validation, language/parent/fallback lookup, failed/cancelled/stale preparations, teardown and hidden-view subscriptions. Diagnostics checks that Instance is never accessed and missing/failing providers cannot prevent other metrics.

For managed API compatibility against installed Unity and URP template assemblies:

```sh
python3 tools/FeatureVerification/compile-native.py --editor /path/to/2022.3.62f1/Editor --dotnet /path/to/dotnet
```

Add `--include-tests` to also compile both native GPU test assemblies (restores NUnit 3.13.3 from NuGet). This compiles C# only; it does not launch Unity or execute shaders. Real import/Shader/Play/IL2CPP acceptance requires a licensed Editor. GPU URP EditMode checks ship in the URP package; the generated standalone verification project lists both GPU packages as testable. Real fonts, layouts, rendering and target-device batching remain native checks.


Dynamic list coverage includes bounded 10,000-item windows, keyed insertion/removal anchors, stale asynchronous height reports, width invalidation, batched visible measurements, callback reentry/failures, delayed destruction, randomized differential height/seek checks, and prewarmed allocation checks. This uses the managed geometry model; native Canvas layout and ScrollRect input still need acceptance.

For a supplementary D3D11 HLSL syntax check against the installed official URP 14 source:

```sh
python3 tools/FeatureVerification/compile-shaders.py --editor /path/to/2022.3.62f1/Editor --dxc /path/to/dxc
```

Use an official standalone Microsoft DXC Linux build (verified with 1.9.2609.5). The script compiles the actual shared HLSL and each pass's vertex/fragment entry points with representative instancing/stereo, main/additional/cascaded/screen-space/punctual shadows, soft-shadow quality, SSAO, fog, Forward+ and oct-normal variants. It uses HLSL 2018 and legacy macro expansion matching Unity's include conventions. DXC/SM6 compilation checks syntax and source API use; it does not reproduce Unity 2022's platform compiler, SM3.5 translation, keyword stripping, rendering or device behavior. DXC and Unity are optional local tools, so this check is separate from the portable managed CI.
