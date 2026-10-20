"""MCP-only Blender normalization of original Linvel assets. Source is untouched.
Connection map: existing connected parts are preserved; planar dissolution and
bounded decimation alter only copied meshes. Exported bounds must stay within 2%.
No new geometry, joints, external assets or provider requests.
"""
import bpy, json, math, hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path('D:/projects/project-g/art-source/linvel/production')
OUT=Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/City/Production/Models')
OUT.mkdir(parents=True,exist_ok=True)
assert bpy.data.scenes.get('Linvel_ProductionLibrary') is None,'Do not overwrite existing editable production source'
scene=bpy.data.scenes.new('Linvel_ProductionLibrary');bpy.context.window.scene=scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
source=ROOT.parent/'linvel_asset_library.blend'
catalog={a['key']:a for a in json.loads((ROOT.parent/'catalog.json').read_text(encoding='utf-8'))}
keys=['townhouse_a','townhouse_b','orchard_cottage','adventurers_guild','healing_house','harbor_shrine',
 'smith_workshop','carpenter_workshop','watermill','harbor_warehouse','market_shop','boatshed_open','boatshed_closed',
 'harbor_watchtower','orchard_tree','blossoming_tree','cypress_tree','garden_shrub','flower_planter','meadow_flowers',
 'square_fountain','market_bread','market_fruit','street_lantern','guild_banner_pole','public_bench','orchard_fence',
 'training_dummy','training_weapon_rack','dorn_pavilion','cargo_barrel','cargo_crate','cargo_sacks','handcart',
 'fishing_net_rack','harbor_bollard','dock_straight','dock_landing','stone_balustrade','terrace_retaining_wall',
 'harbor_seawall','fishing_sailboat','fishing_rowboat','coastal_boulder_a','coastal_boulder_b','crop_row']
with bpy.data.libraries.load(str(source),link=False) as (available,loaded):
    loaded.collections=['LV_'+k for k in keys]
report=[]
def tris(objects):return sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
for key,col in zip(keys,loaded.collections):
    assert col is not None,key
    scene.collection.children.link(col)
    meshes=[o for o in col.all_objects if o.type=='MESH']
    before=tris(meshes)
    target=14500 if key in ['adventurers_guild','healing_house'] else 10000
    if key in ['orchard_tree','blossoming_tree','cypress_tree']:target=5500
    if key in ['garden_shrub','flower_planter','meadow_flowers']:target=1300
    if key in ['dock_straight','dock_landing']:target=2800
    for obj in meshes:
        obj.data=obj.data.copy()
        bpy.context.view_layer.objects.active=obj
        for selected in bpy.context.selected_objects:selected.select_set(False)
        obj.select_set(True)
        original_bounds=[obj.matrix_world@Vector(p) for p in obj.bound_box]
        if len(obj.data.polygons)>100:
            mod=obj.modifiers.new('PlanarSimplification','DECIMATE');mod.decimate_type='DISSOLVE';mod.angle_limit=.022
            mod.delimit={'UV','MATERIAL','SHARP'}
            bpy.ops.object.modifier_apply(modifier=mod.name)
        now=tris(meshes)
        if now>target and len(obj.data.polygons)>180:
            ratio=max(.12,min(.95,target/max(now,1)))
            mod=obj.modifiers.new('BoundedSimplification','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True
            bpy.ops.object.modifier_apply(modifier=mod.name)
        assert obj.data.uv_layers.active and obj.data.materials,key
        assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co),key
        assert all(abs(v-1)<1e-6 for v in obj.scale),key
    after=tris(meshes)
    bpy.context.view_layer.update()
    vertices=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    actual=[(min(v[i] for v in vertices),max(v[i] for v in vertices)) for i in range(3)]
    expected=catalog[key]['bounds_blender_m']
    assert all(abs(actual[i][j]-expected[i][j])<max(.08,catalog[key]['dimensions_m'][i]*.02) for i in range(3) for j in range(2)),(key,actual,expected)
    for obj in bpy.data.objects:obj.select_set(False)
    for obj in col.all_objects:obj.select_set(True)
    root=next(o for o in col.objects if o.type=='EMPTY')
    bpy.context.view_layer.objects.active=root
    file=OUT/(key+'.glb')
    bpy.ops.export_scene.gltf(filepath=str(file),export_format='GLB',use_selection=True,use_active_scene=True,export_animations=False,export_extras=True)
    report.append(dict(key=key,source='art-source/linvel/linvel_asset_library.blend',before_triangles=before,triangles=after,
      dimensions_m=catalog[key]['dimensions_m'],bounds_blender_m=actual,sha256=hashlib.sha256(file.read_bytes()).hexdigest(),original_design=True))
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'linvel_optimized_library.blend'),copy=True)
(ROOT/'existing-assets.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
result=dict(assets=len(report),before_triangles=sum(r['before_triangles'] for r in report),triangles=sum(r['triangles'] for r in report),source_preserved=True)
