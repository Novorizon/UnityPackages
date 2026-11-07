# Analytics verification

The pure projects compile the actual Analytics and Attribution cores separately with production assembly names. The behavioral program covers consent-delayed startup, identity changes, immutable fanout, failure isolation, supported capabilities, revenue, reentrancy, ownership, disposal and callback suppression across collection epochs. It never sends live telemetry.

```sh
dotnet run --project tools/AnalyticsVerification/AnalyticsVerification.csproj -c Release
```

The suite is part of `workspace.py verify`. Native acceptance additionally exercises the Inspector event trigger, an injected AnalyticsManager and background callbacks dispatched by Unity.

For real SDK API validation, install the pinned official ThinkingData, GameAnalytics, AppsFlyer and Adjust SDKs in a separate licensed Unity project. Enable the optional wrappers and compile them together with Firebase. No vendor source, binaries or project credentials belong in this repository.

```sh
python3 tools/AnalyticsVerification/compile-sdks.py \
  --assemblies /path/to/validation-project/Library/ScriptAssemblies \
  --unity-editor /path/to/Editor/Unity \
  --report /tmp/analytics-api.json
```

The validation project's Assets/Firebase directory supplies the actual Firebase managed DLLs. The API compiler checks mobile C# defines against the real SDK assemblies, not substitute vendor APIs. Copy SdkTests into that project's Assets and run `Novorizon.AnalyticsSdk.Tests` in PlayMode. The tests instantiate real Adjust revenue/event types, verify AppsFlyer reserved fields and culture-independent encoding, invoke the ThinkingData SDK validator, and ensure construction/denied startup stays passive. They do not start vendor networking or claim server ingestion.

Actual Android/iOS builds, native dependency resolution, real device identifiers, installation attribution and dashboard arrival require a separately configured app/device. See the [release validation record](../../Documentation/AnalyticsValidation.json).
