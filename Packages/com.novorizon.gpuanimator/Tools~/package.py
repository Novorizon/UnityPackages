#!/usr/bin/env python3
import argparse,gzip,json,pathlib,re,tarfile,uuid
r=pathlib.Path(__file__).resolve().parents[1];p=argparse.ArgumentParser();p.add_argument('--output',type=pathlib.Path);p.add_argument('--write-metas',action='store_true');a=p.parse_args()
info=json.loads((r/'package.json').read_text());guids={}
for path in r.rglob('*.meta'):
    if any(part in ('bin','obj','.git','Verification~') for part in path.relative_to(r).parts):continue
    match=re.search(r'^guid: ([a-f0-9]{32})$',path.read_text(),re.M)
    assert match,path
    assert match.group(1) not in guids,(path,guids.get(match.group(1)));guids[match.group(1)]=path
for path in [r/'Documentation~/package-validation.md']:
    meta=pathlib.Path(str(path)+'.meta')
    if a.write_metas and not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'Novorizon.GpuAnimator/'+path.relative_to(r).as_posix()).hex+'\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
assert json.loads((r/'GpuAnimator.asmdef').read_text())['references']==[]
assert json.loads((r/'Editor/GpuAnimator.Editor.asmdef').read_text())['includePlatforms']==['Editor']
assert b'#pragma target 3.5' in (r/'Shaders/GpuSkinningAnimation.shader').read_bytes()
assert 'Game.Framework.Pooling' in (r/'Samples~/FrameworkPool/GpuAnimatorPool.cs').read_text()
if a.output:
    a.output.parent.mkdir(parents=True,exist_ok=True)
    selected=['Runtime','Runtime.meta','Editor','Editor.meta','Shaders','Shaders.meta','GpuAnimator.asmdef','GpuAnimator.asmdef.meta','Samples~','Tests','Documentation~','Tools~','package.json','package.json.meta','README.md','README.md.meta','LICENSE','LICENSE.meta','CHANGELOG.md','CHANGELOG.md.meta']
    with a.output.open('wb') as target,gzip.GzipFile(fileobj=target,mode='wb',filename='',mtime=0) as compressed,tarfile.open(fileobj=compressed,mode='w') as tar:
        for name in selected:
            base=r/name
            if not base.exists():continue
            for file in sorted(base.rglob('*')) if base.is_dir() else [base]:
                if not file.is_file() or any(part in ('bin','obj','.git','__pycache__') for part in file.parts):continue
                entry=tar.gettarinfo(str(file),'package/'+file.relative_to(r).as_posix());entry.uid=entry.gid=entry.mtime=0;entry.uname=entry.gname=''
                with file.open('rb') as stream:tar.addfile(entry,stream)
    print(a.output)
print('PASS runtime/editor package boundaries, Built-in shader target, GUIDs and optional Framework sample')
