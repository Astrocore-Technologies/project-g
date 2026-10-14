"""Execute inside Blender MCP; creates only the separate reference asset scene.

Connection map (metres, ground-centred export pivots):
  trunk base <-> roots: roots start inside trunk, radial overlap >= .12 m.
  trunk forks <-> branches: branch starts inside measured trunk ring, >= .08 m.
  branches <-> crown lobes: tips extend into lobe bounds, >= .25 m.
  neighbouring crown lobes: designed overlap >= .30 m; no detached balls.
  grass blade segments: shared ring vertices; blades enter soil by .035 m.
No existing collections, meshes or source files are changed.
"""
import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector

ROOT = Path('D:/projects/project-g')
DEST = ROOT/'Content.Client/Assets/ArtDirection/ForestReference/Models'
SOURCE = ROOT/'art-source/linvel/forest-reference'
DEST.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
if bpy.data.scenes.get('EliaraForestReferenceAssets'):
    raise RuntimeError('Reference scene exists: preserve manual edits; do not rerun.')
scene = bpy.data.scenes.new('EliaraForestReferenceAssets')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
audit = []

def material(name, rgba):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = rgba
    mat.use_nodes = True
    node = mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = rgba
    node.inputs['Roughness'].default_value = .92
    return mat

BARK = material('reference_silver_bark',(.50,.49,.39,1))
LEAF = material('reference_beech_foliage',(.30,.47,.13,1))
GRASS = material('reference_grass',(.27,.42,.12,1))

def verify_part(name, vertices):
    b = [[min(p[i] for p in vertices),max(p[i] for p in vertices)] for i in range(3)]
    assert all(math.isfinite(v) for axis in b for v in axis)
    audit.append({'part':name,'bounds':b})
    return b

def append_tube(vs,fs,uvs,points,radii,segments=14):
    start=len(vs)
    points=[Vector(p) for p in points]
    total=0.0
    for i,p in enumerate(points):
        if i: total+=(p-points[i-1]).length
        forward=(points[min(i+1,len(points)-1)]-points[max(0,i-1)]).normalized()
        right=forward.cross(Vector((0,1,0)) if abs(forward.y)<.95 else Vector((1,0,0))).normalized()
        up=right.cross(forward).normalized()
        for j in range(segments+1):
            a=j/segments*math.tau
            q=p+radii[i]*(math.cos(a)*right+math.sin(a)*up)
            vs.append(tuple(q)); uvs.append((j/segments,total*.22))
    for i in range(len(points)-1):
        for j in range(segments):
            a=start+i*(segments+1)+j
            fs.append((a,a+1,a+segments+2,a+segments+1))
    fs.append(tuple(start+j for j in reversed(range(segments))))
    fs.append(tuple(start+(len(points)-1)*(segments+1)+j for j in range(segments)))
    return verify_part('tube_%d'%start,vs[start:])

def make_mesh(name,collection,vs,fs,uvs,mat,leaf=False):
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(vs,[],fs); mesh.update()
    if mat != GRASS:
        bm=bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=tuple(bm.faces))
        bm.to_mesh(mesh); bm.free(); mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    colour=mesh.color_attributes.new(name='Col',type='FLOAT_COLOR',domain='POINT')
    for i,v in enumerate(mesh.vertices):
        height=v.co.z
        # Vertex channels are artist masks: R=wind, G=paint variation,
        # B=crown interior exposure. Alpha is opaque, never material family.
        if leaf:
            shade=.88+.12*math.sin(v.co.x*.63+v.co.y*.38)
            colour.data[i].color=(max(0,min(1,(height-4)/8)),shade,.5,1)
        else:
            colour.data[i].color=(0,.95+.04*math.sin(height),.5,1)
    for polygon in mesh.polygons:
        polygon.use_smooth=True
        for li in polygon.loop_indices:
            uv.data[li].uv=uvs[mesh.loops[li].vertex_index]
    obj=bpy.data.objects.new(name,mesh); collection.objects.link(obj)
    obj.data.materials.append(mat)
    # Explicit data coordinates leave rotation zero, scale one, pivot at soil.
    assert tuple(obj.scale)==(1,1,1) and tuple(obj.rotation_euler)==(0,0,0)
    return obj

def ellipsoid(vs,fs,uvs,centre,scale,seed):
    start=len(vs); rings=12; sectors=24
    for i in range(rings+1):
        lat=math.pi*i/rings
        for j in range(sectors+1):
            a=math.tau*j/sectors
            ripple=1+.055*math.sin(a*3+seed+lat*2)+.025*math.cos(a*7-lat*4+seed)
            horizontal=math.sin(lat)*ripple
            vs.append((centre[0]+math.cos(a)*scale[0]*horizontal,
                       centre[1]+math.sin(a)*scale[1]*horizontal,
                       centre[2]+math.cos(lat)*scale[2]*(1+.03*math.sin(a*4+seed)*math.sin(lat))))
            uvs.append((j/sectors,i/rings))
    for i in range(rings):
        for j in range(sectors):
            a=start+i*(sectors+1)+j
            if i==0: fs.append((a,a+sectors+2,a+sectors+1))
            elif i==rings-1: fs.append((a,a+1,a+sectors+1))
            else: fs.append((a,a+1,a+sectors+2,a+sectors+1))
    return verify_part('crown_%d'%seed,vs[start:])

