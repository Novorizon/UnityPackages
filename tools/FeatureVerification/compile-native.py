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
    parser.add_argument('--project', type=Path, help='Compiled Unity project providing the real YooAsset DLL for adapter API validation')
    parser.add_argument('--include-tests', action='store_true', help='Also compile native GPU NUnit tests; restores NUnit 3.13.3 from NuGet')
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
    if args.project:
        yooasset = args.project / 'Library/ScriptAssemblies/YooAsset.dll'
        if not yooasset.is_file():
            raise RuntimeError('Compile the project with YooAsset installed before validating its adapter.')
        references['YooAsset'] = yooasset
    modules = [
        ('Game.Framework.Core', 'com.novorizon.framework/Runtime/Core', []),
        ('Game.Framework.Unity', 'com.novorizon.framework/Runtime/Unity', ['Game.Framework.Core']),
        ('Game.Framework.UI', 'com.novorizon.framework.ui/Runtime', ['Game.Framework.Unity', 'Game.Framework.Core']),
        ('Game.Framework.UI.Demo', 'com.novorizon.framework.ui/Samples~/Demo/Runtime', ['Game.Framework.UI', 'Game.Framework.Unity', 'Game.Framework.Core']),
        ('Game.Framework.UI.Demo.Editor', 'com.novorizon.framework.ui/Samples~/Demo/Editor', ['Game.Framework.UI.Demo']),
        ('Game.Framework.Localization', 'com.novorizon.localization/Runtime', ['Game.Framework.Core']),
        ('Game.Framework.Localization.Unity', 'com.novorizon.localization/Runtime/Unity', ['Game.Framework.Localization', 'Game.Framework.Core']),
        ('Game.Framework.Localization.UGUI', 'com.novorizon.localization/Runtime/UGUI', ['Game.Framework.Localization', 'Game.Framework.Core']),
        ('Game.Framework.Localization.Editor', 'com.novorizon.localization/Editor/Authoring', ['Game.Framework.Localization', 'Game.Framework.Localization.Unity']),
        ('Game.Framework.Localization.TMP.Editor', 'com.novorizon.localization/Editor/TMP', ['Game.Framework.Localization', 'Game.Framework.Localization.Unity', 'Game.Framework.Localization.UGUI']),
        ('Game.Framework.Story', 'com.novorizon.story/Runtime', []),
        ('Game.Framework.Story.Unity', 'com.novorizon.story/Authoring', ['Game.Framework.Story', 'Game.Framework.Core']),
        ('Game.Framework.Story.UGUI', 'com.novorizon.story/UGUI', ['Game.Framework.Story', 'Game.Framework.Story.Unity', 'Game.Framework.Core', 'Game.Framework.UI', 'Game.Framework.Localization', 'Game.Framework.Localization.UGUI']),
        ('Game.Framework.Story.Video', 'com.novorizon.story/Video', ['Game.Framework.Core', 'Game.Framework.Story.Unity']),
        ('Game.Framework.Story.Editor', 'com.novorizon.story/Editor', ['Game.Framework.Story', 'Game.Framework.Story.Unity']),
        ('Game.Framework.Story.Sample.Basic', 'com.novorizon.story/Samples~/BasicStory/Runtime', ['Game.Framework.Core', 'Game.Framework.Story', 'Game.Framework.Story.Unity', 'Game.Framework.Story.UGUI', 'Game.Framework.Story.Video', 'Game.Framework.Localization', 'Game.Framework.Localization.UGUI', 'Game.Framework.UI']),
        ('Game.Framework.Story.Sample.Basic.Editor', 'com.novorizon.story/Samples~/BasicStory/Editor', ['Game.Framework.Story.Sample.Basic']),
        ('Game.Framework.Data.Editor', 'com.novorizon.data/Editor', []),
        ('Game.Framework.Localization.Luban.Editor', 'com.novorizon.localization/Editor', ['Game.Framework.Data.Editor']),
        ('Game.Framework.Diagnostics.Editor', 'com.novorizon.diagnostics/Editor', []),
        ('GpuAnimator', 'com.novorizon.gpuanimator/Runtime', ['Game.Framework.Unity', 'Game.Framework.Core']),
        ('GpuAnimator.Editor', 'com.novorizon.gpuanimator/Editor', ['GpuAnimator', 'Game.Framework.Unity', 'Game.Framework.Core']),
        ('Game.Framework.GpuAnimation.URP.Editor', 'com.novorizon.gpuanimator.urp/Editor', []),
        ('Game.Framework.All.Editor', 'com.novorizon.framework.all/Editor', []),
    ]
    if args.project:
        modules.append(('Game.Framework.Resources.YooAsset', 'com.novorizon.framework.yooasset/Runtime', ['Game.Framework.Core', 'Game.Framework.Unity']))
    if args.include_tests:
        modules += [
            ('GpuAnimator.Editor.Tests', 'com.novorizon.gpuanimator/Tests/Editor', ['GpuAnimator', 'GpuAnimator.Editor']),
            ('Game.Framework.GpuAnimation.URP.Tests', 'com.novorizon.gpuanimator.urp/Tests/Editor', ['Game.Framework.GpuAnimation.URP.Editor']),
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
            if name.endswith('.Tests'):
                ET.SubElement(items, 'PackageReference', Include='NUnit', Version='3.13.3')
            source = ROOT / 'Packages' / folder
            for file in source.rglob('*.cs'):
                if any(list(parent.glob('*.asmdef')) for parent in file.parents if parent != source and source in parent.parents):
                    continue
                ET.SubElement(items, 'Compile', Include=str(file))
            selected = dict(references)
            selected.update({key: built[key] for key in dependencies})
            for key, file in selected.items():
                ET.SubElement(ET.SubElement(items, 'Reference', Include=key), 'HintPath').text = str(file)
            directory = tmp / name
            directory.mkdir()
            path = directory / (name + '.csproj')
            ET.ElementTree(project).write(path, encoding='unicode')
            subprocess.run([args.dotnet, 'build', str(path), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo'], env=env, check=True)
            built[name] = directory / 'bin/Release/netstandard2.1' / (name + '.dll')
        print(f'PASS {len(modules)} package assembly compiles against installed Unity and URP APIs; native Editor and shaders were not executed.')

if __name__ == '__main__':
    main()
