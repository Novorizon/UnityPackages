#!/usr/bin/env python3
"""Maintain, validate and export the multi-package Unity repository."""
import argparse
import gzip
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ALL = 'com.novorizon.framework.all'
OFFICIAL = {'com.code-philosophy.hybridclr', 'com.tuyoogame.yooasset'}


def read(path):
    return json.loads(path.read_text())


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def definitions():
    result = {}
    for path in sorted((ROOT / 'Packages').iterdir()):
        if not path.is_dir():
            continue
        metadata = read(path / 'package.json')
        assert metadata['name'] == path.name and path.name not in result
        assert path.name.startswith('com.novorizon.'), ('Invalid maintained package prefix', path.name)
        result[path.name] = metadata
    return result


def package_bytes(folder, extras=None):
    files = {p.relative_to(folder).as_posix(): p.read_bytes() for p in folder.rglob('*') if p.is_file() and not any(v in {'bin', 'obj', '__pycache__'} for v in p.parts)}
    files.update(extras or {})
    result = io.BytesIO()
    with gzip.GzipFile(filename='', mode='wb', fileobj=result, mtime=0) as gz, tarfile.open(fileobj=gz, mode='w') as tf:
        for name, data in sorted(files.items()):
            entry = tarfile.TarInfo('package/' + name)
            entry.size = len(data)
            entry.mode = 0o644
            tf.addfile(entry, io.BytesIO(data))
    return result.getvalue()


def snapshots():
    return {name: package_bytes(ROOT / 'Packages' / name) for name in definitions() if name != ALL}


def sync():
    config = read(ROOT / 'workspace.json')
    packages = definitions()
    entries = []
    known = set(packages) | set(config['externalPackages'])
    for name, metadata in packages.items():
        if name == ALL: continue
        entries.append(dict(name=name, version=metadata['version'], git=f'{config["repository"]}?path=/Packages/{name}#{config["release"]}', external=False, embed=False, dependencies=[dep for dep in metadata.get('dependencies', {}) if dep in known]))
    for name, metadata in config['externalPackages'].items():
        entries.append(dict(name=name, external=True, **metadata))
    write(ROOT / 'Packages' / ALL / 'Dependencies.json', dict(packages=entries, obsoletePackages=list(config.get('packageReplacements', {}))))
    print(f'Synchronized {len(entries)} dependency references without source payloads.', flush=True)


