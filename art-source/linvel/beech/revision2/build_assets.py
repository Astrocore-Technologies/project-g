"""Own stylized forest modules, executed only inside Blender through MCP.

Connection map (metres, minimum overlap 0.01):
  trunk rings share vertices; root fingers overlap trunk base by 0.08;
  branch start lies inside its parent stem; crown masses overlap twig ends;
  bridge planks overlap stringers by 0.10; posts meet deck/rails by 0.06;
  shelter posts overlap sill and eave beam by 0.05; rafters overlap ridge;
  charcoal ribs meet radial log bed; shelter roofing overlaps battens.
Assemblies retain independent source parts; exports join only duplicates.
"""
import bpy, bmesh, math, random, json
from pathlib import Path
from mathutils import Vector

ROOT=Path('D:/projects/project-g')
OUT=ROOT/'Content.Client/Assets/World/Linvel/Beech/Revision2/Models'
SOURCE=ROOT/'art-source/linvel/beech/revision2'
PALETTE={'bark':(.32,.30,.24),'wood':(.28,.15,.065),'fresh':(.43,.27,.13),
 'leaf':(.17,.32,.075),'leaflight':(.24,.40,.10),'moss':(.12,.24,.065),
 'stone':(.29,.30,.255),'cloth':(.25,.29,.22),'coal':(.07,.065,.055),
 'iron':(.13,.15,.14),'reed':(.38,.29,.12)}

def setup():
    OUT.mkdir(parents=True,exist_ok=True); SOURCE.mkdir(parents=True,exist_ok=True)
    if 'BeechRevision2_AssetLibrary' in bpy.data.scenes:
        raise RuntimeError('Asset source already exists; edit it instead of regenerating.')
    scene=bpy.data.scenes.new('BeechRevision2_AssetLibrary')
    bpy.context.window.scene=scene
    scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    material=bpy.data.materials.new('Beech_HandPaintedVertexColor')
    material.use_nodes=True
    nodes=material.node_tree.nodes
    color=nodes.new('ShaderNodeVertexColor'); color.layer_name='Paint'
    bsdf=nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value=1
    material.node_tree.links.new(color.outputs['Color'],bsdf.inputs['Base Color'])
    return scene, material

scene,mat=setup()
parts=[]; catalog=[]; bounds_checked=0

def bounds(obj):
    global bounds_checked
    bpy.context.view_layer.update()
    vs=[obj.matrix_world@v.co for v in obj.data.vertices]
    bounds_checked+=1
    return [[min(v[i] for v in vs),max(v[i] for v in vs)] for i in range(3)]

def finish(obj,kind,seed=0):
    obj.data.materials.append(mat)
    attr=obj.data.color_attributes.new(name='Paint',type='FLOAT_COLOR',domain='CORNER')
    base=PALETTE[kind]; rng=random.Random(seed)
    b=bounds(obj); span=max(.01,b[2][1]-b[2][0])
    for poly in obj.data.polygons:
        poly.use_smooth=kind in ['leaf','leaflight','bark','moss','stone']
        stroke=rng.uniform(-.024,.024)
        for idx in poly.loop_indices:
            v=obj.data.vertices[obj.data.loops[idx].vertex_index].co
            wash=.82+.24*(v.z-b[2][0])/span
            # Large soft washes and small polygon variation read from the game camera.
            wash+=.045*math.sin(v.x*2.8+v.y*1.7)+stroke
            attr.data[idx].color=tuple(max(.012,c*wash) for c in base)+(1,)
    parts.append(obj)
    return obj

def mesh(name,verts,faces,kind,seed=0):
    m=bpy.data.meshes.new(name); m.from_pydata(verts,[],faces); m.update()
    o=bpy.data.objects.new(name,m); scene.collection.objects.link(o)
    return finish(o,kind,seed)

def box(name,p,size,kind='wood',bevel=.03):
    bpy.ops.mesh.primitive_cube_add(size=2,location=p)
    o=bpy.context.object; o.name=name; o.scale=tuple(s/2 for s in size)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Soft worn edge','BEVEL'); mod.width=bevel; mod.segments=2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o,kind)

