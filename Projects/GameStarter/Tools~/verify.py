#!/usr/bin/env python3
"""Compile actual Framework foundation sources and run deterministic API-model scenarios."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def validate_project(root=ROOT):
    manifest = json.loads((root / 'Packages/manifest.json').read_text())
    catalog = json.loads((root / 'Assets/Settings/OptionalModules.json').read_text())['modules']
    modules = {m['id']: m for m in catalog}
    assert len(modules) == len(catalog), 'Duplicate package ID'
    assert manifest['dependencies']['com.novorizon.framework'] == modules['com.novorizon.framework']['source'] or manifest['dependencies']['com.novorizon.framework'] == 'file:OptionalArchives~/' + modules['com.novorizon.framework']['archive']
    for spec in catalog:
        assert spec['source'].startswith('file:'), spec['id']
        local = (root / 'Packages' / spec['source'][5:]).resolve()
        if local.exists():
            identity = json.loads((local / 'package.json').read_text())
            assert (identity['name'], identity['version']) == (spec['id'], spec['version'])
        assert len(spec['sha256']) == 64
        for dependency in spec['dependencies']:
            assert dependency in modules, dependency
    assert modules['com.code-philosophy.hybridclr']['embed']
    assert modules['com.novorizon.framework.hybridclr']['setupMethod'] == 'Configure'
    runtime = json.loads((root / 'Assets/Game/Runtime/GameStarter.Runtime.asmdef').read_text())
    assert set(runtime['references']) == {'Game.Framework.Core', 'Game.Framework.Unity'}
    assert 'Editor' not in runtime.get('includePlatforms', [])
    guids = set()
    for asset in (root / 'Assets').rglob('*'):
        if asset.suffix == '.meta':
            continue
        meta = Path(str(asset) + '.meta')
        assert meta.is_file(), f'Missing meta: {asset}'
        match = re.search(r'^guid: ([a-f0-9]{32})$', meta.read_text(), re.M)
        assert match and match[1] not in guids, f'Invalid or duplicate GUID: {meta}'
        guids.add(match[1])
    entry_guid = re.search(r'^guid: (.+)$', (root / 'Assets/Game/Runtime/GameEntry.cs.meta').read_text(), re.M)[1]
    scene_guid = re.search(r'^guid: (.+)$', (root / 'Assets/Scenes/Bootstrap.unity.meta').read_text(), re.M)[1]
    assert 'guid: ' + entry_guid in (root / 'Assets/Scenes/Bootstrap.unity').read_text()
    assert 'guid: ' + scene_guid in (root / 'ProjectSettings/EditorBuildSettings.asset').read_text()
    archive_root = root / 'Packages/OptionalArchives~'
    for spec in catalog:
        archive = archive_root / spec['archive']
        if archive.exists():
            assert hashlib.sha256(archive.read_bytes()).hexdigest() == spec['sha256'], archive
            with tarfile.open(archive) as tf:
                metadata = json.load(tf.extractfile('package/package.json'))
                assert (metadata['name'], metadata['version']) == (spec['id'], spec['version'])
    return catalog


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--framework', type=Path, help='Actual com.novorizon.framework package directory')
    parser.add_argument('--structure-only', action='store_true')
    args = parser.parse_args()
    catalog = validate_project()
    print('PASS project structure, GUIDs, pinned package sources and archive integrity', flush=True)
    if args.structure_only:
        return
    with tempfile.TemporaryDirectory(prefix='game-starter-verify-') as temp:
        framework = args.framework
        if framework is None:
            sibling = ROOT.parents[1] / 'Packages/com.novorizon.framework'
            if sibling.exists(): framework = sibling
        if framework is None:
            spec = next(m for m in catalog if m['id'] == 'com.novorizon.framework')
            archive = ROOT / 'Packages/OptionalArchives~' / spec['archive']
            if not archive.is_file():
                parser.error('Supply --framework or use the distribution with package archives.')
            with tarfile.open(archive) as tf:
                tf.extractall(temp, filter='data')
            framework = Path(temp) / 'package'
        framework = framework.resolve()
        package = json.loads((framework / 'package.json').read_text())
        assert package['name'] == 'com.novorizon.framework'
        assert package['version'] == next(m['version'] for m in catalog if m['id'] == package['name'])
        assert (framework / 'Runtime/Unity/MonoSingleton.cs').is_file()
        env = os.environ.copy()
        env.setdefault('DOTNET_PROCESSOR_COUNT', '2')
        env.setdefault('DOTNET_CLI_HOME', str(Path(temp) / 'dotnet-home'))
        env.setdefault('DOTNET_SKIP_FIRST_TIME_EXPERIENCE', '1')
        subprocess.run([args.dotnet, 'build', 'Verification~/GameStarter.Verification.csproj', '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo', '-p:FrameworkPackageRoot=' + str(framework)], cwd=ROOT, env=env, check=True)
        subprocess.run([args.dotnet, 'Verification~/bin/Release/net8.0/GameStarter.Verification.dll'], cwd=ROOT, env=env, check=True)


if __name__ == '__main__':
    main()
