# Run only inside Blender via MCP. This creates a separate source library.
# Connection map: original roots/trunk/branch joints remain byte-for-byte mesh
# geometry. New crowns span measured original foliage bounds, enclosing branch
# tips. Reference leaf fans overlap their continuous crown; no loose sprites.
import bpy
import json
from pathlib import Path
from mathutils import Vector

BASE = Path('D:/projects/project-g')
DEST = BASE / 'Content.Client/Assets/World/Linvel/Beech/Revision2/VisualStyle'
SOURCE = BASE / 'art-source/linvel/beech/style-rollout'
for p in [DEST / 'Models', SOURCE]:
    p.mkdir(parents=True, exist_ok=True)
assert bpy.data.scenes.get('BeechValleyVisualStyle') is None, 'Preserve existing style edits'
scene = bpy.data.scenes.new('BeechValleyVisualStyle')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
report = []

def bounds(objects):
    pts = [o.matrix_world @ v.co for o in objects for v in o.data.vertices]
    return Vector([min(p[a] for p in pts) for a in range(3)]), Vector([max(p[a] for p in pts) for a in range(3)])

for variant in range(7):
    label = 'beech_canopy_' + str(variant)
    original = bpy.data.collections[label]
    leaves = [o for o in original.all_objects if o.type == 'MESH' and o.data.color_attributes.active_color.data[0].color[3] > .95]
    lo, hi = bounds(leaves)
    collection = bpy.data.collections.new(label + '_Styled')
    scene.collection.children.link(collection)
    bark_parts = []
    for source in original.all_objects:
        if source in leaves or source.type != 'MESH':
            continue
        obj = source.copy()
        obj.data = source.data.copy()
        collection.objects.link(obj)
        bark_parts.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bark_parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = bark_parts[0]
    bpy.ops.object.join()
    bark = bpy.context.object
    bark.name = label + '_Bark'
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ref = bpy.data.scenes['EliaraForestReferenceAssets'].objects['beech_reference_' + ('a' if variant % 2 == 0 else 'b') + '_Leaves']
    rlo, rhi = bounds([ref])
    crown = ref.copy()
    crown.data = ref.data.copy()
    collection.objects.link(crown)
    crown.name = label + '_Leaves'
    # Scale mesh coordinates, retaining the original ground pivot and branches.
    centre, half = (lo + hi) * .5, (hi - lo) * .5
    for v in crown.data.vertices:
        p = ref.matrix_world @ v.co
        q = Vector([2 * (p[a] - rlo[a]) / (rhi[a] - rlo[a]) - 1 for a in range(3)])
        q.x += (variant - 3) * .018 * (1 - q.z * q.z)
        q.y += (1 if variant % 3 == 0 else -1) * .055 * q.x * (1 - q.z * q.z)
        v.co = centre + Vector((q.x * half.x, q.y * half.y, q.z * half.z))
    crown.matrix_world.identity()
    crown.data.update()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [bark, crown]:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = crown
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    invalid = bark.data.validate(verbose=False) or crown.data.validate(verbose=False)
    assert not invalid, label + ': invalid mesh'
    clo, chi = bounds([crown])
    blo, bhi = bounds([bark])
    overlap = min(chi.z, bhi.z) - max(clo.z, blo.z)
    assert overlap > .005, label + ': detached crown'
    for obj in [bark, crown]:
        assert all(abs(x - 1) < 1e-6 for x in obj.scale)
        assert all(abs(x) < 1e-6 for x in obj.rotation_euler)
        for poly in obj.data.polygons:
            poly.use_smooth = True
    bpy.ops.export_scene.gltf(filepath=str(DEST / 'Models' / (label + '.glb')), export_format='GLB', use_selection=True, use_active_scene=True, export_yup=True, export_apply=True, export_attributes=True, export_vertex_color='ACTIVE', export_materials='EXPORT')
    report.append({'id': label, 'crown_min': list(clo), 'crown_max': list(chi), 'crown_branch_overlap': overlap, 'bark_triangles': sum(len(p.vertices)-2 for p in bark.data.polygons), 'leaf_triangles': sum(len(p.vertices)-2 for p in crown.data.polygons), 'pivot': 'original ground-centred; Godot Y up'})
bpy.data.libraries.write(str(SOURCE / 'beech_style_assets.blend'), {scene}, fake_user=True, compress=True)
(SOURCE / 'geometry-audit.json').write_text(json.dumps({'assets': report, 'license': 'Original project geometry, created and adapted through Blender MCP', 'preserved': 'Original trunk, roots and branches; no physics data in GLBs'}, indent=2), encoding='utf-8')
result = {'success': True, 'variants': len(report), 'audit': str(SOURCE / 'geometry-audit.json'), 'sources': str(SOURCE / 'beech_style_assets.blend')}