def validate():
    config, packages = read(ROOT / 'workspace.json'), definitions()
    assert len(packages) == 9
    assert not set(packages) & OFFICIAL
    external = config['externalPackages']
    assert not set(config.get('packageReplacements', {})) & set(packages)
    editor = ROOT / 'Packages/com.novorizon.ability/Editor/Game.Ability.Editor.Odin.asmdef'
    assert read(editor)['includePlatforms'] == ['Editor']
    assert read(editor)['references'] == ['Game.Ability']
    assert read(ROOT / 'Packages/com.novorizon.ability/Game.Ability.asmdef')['references'] == []
    specs = read(ROOT / 'Packages' / ALL / 'Dependencies.json')['packages']
    assert {p['name'] for p in specs} == (set(packages) - {ALL}) | set(external)
    for spec in specs:
        assert 'archive' not in spec and 'sha256' not in spec
        if spec['external']:
            assert spec['version'] == external[spec['name']]['version']
            assert spec['git'] == external[spec['name']]['git']
            assert 'github.com/Novorizon/' not in spec['git']
        else:
            assert spec['version'] == packages[spec['name']]['version']
            assert spec['git'] == f'{config["repository"]}?path=/Packages/{spec["name"]}#{config["release"]}'
    assert not (ROOT / 'Packages' / ALL / 'Payload~').exists()
    def visit(name, ancestors):
        assert name not in ancestors, 'Circular package dependency: ' + name
        for dependency, version in packages[name].get('dependencies', {}).items():
            if dependency in packages:
                assert packages[dependency]['version'] == version, (name, dependency, version)
                visit(dependency, ancestors + [name])
            elif dependency in external:
                assert external[dependency]['version'] == version
            else:
                assert dependency.startswith('com.unity.'), (name, 'unresolved dependency', dependency)
    for name in packages:
        visit(name, [])
    assert not any(name in packages['com.novorizon.framework'].get('dependencies', {}) for name in packages if name != 'com.novorizon.framework')
    assert 'com.novorizon.ability' not in packages['com.novorizon.ecs'].get('dependencies', {})
    assert 'com.novorizon.ecs' not in packages['com.novorizon.ability'].get('dependencies', {})
    asmdefs, guids = {}, {}
    for package in (ROOT / 'Packages').iterdir():
        if not package.is_dir():
            continue
        for file in package.rglob('*'):
            if not file.is_file() or any(part.endswith('~') for part in file.relative_to(package).parts):
                continue
            if file.suffix == '.asmdef':
                definition = read(file)
                assert definition['name'] not in asmdefs, ('Duplicate assembly', file, asmdefs.get(definition['name']))
                asmdefs[definition['name']] = file
            if file.suffix == '.meta':
                match = re.search(r'^guid: ([a-fA-F0-9]{32})$', file.read_text(), re.M)
                if match:
                    assert match[1] not in guids, ('Duplicate active GUID', file, guids.get(match[1]))
                    guids[match[1]] = file
    baseline = read(ROOT / 'Documentation/ImportBaseline.json')
    doc_changes = []
    layout = read(ROOT / 'Documentation/PackageMoves.json')
    def relocated(relative):
        for previous, current in sorted(layout['pathMoves'].items(), key=lambda item: len(item[0]), reverse=True):
            if relative == previous or relative.startswith(previous.rstrip('/') + '/'):
                return current.rstrip('/') + relative[len(previous.rstrip('/')):]
        return relative
    checked_files, external_files = 0, 0
    for relative, expected in baseline.items():
        if relative.split('/')[1] in layout.get('externalizedPackages', []):
            external_files += 1
            assert not (ROOT / 'Packages' / relative.split('/')[1]).exists()
            continue
        if relative in layout['retiredFiles']:
            assert not (ROOT / relative).exists()
            continue
        checked_files += 1
        file = ROOT / relocated(relative)
        assert file.is_file(), ('Missing imported file', relative)
        if hashlib.sha256(file.read_bytes()).hexdigest() != expected:
            assert (file.suffix == '.md' and relative.split('/')[1] not in OFFICIAL) or file.relative_to(ROOT).as_posix() in layout['metadataChanges'] or file.relative_to(ROOT).as_posix() in layout.get('packagingToolChanges', []), ('Executable code or upstream package changed', relative)
            doc_changes.append(relative)
    assert not (ROOT / 'Projects').exists(), 'Unity projects belong in their own repository'
    assert not (ROOT / 'Assets').exists() and not (ROOT / 'ProjectSettings').exists()
    report = dict(packages=len(packages), ownedPackages=len(packages), externalDependencyReferences=len(external), importedFilesChecked=checked_files, externalHistoricalFilesOmitted=external_files, unchangedRuntimeAndAssetFiles=True, updatedDocumentation=doc_changes, activeAssemblies=len(asmdefs), activeGuids=len(guids), nativeUnityExecuted=False)
    print('PASS package dependency closure, source hashes, assembly/GUID uniqueness and package-only repository structure:', json.dumps({k: v for k, v in report.items() if k != 'updatedDocumentation'}), flush=True)
    return report


