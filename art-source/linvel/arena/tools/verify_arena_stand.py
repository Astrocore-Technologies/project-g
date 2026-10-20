"""Read the exported binary; reuse the existing library's read-only validator."""
from pathlib import Path
import json

root = Path(__file__).resolve().parents[1]
validator = root.parent / 'tools/verify_glb.py'
scope = {'__file__':str(validator)}
exec(compile(validator.read_text(encoding='utf-8').partition('\nfiles = ')[0],str(validator),'exec'),scope)
report = scope['inspect'](root / 'exports/arena_covered_stand.glb')
assert report['triangles'] == 2872 and report['meshes'] == 5
(root / 'reports/glb-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
receipt_path = root / 'reports/arena_covered_stand.json'
receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
receipt['glb_bytes'] = report['bytes']
receipt_path.write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report))
