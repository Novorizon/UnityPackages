#!/usr/bin/env python3
"""Compile mobile C# paths against real, externally installed SDKs; never contact vendor services."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-root', type=Path, required=True)
    parser.add_argument('--iap-assemblies', type=Path, required=True)
    parser.add_argument('--unity-editor', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    sdk = args.sdk_root.resolve()
    managed = args.unity_editor.resolve().parent / 'Data/Managed/UnityEngine'
    references = list(managed.glob('UnityEngine*.dll'))
    references += list((sdk / 'firebase/Assets/Firebase/Plugins').glob('*.dll'))
    references += [sdk / 'facebook/Assets/FacebookSDK/Plugins/Facebook.Unity.dll']
    references += [sdk / 'facebook/Assets/FacebookSDK/Plugins/Settings/Facebook.Unity.Settings.dll']
    references += list(args.iap_assemblies.glob('Unity.Purchasing*.dll'))
    assert references and all(path.is_file() for path in references)
    assert any(path.name == 'Unity.Purchasing.dll' for path in references), 'Compile the real Unity IAP package in Unity first'
    assert any(path.name == 'Firebase.App.dll' for path in references), 'Extract the official Firebase managed SDK'
    sources = []
    for package, folders in {
        'auth': ['Runtime', 'Native', 'Facebook'],
        'payments': ['Runtime', 'UnityIap'],
        'ads': ['Runtime', 'AppLovin', 'Csj'],
        'firebase': ['Runtime', 'App', 'Analytics', 'Crashlytics', 'RemoteConfig', 'Messaging', 'Auth'],
        'analytics': ['Runtime'],
    }.items():
        for folder in folders:
            sources += list((ROOT / f'Packages/com.novorizon.{package}' / folder).glob('*.cs'))
    max_source = sdk / 'max/Assets/MaxSdk/Scripts'
    assert (max_source / 'MaxSdk.cs').is_file(), 'Extract official MAX runtime source'
    sources += list(max_source.glob('*.cs')) + list((max_source / 'ThirdParty').rglob('*.cs'))
    items = [f'<Reference Include="{escape(path.stem)}"><HintPath>{escape(str(path))}</HintPath></Reference>' for path in references]
    items += [f'<Compile Include="{escape(str(path))}" />' for path in sources]
    items += [f'<ProjectReference Include="{ROOT}/tools/FrameworkServicesVerification/Core/Core.csproj" />']
    env = os.environ.copy()
    env.setdefault('DOTNET_PROCESSOR_COUNT', '2')
    results = []
    with tempfile.TemporaryDirectory(prefix='novorizon-sdk-api-') as temporary:
        for platform in ['Android', 'iOS']:
            folder = Path(temporary) / platform
            folder.mkdir()
            definitions = ('UNITY_ANDROID' if platform == 'Android' else 'UNITY_IOS') + ';NOVORIZON_NATIVE_AUTH;NOVORIZON_CSJ'
            project = folder / 'Api.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework>'
                               '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9</LangVersion>'
                               f'<DefineConstants>{definitions}</DefineConstants><NuGetAudit>false</NuGetAudit>'
                               '</PropertyGroup><ItemGroup>' + ''.join(items) + '</ItemGroup></Project>')
            subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo'], env=env, check=True)
            results.append(dict(platform=platform, csharpApiCompiled=True, nativeDeviceExecuted=False))
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(dict(results=results, nativeIosCompiled=False), indent=2) + '\n')
    print('PASS real-SDK Android/iOS C# API compilation. Native SDK execution and Objective-C compilation were not performed.')


if __name__ == '__main__':
    main()
