"""MCP Blender edits to copies only. Existing source scenes remain untouched.
Connection map: curtain upper body/coping/merlons rise together by 4.2m;
their existing overlaps remain unchanged, wall foot -2.6m remains fixed.
Tower door leaf moves outward .5m beyond the 1.65m base; frame front follows,
frame rear stays attached to the shaft. Shared semantic part vertices are
identified before simplification. No primitive creation or cylinder rotation.
"""
import bpy,json,math
from pathlib import Path
ROOT=Path('D:/projects/project-g/art-source/linvel/surface-repair')
OUT=Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/City/Production/Models')
assert not bpy.data.scenes.get('Linvel_SurfaceAssets'), 'Use the saved source for edits'
scene=bpy.data.scenes.new('Linvel_SurfaceAssets');bpy.context.window.scene=scene
source=bpy.data.objects['low_city_wall_8m']
wall=source.copy();wall.data=source.data.copy();wall.name='city_wall_10m'
scene.collection.objects.link(wall)
for v in wall.data.vertices:
    if v.co.z>3.8:v.co.z+=4.2
wall.data.update()
def export(name,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.gltf(filepath=str(OUT/(name+'.glb')),use_selection=True,use_active_scene=True,export_format='GLB')
export('city_wall_10m',[wall])
with bpy.data.libraries.load('D:/projects/project-g/art-source/linvel/linvel_asset_library.blend',link=False) as (available,loaded):
    loaded.collections=['LV_harbor_watchtower']
col=loaded.collections[0];assert col
scene.collection.children.link(col)
info=json.loads(Path('D:/projects/project-g/art-source/linvel/reports/harbor_watchtower.json').read_text(encoding='utf-8'))
names=info['part_ids'];changed=0
for obj in col.all_objects:
    if obj.type!='MESH':continue
    obj.data=obj.data.copy()
    attr=obj.data.attributes.get('LinvelPartID');assert attr
    vertex_parts={}
    for face in obj.data.polygons:
        name=names[attr.data[face.index].value]
        if name.startswith('TowerDoor'):
            for index in face.vertices:vertex_parts[index]=name
    for index,name in vertex_parts.items():
        v=obj.data.vertices[index]
        world_y=v.co.y+obj.location.y
        frame=any(s in name for s in ['Jamb','Voussoir','Keystone','Foot','Sill'])
        if not frame or world_y< -1.35:
            v.co.y-=.5;changed+=1
    obj.data.update()
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('PreservePaintedForms','DECIMATE');mod.decimate_type='DISSOLVE';mod.angle_limit=.02
    mod.delimit={'UV','MATERIAL','SHARP'}
    bpy.ops.object.modifier_apply(modifier=mod.name)
export('watchtower_door_fixed',list(col.all_objects))
assert changed>50
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'linvel_structural_repairs.blend'))
report={'wall_above_ground_m':9.8,'wall_buried_foot_m':-2.6,'door_vertices_moved':changed,'door_offset_m':.5,'source_library_unchanged':True}
(ROOT/'asset-repair-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
result=report
