# Localization, diagnostics, grid and URP verification

`python3 tools/workspace.py verify --dotnet /path/to/dotnet` also generates the localization template using the pinned official Luban CLI, builds the generated C# and ByteBuf, and exercises the actual catalog/service/diagnostic probe. UI.Tests covers actual grid and uGUI localization components using the existing engine boundary model.

The grid tests exercise 10,003 cells, a partial last row, bounded row reuse, source replacement from callbacks, cleanup and prewarmed allocations. Localization covers copied tables, indexed format validation, language/parent/fallback lookup, failed/cancelled/stale preparations, teardown and hidden-view subscriptions. Diagnostics checks that Instance is never accessed and missing/failing providers cannot prevent other metrics.

For managed API compatibility against installed Unity and URP template assemblies:

```sh
python3 tools/FeatureVerification/compile-native.py --editor /path/to/2022.3.62f1/Editor --dotnet /path/to/dotnet
```

This compiles C# only; it does not launch Unity or execute shaders. Real import/Shader/Play/IL2CPP acceptance requires a licensed Editor. GPU URP EditMode checks ship in the URP package; the generated standalone verification project lists both GPU packages as testable. Real fonts, layouts, rendering and target-device batching remain native checks.
