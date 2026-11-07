# Localization authoring verification

Run from the repository root:

```sh
dotnet run --project tools/LocalizationSetupVerification/LocalizationSetupVerification.csproj
```

This isolated project compiles the real pure Core and Localization sources into its own output. It does not build shared verification projects or use a Unity model.

The scenarios check quoted CSV / TSV fields, UTF-8 BOM headers, line endings, missing cells, copied snapshots, exact translation inspection, duplicate or padded keys, invalid culture/header/row shapes, malformed quoting and placeholder validation. A failed replacement parse retains the preceding validated snapshot.

Unity assets, settings, bootstrap ownership and serialized TMP Key binding are covered by `tools/NativeAcceptance/PlayMode/LocalizationSetupTests.cs` in the installed Unity Editor. The authoring Editor assemblies also require the real Unity API compile check. Inspector interaction, glyph coverage and rendered layout need project font assets and native visual checks.
