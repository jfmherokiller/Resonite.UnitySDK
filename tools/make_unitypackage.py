"""Build a .unitypackage from the tracked files of one Assets folder (clean working tree required).

Usage: make_unitypackage.py <repo> <Assets/Folder> <out.unitypackage>
Layout matches Unity's exporter: per-GUID directory entry with asset, asset.meta, pathname; gzip member name
"archtemp.tar" (Unity's importer skips or fails packages with any other gzip name).
"""
import gzip, io, re, subprocess, sys, tarfile, time
from pathlib import Path

repo, root, out = Path(sys.argv[1]), sys.argv[2].rstrip('/'), Path(sys.argv[3])
git = lambda *a: subprocess.run(['git', '-C', str(repo), *a], check=True, capture_output=True, text=True).stdout
if git('status', '--porcelain', '--', root).strip():
    sys.exit('working tree not clean under ' + root)
files = [f for f in git('ls-files', '-z', '--', root, root + '.meta').split('\0') if f]
metas = {f[:-5] for f in files if f.endswith('.meta')}
missing = sorted({f for f in files if not f.endswith('.meta')} - metas)
if missing:
    sys.exit(f'no .meta for: {missing[:5]}')
guids, mtime = {}, int(time.time())


def add(tar, name, data=None):
    info = tarfile.TarInfo(name)
    info.mtime, info.mode = mtime, 0o755 if data is None else 0o644
    if data is None:
        info.type = tarfile.DIRTYPE
        tar.addfile(info)
    else:
        info.size = len(data)
        tar.addfile(info, io.BytesIO(data))


buffer = io.BytesIO()
with tarfile.open(fileobj=buffer, mode='w', format=tarfile.GNU_FORMAT) as tar:
    for asset in sorted(metas):
        meta = (repo / (asset + '.meta')).read_bytes()
        guid = re.search(rb'^guid: ([0-9a-f]{32})', meta, re.M).group(1).decode()
        if guid in guids:
            sys.exit(f'duplicate guid {guid}: {asset} / {guids[guid]}')
        guids[guid] = asset
        add(tar, guid)
        path = repo / asset
        if path.is_file():
            data = path.read_bytes()
            if data.startswith(b'version https://git-lfs'):
                sys.exit('LFS pointer, run git lfs pull: ' + asset)
            add(tar, f'{guid}/asset', data)
        add(tar, f'{guid}/asset.meta', meta)
        add(tar, f'{guid}/pathname', asset.encode())

with open(out, 'wb') as file, gzip.GzipFile(filename='archtemp.tar', mode='wb', fileobj=file, mtime=mtime) as archive:
    archive.write(buffer.getvalue())
print(f'{out} assets={len(guids)} bytes={out.stat().st_size}')
