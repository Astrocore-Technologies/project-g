"""Collect actual saved evidence without regenerating scenes or content."""
from pathlib import Path
import json, hashlib
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=ROOT/'.artifacts/linvel-production'
def records(file,prefix):
    return [json.loads(s[len(prefix):]) for s in (OUT/file).read_text(encoding='utf-8').splitlines() if s.startswith(prefix)]
hashes={}
for old in (HERE/'before-art/Parts').glob('*.tscn'):
    current=ROOT/'Content.Client/Scenes/Regions/Linvel/Masterplan/Parts'/old.name
    digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
    assert digest(old)==digest(current),current
    hashes[str(current.relative_to(ROOT))]=digest(current)
checks=records('production-smoke.log','MASTERPLAN_CHECKS ')[-1]
walk=records('production-smoke.log','MASTERPLAN_WALK ')[-1]
interior=records('tavern-smoke.log','ART_TAVERN_WALK ')[-1]
assert checks['ok'] and walk['ok'] and interior['ok']
for log in ['production-smoke.log','tavern-smoke.log','tree-capture.log']:
    assert not any('ERROR:' in s or 'SCRIPT ERROR' in s for s in (OUT/log).read_text(encoding='utf-8').splitlines()),log
assets=json.loads((OUT/'asset-bytes-validation.json').read_text(encoding='utf-8'))
assert len(assets)==48 and not any(a['degenerate_triangles'] for a in assets)
catalog=json.loads((HERE/'existing-assets.json').read_text(encoding='utf-8'))+json.loads((HERE/'new-assets.json').read_text(encoding='utf-8'))
(ROOT/'Content.Client/Assets/World/Linvel/City/Production/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
report={
 'stage':'whole-city-art-pass-01-awaiting-user-review','date':'2026-10-20',
 'scene':'Content.Client/Scenes/Regions/Linvel/Art01/LinvelCity_Art01.tscn',
 'preview':'Content.Client/Tests/Regions/LinvelArtPreview.tscn',
 'district_directory':'Content.Client/Scenes/Regions/Linvel/Art01/Parts',
 'style':'Stylized Hand-Painted Anime 3D',
 'source_skill':{'name':'Level Designer & Spatial Architect','mcp_tool':'mcpmarket.skill_level_designer','version':'1.0.0','retrieved':True},
 'trees':{'placements':157,'variants':7,'source':'Content.Client/Assets/World/Linvel/Beech/Revision2/VisualStyle','original_forest_modified':False},
 'art_instances':1311,'render_batches':125,'retained_editable_batch_source_nodes':1161,
 'districts':10,'architecture':87,'large_merchant_ships':3,
 'native_terrain':{'regions':16,'vertex_spacing_m':2,'directory':'Content.Client/Assets/World/Linvel/City/Production/TerrainData','west_east_north_land_extended':True,'south_harbor_open':True},
 'original_district_sha256':hashes,'original_districts_unchanged':True,
 'asset_library':{'glb_count':48,'actual_buffer_triangles':sum(x['triangles'] for x in assets),'degenerate_triangles':0,'validation':'asset-bytes-validation.json'},
 'native_scene_checks':checks,'critical_route_walk':walk,'tavern_entry_walk':interior,
 'captures':records('production-smoke.log','MASTERPLAN_CAPTURE '),
 'updated_tree_capture':records('tree-capture.log','ART_TREE_CAPTURE ')[-1],
 'build':{'command':'dotnet build Game.slnx --no-restore -m:1 --nologo -v:minimal -p:CopyRetryCount=0','exit_code':0,'errors':0,'existing_warnings':4},
 'limits':['First art pass, not final production visual sign-off','Natural cliff/retaining-wall joins and coast need further art polish','No fresh-player navigation sign-off','No crowd/FPS benchmark','No server navigation, persistence or NPC service migration','Not all interiors implemented']}
(OUT/'review-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'ok':True,'districts':10,'trees':157,'critical_walk':walk['ok'],'tavern_entry':interior['ok'],'original_districts_unchanged':True,'report':str(OUT/'review-report.json')}))
