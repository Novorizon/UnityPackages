#!/usr/bin/env python3
"""Compile analytics adapters against real vendor assemblies produced by licensed Unity."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import quoteattr, escape

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assemblies', type=Path, required=True)
    parser.add_argument('--unity-editor', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    refs = list((args.unity_editor.resolve().parent / 'Data/Managed/UnityEngine').glob('UnityEngine*.dll'))
    for name in ['ThinkingAnalytics', 'ThinkingSDK', 'GameAnalyticsSDK', 'AppsFlyer', 'AdjustSdk.Scripts', 'Game.Framework.Firebase', 'Game.Framework.Firebase.App']:
        path = args.assemblies / (name + '.dll')
        assert path.is_file(), path
        refs.append(path)
    project = args.assemblies.parents[1]
    refs += list((project / 'Assets/Firebase').glob('*.dll'))
    sources = []
    for package, folders in {'analytics': ['ThinkingData', 'GameAnalytics', 'Unity'], 'attribution': ['AppsFlyer', 'Adjust'], 'firebase': ['Analytics']}.items():
        for folder in folders:
            sources += list((ROOT / f'Packages/com.novorizon.{package}' / folder).glob('*.cs'))
    items = [f'<Reference Include={quoteattr(path.stem)}><HintPath>{escape(str(path))}</HintPath></Reference>' for path in refs]
    items += [f'<Compile Include={quoteattr(str(path))} />' for path in sources]
    for reference in ['tools/FrameworkServicesVerification/Core/Core.csproj', 'tools/AnalyticsVerification/Analytics/Analytics.csproj', 'tools/AnalyticsVerification/Attribution/Attribution.csproj']:
        items.append(f'<ProjectReference Include={quoteattr(str(ROOT / reference))} />')
    env = os.environ.copy()
    env.setdefault('DOTNET_PROCESSOR_COUNT', '2')
    results = []
    with tempfile.TemporaryDirectory(prefix='analytics-sdk-api-') as temp:
        for platform in ['Android', 'iOS']:
            folder = Path(temp) / platform
            folder.mkdir()
            path = folder / 'Api.csproj'
            define = 'UNITY_ANDROID' if platform == 'Android' else 'UNITY_IOS'
            path.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><LangVersion>9</LangVersion><NuGetAudit>false</NuGetAudit><DefineConstants>' + define + '</DefineConstants></PropertyGroup><ItemGroup>' + ''.join(items) + '</ItemGroup></Project>')
            subprocess.run([args.dotnet, 'build', str(path), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo'], env=env, check=True)
            results.append(dict(platform=platform, realSdkApiCompiled=True, deviceExecuted=False))
    args.report.write_text(json.dumps(results, indent=2) + '\n')
    print('PASS analytics SDK mobile C# API contracts; not a native device build.')


if __name__ == '__main__':
    main()