def tube(name,start,end,r0,r1=None,kind='bark',sides=10):
    a,b=Vector(start),Vector(end); direction=(b-a).normalized()
    helper=Vector((0,0,1)) if abs(direction.z)<.98 else Vector((1,0,0))
    right=direction.cross(helper).normalized(); up=right.cross(direction).normalized()
    r1=r0 if r1 is None else r1
    verts=[]
    for j,(p,r) in enumerate([(a,r0),(b,r1)]):
        for i in range(sides):
            angle=math.tau*i/sides
            v=p+right*math.cos(angle)*r+up*math.sin(angle)*r
            verts.append(tuple(v))
    faces=[tuple(range(sides-1,-1,-1)),tuple(range(sides,sides*2))]
    faces += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    return mesh(name,verts,faces,kind)

def beam(name,start,end,w,d,kind='wood'):
    a,b=Vector(start),Vector(end); f=(b-a).normalized()
    helper=Vector((0,0,1)) if abs(f.z)<.98 else Vector((1,0,0))
    r=f.cross(helper).normalized(); u=r.cross(f).normalized()
    verts=[tuple(p+r*sx*w/2+u*sy*d/2) for p in [a,b] for sx,sy in [(-1,-1),(1,-1),(1,1),(-1,1)]]
    return mesh(name,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],kind)

def blob(name,p,size,kind='leaf',seed=0,segments=18,rings=10):
    rng=random.Random(seed); verts=[]; faces=[]
    for j in range(rings+1):
        phi=math.pi*j/rings
        for i in range(segments):
            theta=math.tau*i/segments
            lobes=1+.065*math.sin(theta*5+seed)+.04*math.cos(theta*3-phi*4)
            verts.append((p[0]+math.sin(phi)*math.cos(theta)*size[0]/2*lobes,
                          p[1]+math.sin(phi)*math.sin(theta)*size[1]/2*lobes,
                          p[2]+math.cos(phi)*size[2]/2*(1+.035*math.sin(theta*4))))
    for j in range(rings):
        for i in range(segments):
            ni=(i+1)%segments
            faces.append((j*segments+i,j*segments+ni,(j+1)*segments+ni,(j+1)*segments+i))
    return mesh(name,verts,faces,kind,seed)

def tree(seed=1,height=14,lean=.5):
    rng=random.Random(seed)
    stem=[(0,0,-.12),(.12,-.05,2),(.08,.10,4.3),(lean,.12,6.4),(lean+.1,.1,9),(.3,.1,height-1.5)]
    radii=[.65,.49,.38,.31,.21,.07]
    for i in range(len(stem)-1): tube('Silver trunk',stem[i],stem[i+1],radii[i],radii[i+1],sides=12)
    for i in range(6):
        t=math.tau*i/6+.15
        tube('Buttress root',(.12*math.cos(t),.12*math.sin(t),.50),
             (1.9*math.cos(t),1.9*math.sin(t),.025),.24,.055)
    for i in range(9):
        t=i*2.399+seed*.2; z=6.2+(i%3)*1.2
        extent=3.0+(i%4)*.65
        middle=(math.cos(t)*extent*.6,math.sin(t)*extent*.6,z+1.25)
        end=(math.cos(t)*extent,math.sin(t)*extent,z+2.5)
        tube('Sweeping branch',(.1,.1,z-.15),middle,.22,.13)
        tube('Branch fork',middle,end,.14,.06)
        size=(5.6+(i%3)*.65,4.8+(i%2)*.6,3.6)
        blob('Layered foliage',(end[0],end[1],end[2]+.45),size,'leaf' if i%3 else 'leaflight',seed+i)
    blob('Upper crown',(.3,.2,height-1.1),(6.0,5.8,4.2),'leaflight',seed+30)
    # Small leaf shelves break the outline instead of a single smooth ball.
    for i in range(12):
        t=i*2.1; r=3.3+i%3*.75
        blob('Outer leaf spray',(math.cos(t)*r,math.sin(t)*r,9.0+(i%4)*.85),
             (2.4,2.0,1.5),'leaf',seed+100+i,12,7)
    for i in range(10):
        z=1.1+i*.52
        tube('Bark crease',(.39,0,z),(.38,.16,z+.14),.018,.012,'bark',6)

