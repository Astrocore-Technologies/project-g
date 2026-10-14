# Blender MCP only. Existing root banks/branches and their joints stay intact.
# Crown leaf masses overlap branch ends in the source. Voxel union joins leaf
# masses into continuous organic volumes without moving any trunk or root.
import bpy
import json
from pathlib import Path

BASE = Path('D:/projects/project-g')
scene = bpy.data.scenes['BeechValleyVisualStyle']
bpy.context.window.scene = scene
report = []
for variant in range(2):
    label = 'beech_copse_' + str(variant)
    assert not bpy.data.collections.get(label + '_Styled'), 'Preserve style edits'
    collection = bpy.data.collections.new(label + '_Styled')
    scene.collection.children.link(collection)
    parts = {'Bark': [], 'Leaves': []}
    for source in bpy.data.collections[label].all_objects:
        if source.type != 'MESH':
            continue
        a = source.data.color_attributes.active_color.data[0].color
        leaf = a[3] > .975 and a[1] > a[0] * 1.08 and a[1] > a[2] * 1.15
        obj = source.copy()
        obj.data = source.data.copy()
        collection.objects.link(obj)
        parts['Leaves' if leaf else 'Bark'].append(obj)
    final = []
    for kind, objects in parts.items():
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = objects[0]
        bpy.ops.object.join()
        obj = bpy.context.object
        obj.name = label + '_' + kind
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if kind == 'Leaves':
            remesh = obj.modifiers.new('Continuous beech regrowth', 'REMESH')
            remesh.mode = 'VOXEL'
            remesh.voxel_size = .12
            remesh.use_smooth_shade = True
            bpy.ops.object.modifier_apply(modifier=remesh.name)
            smooth = obj.modifiers.new('Organic crown rounding', 'SMOOTH')
            smooth.factor = .50
            smooth.iterations = 3
            bpy.ops.object.modifier_apply(modifier=smooth.name)
            decimate = obj.modifiers.new('Game mesh density', 'DECIMATE')
            decimate.ratio = .12
            bpy.ops.object.modifier_apply(modifier=decimate.name)
            masks = obj.data.color_attributes.new(name='PaintWindMask', type='FLOAT_COLOR', domain='POINT')
            lo = min(v.co.z for v in obj.data.vertices)
            hi = max(v.co.z for v in obj.data.vertices)
            for v in obj.data.vertices:
                h = (v.co.z - lo) / max(hi - lo, .1)
                masks.data[v.index].color = (h, .65 + .25 * h, .5, 1)
            obj.data.color_attributes.active_color = masks
        assert not obj.data.validate(verbose=False)
        for polygon in obj.data.polygons:
            polygon.use_smooth = True
        final.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in final:
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(filepath=str(BASE / 'Content.Client/Assets/World/Linvel/Beech/Revision2/VisualStyle/Models' / (label + '.glb')), export_format='GLB', use_selection=True, use_active_scene=True, export_yup=True, export_apply=True, export_attributes=True, export_vertex_color='ACTIVE')
    report.append({'id': label, 'triangles': {o.name: sum(len(p.vertices)-2 for p in o.data.polygons) for o in final}, 'physics': 'unchanged original bank'})
dest = BASE / 'art-source/linvel/beech/style-rollout'
bpy.data.libraries.write(str(dest / 'beech_style_assets.blend'), {scene}, fake_user=True, compress=True)
(dest / 'copse-audit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
result = {'success': True, 'copses': report}
