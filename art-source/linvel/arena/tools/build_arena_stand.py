"""Run explicitly through Blender MCP. Never replaces an existing collection/file.
Connection map (metres): plinth.top <-> feet.bottom .04;
feet.top <-> posts.bottom .04; posts.top <-> header.bottom .09;
headers <-> roof underlay .06; rows <-> seat planks .04;
rear posts <-> guard rails .08; rafters <-> ridge .08;
roof shingles <-> underlay .025. Directional members use explicit endpoints.
"""
import bpy, math, json
from pathlib import Path
from mathutils import Vector

BASE = Path('D:/projects/project-g/art-source/linvel')
DEST = BASE / 'arena'
for folder in ('exports', 'reports', 'previews', 'textures'):
    (DEST / folder).mkdir(parents=True, exist_ok=True)
assert not bpy.data.collections.get('LV_arena_covered_stand'), 'Existing editable model: export it instead of rebuilding.'
assert not (DEST / 'arena_covered_stand.blend').exists(), 'Refusing to overwrite source.'
ctx = {}
source = (BASE / 'tools/linvel_builder.py').read_text(encoding='utf-8')
source = source.replace("bpy.data.scenes['Linvel_AssetLibrary']", "bpy.data.scenes['Linvel_TrainingAsset']")
exec(compile(source, 'linvel_builder.py', 'exec'), ctx)
ctx['ROOT'] = DEST
scene = bpy.data.scenes.new('Linvel_TrainingAsset')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
atlas = bpy.data.images.load(str(BASE / 'textures/linvel_painted_atlas.png'), check_existing=True)
atlas.name = 'Linvel_PaintedAtlas'
atlas.pack()
ctx['make_materials']()
b = ctx['Builder']('arena_covered_stand', 'Крытая малая трибуна арены', 'training')
b.box('StonePlinth', (0, 0, .09), (8.0, 4.4, .18), 'stone', bevel=.045)
for x in (-3.6, 3.6):
    for y in (-1.8, 1.8):
        k = str(x) + '_' + str(y)
        foot = b.box('Foot' + k, (x, y, .37), (.58, .58, .46), 'stone', bevel=.045)
        post = b.box('Post' + k, (x, y, 1.955), (.30, .30, 2.79), 'oak', 'Joinery', .035)
        b.joint('StonePlinth', foot, 2, .04)
        b.joint(foot, post, 2, .04)
for row in range(3):
    y = -1.15 + row * 1.05
    h = .26 + row * .30
    for sign in (-1, 1):
        k = str(row) + '_' + str(sign)
        base = b.box('SeatRiser' + k, (sign * 2.05, y, .18 + h / 2), (2.85, 1.1, h), 'darkwood', 'Structure', .025)
        for plank in range(4):
            seat = b.box('SeatPlank' + k + '_' + str(plank), (sign * 2.05, y - .39 + plank * .26, .18 + h), (2.97, .275, .09), 'oak', 'Seating', .015)
            b.joint(base, seat, 2, .04)
    # Real central aisle has six shallow steps, not an impassable solid stair face.
    for sub in range(2):
        sh = .15 * (row * 2 + sub + 1)
        b.box('AisleStep' + str(row) + '_' + str(sub), (0, y - .26 + sub * .525, .18 + sh/2), (1.25, .54, sh), 'stone', 'Structure', .02)
for y in (-1.8, 1.8):
    left, right = b.bounds['Post-3.6_' + str(y)], b.bounds['Post3.6_' + str(y)]
    span = right[0][1] - left[0][0] + .16
    header = b.box('Header' + str(y), (0, y, 3.31), (span, .34, .26), 'oak', 'Joinery', .025)
    for x in (-3.6, 3.6):
        b.joint('Post' + str(x) + '_' + str(y), header, 2, .09)
        for sign in (-1, 1):
            end_x = x + sign * .7
            if abs(end_x) < 3.8:
                b.span('Knee' + str(x) + '_' + str(y) + '_' + str(sign), (x, y, 2.60), (end_x, y, 3.31), .09, .10)
for y in (-1.8, 1.8):
    for z in (1.32, 1.70):
        if y > 0:
            b.box('RearRail' + str(z), (0, y, z), (7.35, .12, .12), 'oak', 'Joinery', .015)
