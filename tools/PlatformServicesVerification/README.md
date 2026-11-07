# Platform service verification

`dotnet run --project tools/PlatformServicesVerification/PlatformServicesVerification.csproj -c Release` exercises real production core code using deliberately controlled test providers. It verifies account ownership, proof disposal, native-operation cancellation, deferred disposal, payment verification/confirmation ordering, recovery, and reward deduplication. These tests do not make real purchases or contact ad/auth/Firebase services. They run in `workspace.py verify` and CI.

The four pure core assemblies are compiled separately; `workspace.py check` compares their direct references with production asmdefs. Native Unity acceptance also exercises actual GameObject/JSON callback delivery and destruction; SDK absence must compile without vendor dependencies.

For optional SDK API validation, install/download official pinned SDKs outside this repository. Use the cache layout `firebase/Assets/Firebase/Plugins/*.dll`, `facebook/Assets/FacebookSDK/Plugins/Facebook.Unity.dll`, `facebook/Assets/FacebookSDK/Plugins/Settings/Facebook.Unity.Settings.dll`, and `max/Assets/MaxSdk/Scripts/`. Compile Unity IAP 5.5.0 in a separate licensed Unity 2022.3 project and pass its `Library/ScriptAssemblies` directory:

```sh
python3 tools/PlatformServicesVerification/compile-sdks.py \
  --sdk-root /path/to/vendor-cache \
  --iap-assemblies /path/to/sdk-validation-project/Library/ScriptAssemblies \
  --unity-editor /path/to/Editor/Unity \
  --report /tmp/platform-api.json
```

This compiles both Android and iOS C# preprocessor paths against real SDK binaries/source and installed Unity assemblies. It does not replace a Unity import: also enable adapter symbols in a separate Unity project and confirm all expected adapter assemblies exist. SDK binaries, keys and projects must stay outside this package repository.

Copy `SdkTests` to the separate SDK validation project's Assets directory to run `Novorizon.PlatformSdk.PlayMode`. It uses the official MAX Editor runtime, including its original prefabs/resources, and injects deliberate late/duplicate SDK callbacks. It verifies that a previous ad cannot reward or close the next ad. This is an Editor callback test, not proof of native ad delivery; it makes no SDK network request. Keep the complete Facebook Settings dependency present so its runtime assembly can load.

Android Java bridges can be compiled against the pinned CSJ, AndroidX Credentials, Google ID and Android platform jars. A UnityPlayer boundary fixture may declare `currentActivity` and native `UnitySendMessage`; all vendor types must come from actual vendor artifacts. Objective-C++ still requires an Apple SDK/Xcode build. Device purchases, real ad delivery/SSV, sign-in and Firebase delivery require project configuration and are reported separately in the [validation record](../../Documentation/PlatformSdkValidation.json).