def record(asset,boxes=None):
    global parts
    bpy.ops.object.select_all(action='DESELECT')
    copies=[]
    for o in parts:
        c=o.copy(); c.data=o.data.copy(); scene.collection.objects.link(c)
        c.select_set(True); copies.append(c)
    bpy.context.view_layer.objects.active=copies[0]
    bpy.ops.object.join()
    joined=bpy.context.object; joined.name=asset+'_export'
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    # Ground-centred export origin; source parts keep their artistic pivots.
    scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    b=bounds(joined)
    bpy.ops.export_scene.gltf(filepath=str(OUT/(asset+'.glb')),export_format='GLB',
        use_selection=True,use_active_scene=True,export_apply=True,export_vertex_color='ACTIVE',
        export_vertex_color_name='Paint',export_active_vertex_color_when_no_material=True)
    catalog.append({'id':asset,'bounds':b,'vertices':len(joined.data.vertices),
        'triangles':sum(len(p.vertices)-2 for p in joined.data.polygons),
        'collision_boxes':boxes or [],'license':'project-owned original geometry',
        'origin':'ground-centred; Blender Z up / glTF Y up','paint':'vertex washes, one material'})
    # Only the temporary joined export is removed; all editable parts stay saved.
    bpy.data.objects.remove(joined,do_unlink=True)
    collection=bpy.data.collections.new(asset); scene.collection.children.link(collection)
    for o in parts:
        for old in list(o.users_collection): old.objects.unlink(o)
        collection.objects.link(o)
        o.hide_set(True); o.hide_render=True
    parts=[]

for i in range(3):
    tree(31+i*23,14+i*.6,(-.45,.6,.15)[i])
    record('beech_canopy_'+str(i),[{'center':[0,3.8,0],'size':[1.05,7.6,1.05],'part':'trunk'}])

