"""Execute through Blender MCP against the existing editable asset.
Connection map: preserve the existing 36 checked joints; multiply all X/Y
endpoints and widths by the same positive factors. All vertical overlaps,
seat/riser heights and ground pivot remain unchanged. No new primitives.
"""
import bpy, json, math
from pathlib import Path
from mathutils import Matrix, Vector

root = Path('D:/projects/project-g/art-source/linvel/arena')
col = bpy.data.collections['LV_arena_covered_stand']
asset = col.objects['arena_covered_stand']
assert asset.get('revision') != 'large-v2', 'Already enlarged; export existing geometry instead.'
scene = bpy.data.scenes['Linvel_TrainingAsset']
bpy.context.window.scene = scene
bpy.context.view_layer.update()
meshes = [o for o in col.objects if o.type == 'MESH']
points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
before = [max(v[i] for v in points)-min(v[i] for v in points) for i in range(3)]
sx, sy = 14.0/before[0], 7.0/before[1]
stretch = Matrix.Diagonal((sx, sy, 1, 1))
for obj in meshes:
    if obj.data.users > 1:
        obj.data = obj.data.copy()
    # Bake world-space dimensions into mesh, not a non-uniform object transform.
    previous = obj.matrix_world.copy()
    new_origin = stretch @ previous.translation
    new_world = previous.copy()
    new_world.translation = new_origin
    obj.data.transform(new_world.inverted() @ stretch @ previous)
    obj.matrix_world = new_world
    obj.data.update()
bpy.context.view_layer.update()
points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
after = [max(v[i] for v in points)-min(v[i] for v in points) for i in range(3)]
assert abs(after[0]-14)<1e-4 and abs(after[1]-7)<1e-4
assert abs(after[2]-before[2])<1e-5
assert all(tuple(o.scale)==(1,1,1) and max(abs(a) for a in o.rotation_euler)<1e-6 for o in meshes)
assert all(math.isfinite(c) for v in points for c in v)
asset['revision']='large-v2'
asset['dimensions_m']=after
asset['approved_reference']='arena-stand-concept.png'
for obj in bpy.data.objects:
    obj.select_set(False)
for obj in col.objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active=asset
bpy.ops.export_scene.gltf(filepath=str(root/'exports/arena_covered_stand.glb'),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_animations=False,export_cameras=False,export_lights=False,export_extras=True)
cam=scene.camera
cam.location=(14,-19,12)
cam.rotation_euler=(Vector((0,0,1.8))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.ortho_scale=18.5
scene.render.filepath=str(root/'previews/arena_covered_stand-large.png')
bpy.ops.wm.save_as_mainfile(filepath=str(root/'arena_covered_stand-large.blend'),copy=True)
bpy.ops.render.render(write_still=True)
receipt_path=root/'reports/arena_covered_stand.json'
receipt=json.loads(receipt_path.read_text(encoding='utf-8'))
receipt['label_ru']='Крытая трибуна арены'
receipt['dimensions_m']=after
receipt['bounds_blender_m']=[(min(v[i] for v in points),max(v[i] for v in points)) for i in range(3)]
receipt['source']='arena_covered_stand-large.blend'
receipt['revision']='large-v2'
receipt['horizontal_factors']=[sx,sy]
receipt['height_preserved']=True
receipt_path.write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
result={'dimensions_blender_m':after,'factors':[sx,sy],'height_preserved':True,'source':receipt['source'],'preview':scene.render.filepath}
