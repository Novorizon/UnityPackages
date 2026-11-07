#!/usr/bin/env bash
set -euo pipefail
project_dir="${1:?Usage: run.sh /absolute/project [EditMode PlayMode]}"
shift
editor_bin="${UNITY_EDITOR_PATH:?Set UNITY_EDITOR_PATH to a licensed Unity 2022.3.62f1 executable}"
mkdir -p "$project_dir/ValidationResults"
runner=()
graphics=()
if [ "$(uname -s)" = Linux ]; then
    graphics=(-force-glcore)
    if [ -z "${DISPLAY:-}" ]; then
        runner=(xvfb-run -a -s '-screen 0 1280x800x24')
    fi
fi
"${runner[@]}" "$editor_bin" -batchmode "${graphics[@]}" -projectPath "$project_dir" \
    -executeMethod NativeAcceptanceSetup.ImportTmpResources \
    -logFile "$project_dir/ValidationResults/Setup.log"
platforms=("$@")
if [ "${#platforms[@]}" -eq 0 ]; then
    platforms=(EditMode PlayMode)
fi
for platform in "${platforms[@]}"; do
    case "$platform" in
        EditMode) assemblies='GpuAnimator.Editor.Tests;Game.Framework.GpuAnimation.URP.Tests' ;;
        PlayMode) assemblies='Novorizon.NativeAcceptance.PlayMode' ;;
        *) echo "Unsupported platform: $platform" >&2; exit 2 ;;
    esac
    rm -f "$project_dir/ValidationResults/$platform.xml"
    "${runner[@]}" "$editor_bin" -batchmode "${graphics[@]}" -projectPath "$project_dir" \
        -runTests -testPlatform "$platform" -assemblyNames "$assemblies" \
        -testResults "$project_dir/ValidationResults/$platform.xml" \
        -logFile "$project_dir/ValidationResults/$platform.log"
    python3 - "$project_dir/ValidationResults/$platform.xml" <<'PY'
import sys,xml.etree.ElementTree as ET
root=ET.parse(sys.argv[1]).getroot()
assert int(root.get('total','0'))>0,'No tests executed'
assert root.get('result')=='Passed',root.attrib
print(root.attrib)
PY
done
