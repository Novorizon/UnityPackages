"""Fetch pinned official build/test dependencies outside the repository; never vendor them."""
import os
from pathlib import Path
import subprocess
import tempfile

RUNTIME_COMMIT = '061314200e2f03307c4d90b448e1de7b85431905'
HYBRIDCLR_COMMIT = '6cadd24d101f4cbd00cd80190106f6621132d24d'
TOOL_COMMIT = '0e1a6179a524e71cc503c20bebbca47cc59c7619'


def checkout(cache, name, repository, tag, commit, env):
    destination = cache / name
    if not destination.exists():
        subprocess.run(['git', 'clone', '--depth', '1', '--branch', tag, repository, str(destination)], check=True, env=env)
    actual = subprocess.check_output(['git', '-C', str(destination), 'rev-parse', 'HEAD'], env=env, text=True).strip()
    assert actual == commit, (name, 'Unexpected official source commit', actual)
    subprocess.run(['git', '-C', str(destination), 'diff', '--exit-code', 'HEAD'], check=True, env=env)
    return destination


def prepare(root, dotnet, env):
    cache = Path(env.get('NOVORIZON_VERIFY_CACHE', Path(tempfile.gettempdir()) / 'novorizon-interaction-dependencies'))
    cache.mkdir(parents=True, exist_ok=True)
    hybridclr = checkout(cache, 'hybridclr-' + HYBRIDCLR_COMMIT, 'https://github.com/focus-creative-games/hybridclr_unity.git', 'v9.0.3', HYBRIDCLR_COMMIT, env)
    env['HybridCLRPackageRoot'] = str(hybridclr)
    runtime = Path(env['LubanPackageRoot']) if env.get('LubanPackageRoot') else checkout(cache, 'runtime-' + RUNTIME_COMMIT, 'https://github.com/focus-creative-games/luban_unity.git', 'v1.2.0', RUNTIME_COMMIT, env)
    cli = Path(env['LUBAN_CLI']) if env.get('LUBAN_CLI') else cache / ('cli-' + TOOL_COMMIT) / 'Luban.dll'
    if not cli.exists():
        source = checkout(cache, 'tool-' + TOOL_COMMIT, 'https://github.com/focus-creative-games/luban.git', 'v4.12.0', TOOL_COMMIT, env)
        subprocess.run([dotnet, 'publish', str(source / 'src/Luban/Luban.csproj'), '-c', 'Release', '-o', str(cli.parent), '-m:1', '--disable-build-servers', '-p:NuGetAudit=false'], env=env, check=True)
    fixture = cache / 'fixture'
    subprocess.run([dotnet, str(cli), '-t', 'client', '-c', 'cs-bin', '-d', 'bin', '--conf', str(root / 'Packages/com.novorizon.data/Templates~/LubanProject/luban.conf'), '-x', 'outputCodeDir=' + str(fixture / 'code'), '-x', 'outputDataDir=' + str(fixture / 'data')], env=env, check=True)
    env.update(LubanPackageRoot=str(runtime), LubanFixtureRoot=str(fixture), LUBAN_CLI=str(cli), NOVORIZON_DOTNET=dotnet)


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    prepare(Path(__file__).resolve().parents[1], args.dotnet, os.environ)
