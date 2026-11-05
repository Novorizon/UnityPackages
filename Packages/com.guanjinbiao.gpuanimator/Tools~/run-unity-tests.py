#!/usr/bin/env python3
"""Run the actual package EditMode tests; a native Editor and Unity Test Framework are required."""
import argparse,subprocess,xml.etree.ElementTree as ET
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--unity',required=True,type=Path);p.add_argument('--project',required=True,type=Path);p.add_argument('--output',required=True,type=Path);p.add_argument('--timeout',type=int,default=1200);a=p.parse_args()
if not a.unity.is_file():p.error('Unity Editor executable was not found')
if a.output.exists():p.error('Output must be a new directory')
a.output.mkdir(parents=True);xml=a.output.resolve()/'results.xml';log=a.output.resolve()/'editor.log'
command=[str(a.unity.resolve()),'-batchmode','-nographics','-projectPath',str(a.project.resolve()),'-runTests','-testPlatform','editmode','-testFilter','GpuAnimatorTests','-testResults',str(xml),'-logFile',str(log)]
result=subprocess.run(command,timeout=a.timeout,check=False)
if result.returncode!=0 or not xml.is_file():raise SystemExit('Native Unity test run failed; inspect '+str(log))
root=ET.parse(xml).getroot();total=int(root.get('total','0'));passed=int(root.get('passed','0'));failed=int(root.get('failed','0'))
if total<=0 or failed or passed!=total:raise SystemExit('Incomplete or failing native test result; inspect '+str(xml))
print('PASS native Unity EditMode:',passed,'tests;',xml)