def verify(dotnet):
    report = validate()
    with tempfile.TemporaryDirectory(prefix='unity-packages-verify-') as temp:
        env = os.environ.copy()
        env.setdefault('DOTNET_CLI_HOME', str(Path(temp) / 'dotnet-home'))
        env.setdefault('DOTNET_PROCESSOR_COUNT', '2')
        for folder, project_name in [('AllPackagesVerification', 'AllPackagesVerification'), ('RTreeCorrectness', 'Correctness'), ('RTreeSpatialCorrectness', 'SpatialCorrectness')]:
            project = ROOT / 'tools' / folder / (project_name + '.csproj')
            subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo'], cwd=temp, env=env, check=True)
            subprocess.run([dotnet, str(project.parent / 'bin/Release/net8.0' / (project_name + '.dll'))], cwd=ROOT, env=env, check=True)
        for name in ['ApiContract/ApiContract', 'Editor/EditorTests']:
            project = ROOT / 'tools/AbilityVerification' / (name + '.csproj')
            subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo'], cwd=temp, env=env, check=True)
            subprocess.run([dotnet, str(project.parent / 'bin/Release/net8.0' / (project.stem + '.dll'))], cwd=ROOT, env=env, check=True)
        subprocess.run([dotnet, 'build', str(ROOT / 'tools/AbilityVerification/Editor/EditorUiCompile.csproj'), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo', '-p:OdinEnabled=false'], cwd=temp, env=env, check=True)
    write(ROOT / 'Documentation/Verification.json', report)


def export(output):
    validate()
    output.mkdir(parents=True, exist_ok=True)
    metadata = definitions()
    for name, data in snapshots().items():
        archive = f'{name}-{metadata[name]["version"]}.tgz'
        (output / archive).write_bytes(data)
    aggregate = f'{ALL}-{metadata[ALL]["version"]}.tgz'
    (output / aggregate).write_bytes(package_bytes(ROOT / 'Packages' / ALL))
    sources = [p for p in ROOT.rglob('*') if p.is_file() and not any(part in {'.git', 'bin', 'obj', '__pycache__', 'Library', 'Temp', 'Logs', 'UserSettings', 'Artifacts'} or part in {'OptionalArchives~', 'NovorizonFrameworkAll~'} for part in p.relative_to(ROOT).parts)]
    zip_path = output / 'UnityPackages-source.zip'
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(sources):
            entry = zipfile.ZipInfo('UnityPackages/' + file.relative_to(ROOT).as_posix(), (2026, 11, 5, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, file.read_bytes())
    checksums = []
    for file in sorted(output.iterdir()):
        if file.suffix in {'.tgz', '.zip', '.bundle'}:
            checksums.append(hashlib.sha256(file.read_bytes()).hexdigest() + '  ' + file.name)
    (output / 'SHA256SUMS').write_text('\n'.join(checksums) + '\n')
    print('Exported', zip_path, 'and', aggregate, flush=True)


def unity_project(output):
    output = output.resolve()
    if output == ROOT or ROOT in output.parents:
        raise ValueError('Generate the Unity test project outside the package repository.')
    config, packages = read(ROOT / 'workspace.json'), definitions()
    deps = dict(packages[ALL].get('dependencies', {}))
    deps['com.unity.test-framework'] = '1.1.33'
    for name in packages:
        deps[name] = f'{config["repository"]}?path=/Packages/{name}#{config["release"]}'
    for name, metadata in config['externalPackages'].items():
        deps[name] = metadata['git']
    (output / 'Assets').mkdir(parents=True, exist_ok=True)
    (output / 'ProjectSettings').mkdir(parents=True, exist_ok=True)
    write(output / 'Packages/manifest.json', dict(dependencies=dict(sorted(deps.items()))))
    (output / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + config['unity'] + '\n')
    print('Generated standalone Unity verification project:', output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('sync')
    sub.add_parser('check')
    v = sub.add_parser('verify')
    v.add_argument('--dotnet', default='dotnet')
    u = sub.add_parser('unity-project')
    u.add_argument('--output', type=Path, required=True)
    p = sub.add_parser('export')
    p.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'sync': sync()
    elif args.command == 'check': validate()
    elif args.command == 'verify': verify(args.dotnet)
    elif args.command == 'unity-project': unity_project(args.output)
    else: export(args.output.resolve())


if __name__ == '__main__':
    main()
