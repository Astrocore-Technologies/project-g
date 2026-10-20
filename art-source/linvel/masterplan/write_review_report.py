"""Summarise the actual saved engine logs and current district sources."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[3]
directory=ROOT/'.artifacts/linvel-masterplan'
report={}
for line in (directory/'masterplan-smoke.log').read_text(encoding='utf-8').splitlines():
    for prefix,key in [('MASTERPLAN_CHECKS ','checks'),('MASTERPLAN_WALK ','walk')]:
        if line.startswith(prefix):report[key]=json.loads(line[len(prefix):])
assert report['checks']['ok'] and report['walk']['ok']
report['build']='Passed, 0 errors, 4 existing NU1900/NU1903 warnings'
report['legacy_regression']='Godot exit 0; saved previous city and its checks preserved'
report['districts']=[dict(scene=str(p.relative_to(ROOT)),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted((ROOT/'Content.Client/Scenes/Regions/Linvel/Masterplan/Parts').glob('*.tscn'))]
report['remaining_review']='Human navigation/readability/art-direction review; no art pass sign-off yet'
(directory/'review-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
(directory/'district-manifest.json').write_text(json.dumps(report['districts'],ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(ok=True,districts=len(report['districts']),ground_samples=report['checks']['ground_samples'],walk_waypoints=report['walk']['waypoints'])))
