"""Run only inside Blender MCP. Preserve original parts and scene placements.

Connection map: cloned trunk rings/roots/branch forks retain the same continuous
world-space warp, so every existing shared joint and crown overlap is preserved.
UV/material classification changes no geometry. New variants are separate assets.
Export meshes combine measured data in a temporary mesh; source parts remain editable.
"""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Matrix
MODE = globals().get('POLISH_MODE','all')

ROOT = Path('D:/projects/project-g')
SOURCE = ROOT/'art-source/linvel/beech/revision2'
OUT = ROOT/'Content.Client/Assets/World/Linvel/Beech/Revision2/Models'
catalog = json.loads((SOURCE/'catalog.json').read_text(encoding='utf-8'))
if isinstance(catalog, dict):
    catalog = catalog['assets']
scene = bpy.data.scenes['BeechRevision2_AssetLibrary']
if bpy.context.window:
    bpy.context.window.scene = scene
verified = 0

def bounds(obj):
    global verified
    # These source parts have applied modifiers and no parent constraints.
    # Matrices/vertex coordinates are explicit; UV work needs no graph rebuild.
    points = [obj.matrix_world @ v.co for v in obj.data.vertices]
    verified += 1
    result = [[min(p[i] for p in points),max(p[i] for p in points)] for i in range(3)]
    if not all(math.isfinite(v) for axis in result for v in axis):
        raise ValueError('Non-finite bounds: '+obj.name)
    return result

def family(obj):
    name = obj.name.lower()
    if any(s in name for s in ('leaf','foliage','crown','frond','fern','herb')): return 1.0
    if any(s in name for s in ('trunk','root','branch','bark','twig','stem')) and 'bank' not in name: return .85
    if any(s in name for s in ('soil','vent','charred','coal','ash')): return .1
    if any(s in name for s in ('sack','rope','tie','twine')): return .4
    if any(s in name for s in ('cover','cloth','canvas','supply','bundle','patch')): return .4
    if any(s in name for s in ('iron','nail','hoop','metal','pin','bolt')): return .25
    if any(s in name for s in ('coal','ash','charred')): return .1
    if any(s in name for s in ('rock','stone','boulder','moss','bank','hearth')): return .55
    return .7

def paint_uv(obj):
    """Metre-scaled UVs and an opaque paint-family code in vertex colour alpha.
    The game shader never uses this alpha as transparency; green canvas is
    therefore distinguishable from living leaves without extra draw calls.
    """
    mesh = obj.data
    code = family(obj)
    uv = mesh.uv_layers.get('UVMap') or mesh.uv_layers.new(name='UVMap')
    mesh.uv_layers.active = uv
    uv_coordinates = [0.0]*(len(mesh.loops)*2)
    b = bounds(obj)
    extent = [axis[1]-axis[0] for axis in b]
    long_axis = max(range(3),key=lambda i:extent[i])
    # Tubes have two explicit vertex rings. Build cylindrical coordinates
    # from their measured centres, including angled branches and roots.
    tube = code == .85 and len(mesh.vertices)%2 == 0 and len(mesh.vertices)<=32
    if tube:
        half = len(mesh.vertices)//2
        start = sum((v.co for v in mesh.vertices[:half]),Vector())/half
        end = sum((v.co for v in mesh.vertices[half:]),Vector())/half
        axis = (end-start).normalized()
        helper = Vector((0,0,1)) if abs(axis.z)<.98 else Vector((1,0,0))
        right = axis.cross(helper).normalized()
        up = right.cross(axis).normalized()
    for poly in mesh.polygons:
        if not tube:
            normal_axis = max(range(3),key=lambda i:abs(poly.normal[i]))
            axes = [i for i in range(3) if i!=normal_axis]
            v_axis = long_axis if long_axis in axes else axes[1]
            u_axis = next(i for i in axes if i!=v_axis)
        for loop_index in poly.loop_indices:
            p = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            if tube:
                offset = p-start
                u = math.atan2(offset.dot(up),offset.dot(right))/math.tau*2
                v = offset.dot(axis)/1.8
            else:
                u,v = p[u_axis]/1.8,p[v_axis]/1.8
            uv_coordinates[loop_index*2] = u
            uv_coordinates[loop_index*2+1] = v
        if tube:
            us = [uv_coordinates[index*2] for index in poly.loop_indices]
            if max(us)-min(us)>1:
                for index in poly.loop_indices:
                    if uv_coordinates[index*2]<0:
                        uv_coordinates[index*2] += 2
    # Bulk attribute writes avoid a dependency/attribute update per UV loop.
    uv.uv.foreach_set('vector',uv_coordinates)
    colours = mesh.color_attributes.get('Paint')
    if not colours:
        raise ValueError('Missing authored vertex paint: '+obj.name)
    rgba = [0.0]*(len(colours.data)*4)
    colours.data.foreach_get('color',rgba)
    for i in range(3,len(rgba),4):rgba[i] = code
    colours.data.foreach_set('color',rgba)
    obj['paint_family'] = code

