"""Repair only confirmed 0xCD GPU placeholders; back up every changed file."""
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/unity-asset-tools'))
from PIL import Image

apply = '--apply' in sys.argv
content = Path(os.environ['LOCALAPPDATA']) / 'T2T Campaign Studio'
custom = Path.home() / 'AppData/LocalLow/Excellent Rectangle/Tape to Tape/CustomLogos'
assets = ROOT / 'assets/team-logos'
catalog = json.loads((assets / 'index.json').read_text())['teams']
backup = ROOT / 'BACKUPS/logo-placeholder-repair'
changes = []
def blank(raw):
    try:
        with Image.open(io.BytesIO(raw)) as image:
            return image.convert('RGBA').getextrema() == ((205, 205),) * 4
    except Exception:
        return False
def write(path, raw):
    if path.exists() and path.read_bytes() == raw:
        return
    changes.append(str(path))
    if not apply:
        return
    if path.exists():
        saved = backup / hashlib.sha256(str(path).encode()).hexdigest()
        saved.parent.mkdir(parents=True, exist_ok=True)
        if not saved.exists():
            shutil.copy2(path, saved)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(raw)

for name, info in catalog.items():
    raw = (assets / info['file']).read_bytes()
    if hashlib.sha256(raw).hexdigest() != info['sha256']:
        raise RuntimeError('Artwork checksum mismatch: ' + name)
    write(content / '_team_logos' / info['file'], raw)
    old = custom / (name + '.png')
    if old.exists() and blank(old.read_bytes()):
        write(old, raw)

repaired_exports = 0
for folder in ('play_now_overrides', 'library', 'campaigns', '.studio-drafts', '.studio-staging'):
    for file in (content / folder).rglob('*.txt'):
        text = file.read_text(encoding='utf-8-sig')
        fields = dict(re.findall(r'^([^=\r\n]+?)\s*=\s*([^\r\n]*)', text, re.M))
        fields = {k.strip(): v.strip() for k, v in fields.items()}
        name = fields.get('Logo From', '')
        if name not in catalog or not fields.get('Export Logo PNG'):
            continue
        try:
            raw = base64.b64decode(fields['Export Logo PNG'], validate=True)
        except ValueError:
            continue
        if not blank(raw):
            continue
        replacement = (assets / catalog[name]['file']).read_bytes()
        digest = hashlib.sha256(replacement).hexdigest()
        values = {'Export Logo PNG': base64.b64encode(replacement).decode(), 'Export Logo SHA256': digest,
                  'Export Logo ID': 'custom_t2t_' + digest + '.png', 'Export Logo Mode': 'embedded'}
        for key, value in values.items():
            pattern = r'^(' + re.escape(key) + r'\s*=\s*)[^\r\n]*'
            text, count = re.subn(pattern, lambda m: m[1] + value, text, flags=re.M)
            if count != 1:
                raise RuntimeError('Missing/duplicate logo field: ' + str(file) + ': ' + key)
        write(file, text.encode('utf-8'))
        repaired_exports += 1
report = dict(applied=apply, repaired_documents=repaired_exports, files=changes)
destination = ROOT / 'artifacts/logo-repair-checks'
destination.mkdir(parents=True, exist_ok=True)
(destination / ('repair-applied.json' if apply else 'repair-plan.json')).write_text(json.dumps(report, indent=2))
print(f'{"Repaired" if apply else "Would repair"} {repaired_exports} documents; {len(changes)} files written; backups: {backup}')
