"""Build a lean portable release from a matching Electron runtime, offline.
Usage: python package-windows.py --runtime PATH --node PATH --asar-module PATH
Keeps graphics/codecs and all licences. English Chromium UI resources only;
Unicode text/filenames and the editor's complete feature set are retained.
"""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, tempfile, zipfile, tarfile, lzma, sys
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--runtime',type=Path,required=True)
p.add_argument('--node',required=True)
p.add_argument('--asar-module',required=True)
p.add_argument('--output-dir',type=Path,help='Optional release destination, including another drive')
p.add_argument('--refresh',action='store_true',help='Refresh this version in place after code fixes')
p.add_argument('--xz',action='store_true',help='Also produce a smaller .tar.xz archive')
a=p.parse_args()
src=Path(__file__).resolve().parents[1]; out=a.output_dir.resolve() if a.output_dir else src.parent
out.mkdir(parents=True,exist_ok=True)
version=json.loads((src/'package.json').read_text(encoding='utf-8-sig'))['version']
release=out/f'HitSound Editor v{version}'
if release.exists() and not a.refresh: raise SystemExit(f'Refusing to overwrite {release}')
release.mkdir(exist_ok=a.refresh)
# Explicit runtime allow-list prevents release screenshots/projects leaking in.
runtime_names=['Hitsound Editor.exe','chrome_100_percent.pak','chrome_200_percent.pak',
 'd3dcompiler_47.dll','dxcompiler.dll','dxil.dll','ffmpeg.dll','icudtl.dat',
 'LICENSE.electron.txt','LICENSES.chromium.html','resources.pak','snapshot_blob.bin',
 'v8_context_snapshot.bin','vk_swiftshader_icd.json','vk_swiftshader.dll','vulkan-1.dll']
for name in runtime_names: shutil.copy2(a.runtime/name,release/name)
subprocess.run([sys.executable,str(src/"scripts/embed-windows-icon.py"),str(release/"Hitsound Editor.exe"),str(src/"src/app.ico")],check=True)
(release/'locales').mkdir(exist_ok=a.refresh)
for name in ['en-US.pak','en-GB.pak']: shutil.copy2(a.runtime/'locales'/name,release/'locales'/name)
resources=release/'resources'; resources.mkdir(exist_ok=a.refresh)
# Native audio stays external so Windows can execute it.
native=resources/'native-audio';native.mkdir(exist_ok=a.refresh)
for file in (src/'native-audio').iterdir():
 if file.is_file() and (file.suffix.lower() in ['.exe','.dll'] or 'licen' in file.name.lower() or file.name in ['README.md','dependencies.json']):shutil.copy2(file,native/file.name)
with tempfile.TemporaryDirectory(prefix='hse-package-') as temp:
 stage=Path(temp)
 for name in ['main.cjs','preload.cjs','package.json','index.html']:shutil.copy2(src/name,stage/name)
 for name in ['src','samples']:shutil.copytree(src/name,stage/name)
 subprocess.run([a.node,str(src/'scripts/pack-runtime.cjs'),str(src),str(stage),a.asar_module,str(resources/'app.asar')],check=True)
shutil.copy2(src/'README.md',release/'README.md')
baseline_zip=out/f'Hitsound Editor v{a.runtime.name.rsplit(chr(118),1)[-1]} - Windows.zip'
report={'version':version,'baseline':{'installedBytes':sum(f.stat().st_size for f in a.runtime.rglob('*') if f.is_file() and f.relative_to(a.runtime).parts[:2]!=('resources','app')),'zipBytes':baseline_zip.stat().st_size if baseline_zip.exists() else None},'installedBytes':sum(f.stat().st_size for f in release.rglob('*') if f.is_file()),'archives':[]}
def record(file,kind):
 result={'file':file.name,'format':kind,'bytes':file.stat().st_size,'sha256':hashlib.file_digest(file.open('rb'),'sha256').hexdigest()};report['archives'].append(result);print(json.dumps(result),flush=True)
zip_path=out/f'Hitsound Editor v{version} - Windows.zip'
staged_zip=zip_path.with_suffix('.zip.tmp')
with zipfile.ZipFile(staged_zip,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
 for file in sorted(release.rglob('*')):
  if file.is_file():archive.write(file,Path('HitSound Editor')/file.relative_to(release))
with zipfile.ZipFile(staged_zip) as archive:
 assert archive.testzip() is None
 assert not any('/resources/app/' in n or '/.pnpm/' in n for n in archive.namelist())
staged_zip.replace(zip_path)
record(zip_path,'zip');report['zipCrcVerified']=True
if a.xz:
 xz_path=out/f'Hitsound Editor v{version} - Windows.tar.xz'
 with lzma.open(xz_path,'xb',preset=6) as stream:
  with tarfile.open(fileobj=stream,mode='w|') as archive:archive.add(release,arcname='HitSound Editor')
 with tarfile.open(xz_path,'r:xz') as archive:
  for member in archive:
   if member.isfile():
    with archive.extractfile(member) as f:
     while f.read(1024*1024):pass
 record(xz_path,'tar.xz');report['xzReadVerified']=True
(src/f'package-validation-v{version}.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report),flush=True)