for x in (-3.6, 3.6):
    b.span('SideGuard' + str(x), (x,-1.8,.95), (x,1.8,1.75), .07, .07)
    for sign in (-1,1):
        b.span('Rafter' + str(x) + '_' + str(sign), (x, sign * 2.45, 3.31), (x,0,4.48), .10,.10)
b.box('Ridge', (0,0,4.48),(8.9,.23,.20),'darkwood','Joinery',.025)
# Shared broad painted atlas; modest number of shingle plates, not dense sculpting.
for sign in (-1,1):
    verts = [(-4.5,0,4.50),(4.5,0,4.50),(4.5,sign*2.48,3.27),(-4.5,sign*2.48,3.27)]
    b.add('RoofUnderlay' + str(sign), verts, [(0,1,2,3)], 'tile', 'Roof')
    for row in range(5):
        f0, f1 = row/5, min(1.025,(row+1)/5+.025)
        for col in range(16):
            x0, x1 = -4.5+col*9/16, -4.5+(col+1)*9/16+.012
            points = [(x0,sign*2.48*f0,4.53-1.23*f0),(x1,sign*2.48*f0,4.53-1.23*f0),(x1,sign*2.48*f1,4.53-1.23*f1),(x0,sign*2.48*f1,4.53-1.23*f1)]
            b.add('Shingle'+str(sign)+'_'+str(row)+'_'+str(col),points,[(0,1,2,3)],'tile','Roof')
    b.span('Eave'+str(sign),(-4.52,sign*2.48,3.27),(4.52,sign*2.48,3.27),.10,.10,'darkwood','Joinery')
for panel in range(4):
    x0=-3.5+panel*1.75
    points=[(x0,-1.99,3.22),(x0+1.7,-1.99,3.22),(x0+1.7,-2.01,2.96),(x0+.85,-2.03,2.80),(x0,-2.01,2.96)]
    b.add('ClothSwag'+str(panel),points,[(0,1,2,3,4)],'blue','Cloth')
info = b.finish()
col = bpy.data.collections['LV_arena_covered_stand']
for obj in col.objects:
    if obj.type=='MESH':
        assert tuple(obj.scale)==(1,1,1) and max(abs(v) for v in obj.rotation_euler)<1e-7
        assert obj.data.uv_layers and all(p.area>1e-9 for p in obj.data.polygons)
        assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co)
bpy.ops.object.select_all(action='DESELECT')
for obj in col.objects: obj.select_set(True)
bpy.context.view_layer.objects.active=col.objects['arena_covered_stand']
target = DEST / 'exports/arena_covered_stand.glb'
bpy.ops.export_scene.gltf(filepath=str(target),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_animations=False,export_cameras=False,export_lights=False,export_extras=True)
# Studio is separate from asset collection and never exported.
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1200
scene.render.resolution_y=900
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='Standard'
world=bpy.data.worlds.new('TrainingStudioWorld'); world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.64,.72,.78,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.7
scene.world=world
cam_data=bpy.data.cameras.new('TrainingStudioCamera'); cam=bpy.data.objects.new('TrainingStudioCamera',cam_data); scene.collection.objects.link(cam)
cam.location=(11,-14,9); cam.rotation_euler=(Vector((0,0,1.8))-cam.location).to_track_quat('-Z','Y').to_euler(); cam_data.type='ORTHO'; cam_data.ortho_scale=12.2; scene.camera=cam
light_data=bpy.data.lights.new('TrainingStudioKey','AREA'); light_data.energy=1800; light_data.shape='DISK'; light_data.size=8
light=bpy.data.objects.new('TrainingStudioKey',light_data); scene.collection.objects.link(light); light.location=(-3,-5,10)
scene.render.filepath=str(DEST/'previews/arena_covered_stand.png')
bpy.ops.wm.save_as_mainfile(filepath=str(DEST/'arena_covered_stand.blend'),copy=True)
bpy.ops.render.render(write_still=True)
info['glb_bytes']=target.stat().st_size
info['source']='arena_covered_stand.blend'
info['checks']={'transforms':True,'finite_coordinates':True,'uvs':True,'nondegenerate_faces':True,'checked_joints':len(b.joints)}
(DEST/'reports/arena_covered_stand.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf-8')
result={'asset':info['key'],'triangles':info['triangles'],'dimensions':info['dimensions_m'],'checks':info['checks'],'preview':scene.render.filepath,'blend':str(DEST/'arena_covered_stand.blend')}
