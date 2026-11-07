#!/usr/bin/env python3
"""Compile actual URP shader entry points/representative variants with standalone DXC.

Checks D3D11 HLSL syntax against installed URP sources. This is not Unity import,
platform variant stripping, visual/shadow acceptance, or a target-device test.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
SHADERS = ROOT / 'Packages/com.novorizon.gpuanimator.urp/Shaders'


def passes(source):
    for match in re.finditer(r'\bPass\s*\{', source):
        depth, end = 1, match.end()
        while depth:
            depth += (source[end] == '{') - (source[end] == '}')
            end += 1
        body = source[match.end():end - 1]
        yield re.search(r'Name\s+"([^"]+)"', body).group(1), body


def variants(name):
    result = [(), ('INSTANCING_ON',), ('INSTANCING_ON', 'STEREO_INSTANCING_ON')]
    if name == 'ForwardLit':
        result += [
            ('_MAIN_LIGHT_SHADOWS', '_ADDITIONAL_LIGHTS', '_ADDITIONAL_LIGHT_SHADOWS', '_SHADOWS_SOFT', 'FOG_LINEAR'),
            ('_MAIN_LIGHT_SHADOWS_CASCADE', '_ADDITIONAL_LIGHTS_VERTEX', 'FOG_EXP'),
            ('_MAIN_LIGHT_SHADOWS_SCREEN', '_SCREEN_SPACE_OCCLUSION', 'FOG_EXP2'),
            ('_MAIN_LIGHT_SHADOWS', '_SHADOWS_SOFT_LOW'),
            ('_MAIN_LIGHT_SHADOWS', '_SHADOWS_SOFT_MEDIUM'),
            ('_MAIN_LIGHT_SHADOWS', '_SHADOWS_SOFT_HIGH'),
            ('INSTANCING_ON', '_FORWARD_PLUS', '_MAIN_LIGHT_SHADOWS_CASCADE', '_ADDITIONAL_LIGHT_SHADOWS', '_SHADOWS_SOFT'),
        ]
    elif name == 'ShadowCaster':
        result += [('_CASTING_PUNCTUAL_LIGHT_SHADOW',), ('INSTANCING_ON', '_CASTING_PUNCTUAL_LIGHT_SHADOW')]
    elif name == 'DepthNormals':
        result += [('_GBUFFER_NORMALS_OCT',), ('INSTANCING_ON', '_GBUFFER_NORMALS_OCT')]
    elif name == 'ForwardUnlit':
        result += [('FOG_LINEAR',), ('FOG_EXP',), ('FOG_EXP2',)]
    return result


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--editor', type=Path, required=True)
    parser.add_argument('--dxc', type=Path, required=True)
    args = parser.parse_args()
    builtin = args.editor / 'Data/Resources/PackageManager/BuiltInPackages'
    version = json.loads((builtin / 'com.unity.render-pipelines.universal/package.json').read_text())['version']
    completed = 0
    with tempfile.TemporaryDirectory(prefix='novorizon-urp-shaders-') as directory:
        directory = Path(directory)
        (directory / 'Packages').mkdir()
        for name in ['com.unity.render-pipelines.core', 'com.unity.render-pipelines.universal', 'com.unity.render-pipelines.universal-config']:
            (directory / 'Packages' / name).symlink_to(builtin / name, target_is_directory=True)
        for shader in sorted(SHADERS.glob('*.shader')):
            source = shader.read_text()
            shared = re.search(r'HLSLINCLUDE(.*?)ENDHLSL', source, re.S).group(1)
            translation = directory / (shader.stem + '.hlsl')
            translation.write_text(shared)
            for name, body in passes(source):
                for variant in variants(name):
                    for stage, profile in [('vertex', 'vs_6_0'), ('fragment', 'ps_6_0')]:
                        entry = re.search(r'#pragma\s+' + stage + r'\s+(\w+)', body).group(1)
                        command = [str(args.dxc), '-HV', '2018', '-flegacy-macro-expansion', '-T', profile, '-E', entry,
                                   '-D', 'SHADER_API_D3D11=1', '-D', 'SHADER_TARGET=45',
                                   '-D', 'SHADER_STAGE_' + ('VERTEX' if stage == 'vertex' else 'FRAGMENT') + '=1',
                                   '-I', str(directory), '-I', str(SHADERS),
                                   '-I', str(args.editor / 'Data/CGIncludes'), str(translation),
                                   '-Fo', str(directory / 'output.dxil')]
                        for keyword in variant:
                            command += ['-D', keyword + '=1']
                        result = subprocess.run(command, capture_output=True, text=True)
                        if result.returncode:
                            raise RuntimeError(f'{shader.name}/{name}/{stage}/{variant}\n{result.stdout}{result.stderr}')
                        completed += 1
    print(f'PASS {completed} DXC D3D11 HLSL entry-point/variant compiles against URP {version}. Unity import/rendering was not executed.')


if __name__ == '__main__':
    main()