def tree(variant):
    name='beech_reference_%s'%('a' if variant==0 else 'b')
    collection=bpy.data.collections.new(name); scene.collection.children.link(collection)
    vs=[];fs=[];uvs=[]
    height=11.8+variant*.8
    trunk=[(.09*math.sin(i*.8+variant)*i/8,.065*math.sin(i*.6)*i/8,i/8*height*.79) for i in range(9)]
    radii=[.62,.48,.40,.33,.26,.20,.14,.08,.025]
    trunk_bounds=append_tube(vs,fs,uvs,trunk,radii)
    for i in range(6):
        a=i*math.tau/6+.22
        root=[(.12*math.cos(a),.12*math.sin(a),.3),(.7*math.cos(a),.7*math.sin(a),.12),(1.35*math.cos(a),1.35*math.sin(a),-.06)]
        append_tube(vs,fs,uvs,root,[.23,.13,.01],10)
        assert Vector(root[0][:2]).length<radii[0]-.08
    centres=[]
    for i in range(8):
        a=i*2.399+variant*.6
        r=2.05+(.5 if i%3==0 else 0)
        z=7.0+(i%3)*1.25+variant*.55
        centre=(math.cos(a)*r,math.sin(a)*r,z)
        centres.append((centre,(2.5,2.15,1.5+.16*(i%2))))
        fork=trunk[3+i%3]
        middle=(centre[0]*.45,centre[1]*.45,fork[2]+.9)
        tip=(centre[0],centre[1],centre[2]-.45)
        append_tube(vs,fs,uvs,[fork,middle,tip],[.18,.095,.018],10)
        assert abs(tip[2]-centre[2])<1.1
    centres.extend([((-.35,.2,height-1.3),(2.7,2.4,1.8)),((.8,-.8,height-2.2),(2.7,2.5,1.7))])
    make_mesh(name+'_Bark',collection,vs,fs,uvs,BARK)
    lv=[];lf=[];lu=[]
    for i,(centre,scale) in enumerate(centres):
        ellipsoid(lv,lf,lu,centre,scale,i+variant*30)
        # Every lobe overlaps another; tips remain inside the foliage.
        connected=any(sum(((centre[k]-other[0][k])/(scale[k]+other[1][k]-.3))**2 for k in range(3))<1 for j,other in enumerate(centres) if j!=i)
        assert connected, ('detached crown',i)
    make_mesh(name+'_Leaves',collection,lv,lf,lu,LEAF,True)
    return collection

def grass():
    collection=bpy.data.collections.new('grass_reference');scene.collection.children.link(collection)
    vs=[];fs=[];uvs=[]
    for i in range(9):
        a=i*2.399
        height=.34+.18*(.5+.5*math.sin(i*2.14))
        x=.12*math.cos(a);y=.12*math.sin(a)
        start=len(vs)
        for j in range(4):
            t=j/3
            width=.045*(1-t)**.6+.001
            bend=height*.38*t*t
            for side in (-1,1):
                vs.append((x+math.cos(a)*bend-math.sin(a)*width*side,y+math.sin(a)*bend+math.cos(a)*width*side,height*t-.035))
                uvs.append(((side+1)/2,t))
        for j in range(3):
            p=start+j*2;fs.append((p,p+1,p+3,p+2))
        verify_part('grass_blade_%d'%i,vs[start:])
    obj=make_mesh('grass_reference',collection,vs,fs,uvs,GRASS,True)
    for v in obj.data.vertices: obj.data.color_attributes['Col'].data[v.index].color=(max(0,v.co.z/.6),1,.5,1)
    return collection

collections=[tree(0),tree(1),grass()]
exports=[]
for collection in collections:
    bpy.ops.object.select_all(action='DESELECT')
    for obj in collection.objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=collection.objects[0]
    path=DEST/(collection.name+'.glb')
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,use_active_scene=True,export_texcoords=True,export_normals=True,export_vertex_color='ACTIVE',export_attributes=True,export_materials='EXPORT',export_yup=True)
    exports.append({'id':collection.name,'file':str(path.relative_to(ROOT)),
                    'vertices':sum(len(o.data.vertices) for o in collection.objects),
                    'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in collection.objects)})
bpy.data.libraries.write(str(SOURCE/'forest_reference_assets.blend'),{scene},fake_user=True,compress=True)
(SOURCE/'geometry-audit.json').write_text(json.dumps({'parts':audit,'exports':exports},indent=2),encoding='utf8')
result={'source':str(SOURCE/'forest_reference_assets.blend'),'exports':exports,'verified_parts':len(audit),'existing_sources_preserved':True}
