#!/usr/bin/env python3
"""Compile changed package assemblies against installed Unity/URP DLLs, without executing Unity.

This verifies managed API compatibility only. Shader import, rendering and Editor
lifecycle tests still require a licensed Editor.
"""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]

def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--editor', type=Path, required=True, help='Installed Editor directory containing Data/Managed')
    parser.add_argument('--dotnet', required=True)
    args = parser.parse_args()
    managed = args.editor / 'Data/Managed'
    caches = list((args.editor / 'Data/Resources/PackageManager/ProjectTemplates/libcache').glob('*/ScriptAssemblies'))
    cache = next((p for p in caches if (p / 'Unity.RenderPipelines.Universal.Runtime.dll').exists()), None)
    if cache is None:
        raise RuntimeError('No URP template assembly cache found; use a project compiled with URP 14.')
    references = {}
    for directory in [managed, managed / 'UnityEngine', cache]:
        for file in directory.glob('Unity*.dll'):
            references[file.stem] = file
    core = ROOT / 'tools/FrameworkServicesVerification/Core/bin/Release/netstandard2.1/Game.Framework.Core.dll'
    localization = ROOT / 'tools/FeatureVerification/Localization/bin/Release/netstandard2.1/Game.Framework.Localization.dll'
    if not core.exists() or not localization.exists():
        raise RuntimeError('Run workspace verification first to build Core and Localization.')
    modules = [
        ('Game.Framework.Unity', 'com.novorizon.framework/Runtime/Unity', []),
        ('Game.Framework.UI', 'com.novorizon.framework.ui/Runtime', ['Game.Framework.Unity']),
        ('Game.Framework.Localization.UGUI', 'com.novorizon.localization/Runtime/UGUI', []),
        ('Game.Framework.Data.Editor', 'com.novorizon.data/Editor', []),
        ('Game.Framework.Localization.Luban.Editor', 'com.novorizon.localization/Editor', ['Game.Framework.Data.Editor']),
        ('Game.Framework.Diagnostics.Editor', 'com.novorizon.diagnostics/Editor', []),
        ('GpuAnimator', 'com.novorizon.gpuanimator/Runtime', ['Game.Framework.Unity']),
        ('GpuAnimator.Editor', 'com.novorizon.gpuanimator/Editor', ['GpuAnimator', 'Game.Framework.Unity']),
        ('Game.Framework.GpuAnimation.URP.Editor', 'com.novorizon.gpuanimator.urp/Editor', []),
        ('Game.Framework.All.Editor', 'com.novorizon.framework.all/Editor', []),
    ]
    env = os.environ.copy()
    env.setdefault('DOTNET_CLI_HOME', '/tmp/novorizon-native-dotnet-home')
    env.setdefault('DOTNET_PROCESSOR_COUNT', '2')
    with tempfile.TemporaryDirectory(prefix='novorizon-native-compile-') as tmp:
        tmp = Path(tmp)
        built = {}
        for name, folder, dependencies in modules:
            project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
            properties = ET.SubElement(project, 'PropertyGroup')
            for key, value in dict(TargetFramework='netstandard2.1', AssemblyName=name, EnableDefaultCompileItems='false', LangVersion='9.0', DefineConstants='UNITY_EDITOR;UNITY_2022_3_OR_NEWER', NuGetAudit='false', NoWarn='CS0067;CS0649').items():
                ET.SubElement(properties, key).text = value
            items = ET.SubElement(project, 'ItemGroup')
            for file in (ROOT / 'Packages' / folder).rglob('*.cs'):
                ET.SubElement(items, 'Compile', Include=str(file))
            selected = dict(references)
            selected['Game.Framework.Core'] = core
            selected['Game.Framework.Localization'] = localization
            selected.update({key: built[key] for key in dependencies})
            for key, file in selected.items():
                ET.SubElement(ET.SubElement(items, 'Reference', Include=key), 'HintPath').text = str(file)
            directory = tmp / name
            directory.mkdir()
            path = directory / (name + '.csproj')
            ET.ElementTree(project).write(path, encoding='unicode')
            subprocess.run([args.dotnet, 'build', str(path), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo'], env=env, check=True)
            built[name] = directory / 'bin/Release/netstandard2.1' / (name + '.dll')
        print('PASS 10 package assembly compiles against installed Unity and URP APIs; native Editor and shaders were not executed.')

if __name__ == '__main__':
    main()