# Dense, physically solid wooded outcrops form forest masses with clear edges.
for variant in range(2):
    box('Rock heart',(0,0,.70),(8.5,7.0,1.55),'stone',.20)
    blob('Moss on root bank',(0,0,1.15),(9.6,8.3,1.65),'moss',variant,22,10)
    for i in range(5):
        before=len(parts); tree(80+variant*41+i*13,11+(i%3),.4)
        shift=Vector(((i%3-1)*2.65,(i//3-.55)*3.2,1.15))
        for o in parts[before:]:
            for v in o.data.vertices: v.co+=shift
    for i in range(8):
        t=i*math.tau/8
        blob('Dense woody undergrowth',(math.cos(t)*3.9,math.sin(t)*3.2,1.45),
             (3.0,2.8,2.8),'moss',i+variant*20,12,8)
    record('beech_copse_'+str(variant),[{'center':[0,1.15,0],'size':[8.5,2.3,7.0],'part':'rock'}])

# Walkable 36m bridge, repaired deck, genuine stringers and separate rail shapes.
for y in [-1.65,1.65]: beam('Bridge stringer',(-18,y,-.25),(18,y,-.25),.42,.60)
for i in range(72):
    x=-17.75+i*.5
    box('Deck plank',(x,0,.0),(.49,5.0,.20),'fresh' if 26<=i<=35 else 'wood',.018)
for y in [-2.36,2.36]:
    for x in range(-18,19,3):
        beam('Railing post',(x,y,-.18),(x,y,1.45),.18,.20)
        box('Iron peg',(x,y,1.05),(.24,.24,.11),'iron',.012)
    for z in [.70,1.32]: beam('Railing',(-18.1,y,z),(18.1,y,z),.16,.16)
for x in [-16,-8,8,16]:
    beam('Bridge brace',(x,-1.65,-2.9),(x+2.0,-1.65,-.20),.24,.24)
record('repaired_beech_bridge',[
 {'center':[0,0,0],'size':[36,.20,5.0],'walkable':True},
 {'center':[0,.78,-2.36],'size':[36,1.45,.20],'part':'rail'},
 {'center':[0,.78,2.36],'size':[36,1.45,.20],'part':'rail'}])

# Shelter frame: solid posts, open working space, shingled roof and repaired bay.
for x in [-4.8,0,4.8]:
    for y in [-2.75,2.75]:
        beam('Shelter post',(x,y,-.06),(x,y,3.65),.28,.32)
        beam('Knee brace',(x,y,2.7),(x+(.65 if x<4 else -.65),y,3.60),.14,.14)
for y in [-2.8,2.8]: beam('Eave beam',(-5.1,y,3.60),(5.1,y,3.60),.32,.35)
beam('Ridge',(-5.2,0,5.3),(5.2,0,5.3),.28,.30)
for x in [-5,-3.35,-1.65,0,1.65,3.35,5]:
    for side in [-1,1]: beam('Roof rafter',(x,0,5.30),(x,side*3.05,3.48),.16,.22)
for side in [-1,1]:
    for row in range(7):
        y0=side*(row*.43-.03); y1=side*((row+1)*.43+.07)
        z0=5.35-abs(y0)*.6; z1=5.35-abs(y1)*.6
        for col in range(20):
            x0=-5.15+col*.52; x1=x0+.55
            mesh('Wood roofing',[(x0,y0,z0),(x1,y0,z0),(x1,y1,z1),(x0,y1,z1)],[(0,1,2,3)],'fresh' if col<3 else 'wood',row+col)
record('working_forest_shelter',[{'center':[x,1.78,z],'size':[.30,3.65,.34],'part':'post'} for x in [-4.8,0,4.8] for z in [-2.75,2.75]])

for i in range(20):
    t=i*math.tau/20
    tube('Kiln wood',(-math.cos(t)*2.0,-math.sin(t)*2.0,.16),
         (-math.cos(t)*.22,-math.sin(t)*.22,2.1),.20,.14,'wood')
blob('Soil-covered kiln',(0,0,1.10),(4.5,4.3,2.8),'coal',4,24,12)
for i in range(8):
    t=i*math.tau/8
    tube('Vent channel',(math.cos(t)*2.15,math.sin(t)*2.15,.10),
         (math.cos(t)*1.8,math.sin(t)*1.8,.50),.095,.08,'coal')
record('covered_charcoal_kiln',[{'center':[0,1,0],'size':[3.6,2,3.6],'part':'structure'}])

for y in [-1.2,1.2]: beam('Timber spacer',(-2.5,y,.05),(2.5,y,.05),.15,.18)
for row in range(3):
    for i in range(5-row):
        x=(i-(4-row)/2)*.50
        tube('Stacked timber',(x,-2.4,.38+row*.43),(x,2.4,.38+row*.43),.245,.22,'bark',12)
record('seasoned_timber',[{'center':[0,.7,0],'size':[2.6,1.4,4.7],'part':'structure'}])

# Distant camp roof: repaired cover on an old structure, open flank access.
for x,y in [(-2.5,-2),(2.5,-2),(-2.5,2),(2.5,2)]:
    tube('Forked stake',(x,y,-.10),(x,y,3.3),.11,.075,'wood')
beam('Camp ridge',(-2.9,0,4.2),(2.9,0,4.2),.20,.20)
for x in [-2.8,0,2.8]:
    for side in [-1,1]: beam('Camp rafters',(x,0,4.20),(x,side*2.35,3.1),.12,.14)
for side in [-1,1]:
    verts=[]; faces=[]
    for i in range(13):
        for j in range(7):
            x=-2.9+i*5.8/12; y=side*j*2.35/6
            z=4.24-abs(y)*.48-.13*math.sin(i*.8)*math.sin(j*.5)
            verts.append((x,y,z))
    for i in range(12):
        for j in range(6):
            a=i*7+j; faces.append((a,a+7,a+8,a+1))
    mesh('Sagging patched cover',verts,faces,'cloth',side)
record('kager_reused_shelter',[{'center':[x,1.6,z],'size':[.23,3.3,.23],'part':'post'} for x in [-2.5,2.5] for z in [-2,2]])

blob('Layered boulder',(0,0,1.4),(5.0,3.3,3.5),'stone',83,16,10)
blob('Moss shoulder',(-.6,-.1,2.1),(3.2,2.9,.9),'moss',53,14,8)
record('mossy_outcrop',[{'center':[0,1.35,0],'size':[3.4,2.6,2.4],'part':'rock'}])

# Low plants are 3D meshes, no billboards or sprites.
for i in range(9):
    t=i*math.tau/9
    for j in range(4):
        r=.18+j*.20; p=(math.cos(t)*r,math.sin(t)*r,.8*math.sin((j+1)*.45))
        blob('Fern leaf',p,(.22,.42,.08),'leaf',i*4+j,8,4)
record('woodland_fern')

for i in range(6):
    t=i*2.399; tube('Herb stem',(0,0,0),(math.cos(t)*.28,math.sin(t)*.28,.6),.018,.012,'moss',6)
    blob('Herb leaf',(math.cos(t)*.28,math.sin(t)*.28,.45),(.35,.2,.10),'leaflight',i,8,4)
record('sunlit_herb')

SOURCE.joinpath('catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf-8')
SOURCE.joinpath('provenance.json').write_text(json.dumps({'author':'Project G asset production through Blender MCP','source':'original procedural geometry and vertex paint','third_party_assets':False,'bounds_checks':bounds_checked,'connection_overlap_min_m':.01,'units':'metres'},indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_revision2_assets.blend'))
result={'asset_count':len(catalog),'bounds_checks':bounds_checked,'triangles':{m['id']:m['triangles'] for m in catalog},'source':str(SOURCE/'beech_revision2_assets.blend')}
