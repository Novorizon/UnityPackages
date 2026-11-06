#!/usr/bin/env python3
"""Build the Roslyn 3.8 generator shipped inside the Framework UPM package."""
import argparse
import os
import tempfile
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / 'Packages/com.novorizon.framework'
PROJECT = PACKAGE / 'SourceGenerators~/StaticResetGenerator/StaticResetGenerator.csproj'
TARGET = PACKAGE / 'Analyzers/Game.Framework.StaticResetGenerator.dll'


def build(dotnet, check=False):
    subprocess.run([dotnet, 'build', str(PROJECT), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo'], check=True)
    artifact = PROJECT.parent / 'bin/Release/netstandard2.0' / TARGET.name
    if check:
        assert TARGET.is_file() and TARGET.read_bytes() == artifact.read_bytes(), 'Shipped generator is stale; run tools/build-static-reset.py'
    else:
        # The Unity compiler may still map the previous DLL. Never overwrite it in place.
        with tempfile.NamedTemporaryFile(dir=TARGET.parent, suffix='.tmp', delete=False) as pending:
            temporary = Path(pending.name)
        try:
            shutil.copyfile(artifact, temporary)
            os.replace(temporary, TARGET)
        finally:
            temporary.unlink(missing_ok=True)
    print('PASS shipped generator matches Roslyn 3.8 source build.', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    build(args.dotnet, args.check)