variants = [
    ('beech_canopy_3',1.22,1.05,.90,.06),
    ('beech_canopy_4',.66,.76,.66,-.08),
    ('beech_canopy_5',1.00,.86,.99,.90),
    ('beech_canopy_6',1.10,1.07,1.23,-.18),
]
new = []
base_parts = tuple(obj for obj in bpy.data.collections['beech_canopy_0'].objects if obj is not None)
for name,sx,sy,sz,bend in variants:
    if MODE not in ('all','variants'):break
    if name in bpy.data.collections:
        raise ValueError('Variant already exists; preserve manual source edits: '+name)
    collection = bpy.data.collections.new(name)
    scene.collection.children.link(collection)
    # Freeze references before creating IDs: Blender's live all_objects cache
    # is invalidated by linking copies, so mutating during iteration is unsafe.
    for old in base_parts:
        if old.type!='MESH':continue
        obj = old.copy();obj.data = old.data.copy();collection.objects.link(obj)
        obj.name = name+' '+old.name
        world = old.matrix_world.copy()
        for vertex in obj.data.vertices:
            p = world@vertex.co
            curve = max(0.0,(p.z-4)/10)**2
            vertex.co = (p.x*sx+bend*curve,p.y*sy,p.z*sz)
        obj.matrix_world = Matrix.Identity(4)
        obj.hide_set(True);obj.hide_render = True
        bounds(obj)
    new.append(name)
    # Lower trunk only; visual crowns/roots do not become broad invisible walls.
    catalog.append({'id':name,'collision_boxes':[{'center':[0,3.8*sz,0],
        'size':[1.05*sx,7.6*sz,1.05*sy],'part':'trunk'}],
        'license':'project-owned original geometry','origin':'ground-centred; glTF Y up'})

bpy.context.view_layer.update()
for item in catalog:
    if MODE in ('variants','finish'):continue
    if MODE.startswith('asset:') and item['id']!=MODE[6:]:continue
    name = item['id']
    if name not in bpy.data.collections:continue
    parts = [obj for obj in bpy.data.collections[name].all_objects if obj.type=='MESH']
    for obj in parts:paint_uv(obj)
    # Combine measured source vertices/attributes through the data API. This
    # avoids operator selection/active-view-layer requirements in headless MCP.
    positions,faces,paint_values,uv_values,smooth = [],[],[],[],[]
    for obj in parts:
        source = obj.data;offset = len(positions)
        positions.extend(tuple(obj.matrix_world@vertex.co) for vertex in source.vertices)
        faces.extend(tuple(offset+index for index in poly.vertices) for poly in source.polygons)
        smooth.extend(poly.use_smooth for poly in source.polygons)
        colours = [0.0]*(len(source.loops)*4)
        source.color_attributes['Paint'].data.foreach_get('color',colours)
        paint_values.extend(colours)
        uvs = [0.0]*(len(source.loops)*2)
        source.uv_layers['UVMap'].uv.foreach_get('vector',uvs)
        uv_values.extend(uvs)
    merged = bpy.data.meshes.new(name+'_painted_export')
    merged.from_pydata(positions,[],faces);merged.update()
    merged.materials.append(parts[0].data.materials[0])
    merged.polygons.foreach_set('use_smooth',smooth)
    colours = merged.color_attributes.new(name='Paint',type='FLOAT_COLOR',domain='CORNER')
    colours.data.foreach_set('color',paint_values)
    merged.uv_layers.new(name='UVMap').uv.foreach_set('vector',uv_values)
    export = bpy.data.objects.new(name+'_painted_export',merged)
    scene.collection.objects.link(export)
    bpy.context.view_layer.update()
    item['bounds'] = bounds(export)
    item['vertices'] = len(export.data.vertices)
    item['triangles'] = sum(len(poly.vertices)-2 for poly in export.data.polygons)
    item['paint'] = 'metre-scaled UV, typed opaque vertex paint, shared painted atlas shader'
    with bpy.context.temp_override(scene=scene,view_layer=scene.view_layers[0]):
        bpy.ops.object.select_all(action='DESELECT')
        export.select_set(True);bpy.context.view_layer.objects.active = export
        bpy.ops.export_scene.gltf(filepath=str(OUT/(name+'.glb')),export_format='GLB',
            use_selection=True,use_active_scene=True,export_apply=True,export_texcoords=True,
            export_vertex_color='ACTIVE',export_vertex_color_name='Paint',
            export_active_vertex_color_when_no_material=True)
    bpy.data.objects.remove(export,do_unlink=True)

(SOURCE/'catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_revision2_assets.blend'))
result = {'assets':len(catalog),'new_silhouettes':new,'verified_parts':verified,
    'paint_family_codes':{'leaf':1,'bark':.85,'wood':.7,'stone':.55,'cloth':.4,'metal':.25,'coal':.1}}
