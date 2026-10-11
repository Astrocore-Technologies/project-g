"""Run inside Blender MCP. Produces editable source collections and portable GLBs."""
# Connection map (metres, Z up in Blender):
# Tree roots overlap the trunk by >=0.06; branches start inside the trunk.
# Leaf crowns overlap their supporting branch tips by >=0.10.
# Shelter posts enter the roof beams by 0.10; roof overlaps beams by 0.08.
# Bridge crossbeams overlap both deck and rails by 0.04.
# Tent canvas meets ridge and ground; fire stones are separate objects by design.
import bpy, bmesh, math, json, random
from pathlib import Path
from mathutils import Vector
import numpy as np

SOURCE = Path('D:/projects/project-g/art-source/linvel/beech')
GAME = Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/Beech')
RNG = random.Random(684)
PALETTE = [(0.54,.55,.44),(.66,.47,.28),(.34,.58,.24),(.54,.70,.31),
           (.39,.47,.42),(.55,.59,.50),(.77,.70,.49),(.42,.54,.26),
           (.34,.57,.35),(.72,.58,.30),(.72,.39,.26),(.39,.30,.25),
           (.81,.77,.62),(.43,.55,.57),(.80,.65,.43),(.56,.45,.35)]
SCENE = None
COLLECTION = None
PARTS = []
CATALOG = []
ATLAS_MATERIAL = None

def bounds(obj):
    bpy.context.view_layer.update()
    vertices = [obj.matrix_world @ v.co for v in obj.data.vertices]
    return [[min(v[i] for v in vertices),max(v[i] for v in vertices)] for i in range(3)]

def overlap(a,b,axis,minimum=.005):
    aa,bb=bounds(a),bounds(b)
    value=min(aa[axis][1],bb[axis][1])-max(aa[axis][0],bb[axis][0])
    if value < minimum: raise ValueError(f'Joint gap: {a.name}/{b.name}/{axis}: {value}')
    return value

def finish_part(obj,tile):
    for c in list(obj.users_collection): c.objects.unlink(obj)
    COLLECTION.objects.link(obj)
    bpy.context.view_layer.objects.active=obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    bpy.context.view_layer.update()
    uv=obj.data.uv_layers.new(name='PaintedUV') if not obj.data.uv_layers else obj.data.uv_layers.active
    uv.name='PaintedUV'
    b=bounds(obj)
    for face in obj.data.polygons:
        face.use_smooth=True
        axis=max(range(3),key=lambda i:abs(face.normal[i]))
        axes=[i for i in range(3) if i!=axis]
        for li in face.loop_indices:
            co=obj.matrix_world @ obj.data.vertices[obj.data.loops[li].vertex_index].co
            u=(co[axes[0]]-b[axes[0]][0])/max(.001,b[axes[0]][1]-b[axes[0]][0])
            v=(co[axes[1]]-b[axes[1]][0])/max(.001,b[axes[1]][1]-b[axes[1]][0])
            uv.data[li].uv=((tile%4+.025+.95*u)/4,(tile//4+.025+.95*v)/4)
    obj.data.materials.clear();obj.data.materials.append(ATLAS_MATERIAL)
    obj.select_set(False);PARTS.append(obj)
    return obj

def box(name,center,dimensions,tile=1,bevel=.06):
    bpy.ops.mesh.primitive_cube_add(size=2,location=center)
    obj=bpy.context.object;obj.name=name
    obj.scale=Vector(dimensions)/2
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=obj.modifiers.new('SoftEdges','BEVEL');mod.width=bevel;mod.segments=2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish_part(obj,tile)

def beam(name,start,end,radius=.12,end_radius=None,tile=1,segments=10):
    a,b=Vector(start),Vector(end);direction=(b-a).normalized()
    guide=Vector((0,0,1)) if abs(direction.z)<.95 else Vector((1,0,0))
    right=direction.cross(guide).normalized();up=direction.cross(right).normalized()
    bm=bmesh.new();rings=[]
    for center,r in [(a,radius),(b,end_radius if end_radius is not None else radius)]:
        rings.append([bm.verts.new(center+r*(math.cos(i*math.tau/segments)*right+math.sin(i*math.tau/segments)*up)) for i in range(segments)])
    for i in range(segments):bm.faces.new((rings[0][i],rings[0][(i+1)%segments],rings[1][(i+1)%segments],rings[1][i]))
    bm.faces.new(list(reversed(rings[0])));bm.faces.new(rings[1])
    mesh=bpy.data.meshes.new(name);bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new(name,mesh);COLLECTION.objects.link(obj)
    return finish_part(obj,tile)

def blob(name,center,scale,tile=2,detail=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=detail,radius=1,location=center)
    obj=bpy.context.object;obj.name=name;obj.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for v in obj.data.vertices:
        v.co*=1+.035*math.sin(v.co.x*.6+v.co.y*.5+v.co.z*.7)
    return finish_part(obj,tile)

def begin(identifier):
    global COLLECTION,PARTS
    COLLECTION=bpy.data.collections.new('BV_'+identifier);SCENE.collection.children.link(COLLECTION);PARTS=[]

def export_asset(identifier):
    bpy.ops.object.select_all(action='DESELECT')
    for o in PARTS:o.select_set(True)
    bpy.context.view_layer.objects.active=PARTS[0]
    bpy.ops.object.join();mesh=bpy.context.object;mesh.name=identifier
    # Place every asset origin at its ground insertion point; preserve geometry.
    cursor=SCENE.cursor.location.copy();SCENE.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR');SCENE.cursor.location=cursor
    mesh.rotation_euler=(0,0,0);mesh.scale=(1,1,1)
    mesh.data.calc_loop_triangles()
    mesh.asset_mark();mesh.asset_data.description='Линвель · Буковая долина · Stylized Hand-Painted Anime 3D'
    b=bounds(mesh)
    if any(not math.isfinite(v.co.x+v.co.y+v.co.z) for v in mesh.data.vertices):raise ValueError(identifier)
    if any(p.area < 1e-10 for p in mesh.data.polygons):raise ValueError('Degenerate face: '+identifier)
    path=GAME/(identifier+'.glb')
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_apply=True,export_animations=False)
    CATALOG.append({'id':identifier,'triangles':len(mesh.data.loop_triangles),'bounds':b,'dimensions':[a[1]-a[0] for a in b], 'source':COLLECTION.name,'game_path':'res://Assets/World/Linvel/Beech/'+path.name})
    mesh.hide_set(True);mesh.select_set(False)

def painted_atlas():
    global ATLAS_MATERIAL
    n=1024;tile_size=n//4;yy,xx=np.mgrid[0:tile_size,0:tile_size].astype(np.float32)/tile_size
    pixels=np.ones((n,n,4),np.float32)
    for index,color in enumerate(PALETTE):
        brush=.05*np.sin(xx*29+np.sin(yy*17))+.025*np.cos(yy*43+xx*11)
        light=.86+.19*yy+.07*np.sin(xx*5+yy*7)+brush
        if index in (0,1,9,15):light-=.07*np.maximum(0,np.sin(xx*72+np.sin(yy*15)))
        y,x=index//4*tile_size,index%4*tile_size
        pixels[y:y+tile_size,x:x+tile_size,:3]=np.clip(np.array(color)*light[:,:,None],0,1)
    image=bpy.data.images.new('Beech_PaintedAtlas',width=n,height=n,alpha=True)
    image.pixels.foreach_set(pixels.ravel());image.filepath_raw=str(SOURCE/'textures'/'beech_painted_atlas.png');image.file_format='PNG';image.save();image.pack()
    mat=bpy.data.materials.new('Beech_Painted');mat.use_nodes=True
    nodes=mat.node_tree.nodes;tex=nodes.new('ShaderNodeTexImage');tex.image=image
    principled=nodes.get('Principled BSDF');principled.inputs['Roughness'].default_value=.92
    mat.node_tree.links.new(tex.outputs['Color'],principled.inputs['Base Color']);ATLAS_MATERIAL=mat

def tree(identifier,height=12,seed=1,dead=False):
    begin(identifier);rnd=random.Random(seed)
    trunk=beam('Trunk',(0,0,-.18),(.35,.12,height*.78),.48,.18,0,14)
    for i in range(5):
        angle=i*math.tau/5
        root=beam('Root',(0,0,.35),(math.cos(angle)*1.25,math.sin(angle)*1.25,.04),.23,.06,0)
        overlap(root,trunk,2)
    for i in range(7):
        angle=i*2.399;z=height*(.39+i*.065);distance=height*(.19+.035*rnd.random())
        tip=(math.cos(angle)*distance,math.sin(angle)*distance,z+height*.17)
        branch=beam('Branch',(.15,.05,z),tip,.17,.055,0)
        overlap(trunk,branch,2)
        if not dead:
            crown=blob('LeafCrown',tip,(height*.21,height*.18,height*.16),3 if i%3==0 else 2,3)
            overlap(crown,branch,2)
    if not dead:blob('TopCrown',(.3,.1,height*.87),(height*.20,height*.18,height*.15),3,3)
    export_asset(identifier)

def vegetation(identifier,kind):
    begin(identifier)
    if kind=='shrub':
        for i in range(5):
            a=i*2.4;blob('HazelCrown',(math.sin(a)*.7,math.cos(a)*.7,.85+i*.09),(.95,.8,.8),2 if i%2 else 8)
            beam('Stem',(0,0,-.04),(math.sin(a)*.6,math.cos(a)*.6,1),.055,.025,0)
    else:
        for i in range(9):
            a=i*2.399;length=.8 if kind=='fern' else .38
            end=(math.cos(a)*length,math.sin(a)*length,.15+length*.6)
            beam('Stem',(0,0,0),end,.022,.008,7,6)
            if kind=='fern':
                for j in range(1,5):
                    t=j/5;center=Vector(end)*t
                    for side in (-1,1):
                        leaf=center+Vector((-math.sin(a)*side*.16,math.cos(a)*side*.16,.03))
                        blob('FernLeaf',leaf,(.20,.09,.025),8,1)
            elif kind=='flower':
                for j in range(5):
                    q=j*math.tau/5;blob('Petal',Vector(end)+Vector((math.cos(q)*.06,math.sin(q)*.06,0)),(.07,.045,.025),12,1)
            else:blob('GrassBlade',Vector(end)*.7,(.025,.045,.30),3,1)
    export_asset(identifier)

def props():
    for identifier,seed in [('moss_boulder_a',31),('moss_boulder_b',42)]:
        begin(identifier);rnd=random.Random(seed)
        for i in range(3):
            rock=blob('WeatheredStone',(i*.9-.8,rnd.uniform(-.3,.3),.5+i*.12),(1.2,.95,.9),4 if i%2 else 5,2)
            blob('Moss',rock.location+Vector((0,0,.64)),(.9,.7,.24),7,2)
        export_asset(identifier)
    begin('forest_cliff');blob('Cliff',(0,0,5),(14,7,7),5,2);blob('RockShoulder',(10,1,3),(9,6,5),4,2);blob('MossLedge',(-3,-3,7),(9,4,1.8),7,2);export_asset('forest_cliff')
    begin('fallen_beech');beam('FallenTrunk',(-3,0,.45),(3,0,.7),.5,.35,0,14)
    for i in range(3):beam('BrokenBranch',(-1+i*1.2,0,.6),(-1+i*1.2,1.3,.95),.15,.035,0)
    export_asset('fallen_beech')
    begin('beech_stump');beam('Stump',(0,0,-.1),(.05,.03,.65),.55,.48,0,14);blob('CutFace',(.05,.03,.66),(.44,.44,.035),1,2);export_asset('beech_stump')
    begin('timber_stack')
    for layer,count in [(0,4),(1,3),(2,2)]:
        for i in range(count):beam('Log',(-2.1,i*.52-count*.26,.27+layer*.45),(2.1,i*.52-count*.26,.27+layer*.45),.24,.22,0,12)
    export_asset('timber_stack')
    begin('woodcutters_shelter');posts=[]
    for x in (-3,3):
        for y in (-2,2):posts.append(beam('Post',(x,y,-.1),(x,y,3.55),.18,.15,1))
    for y in (-2,2):
        rail=beam('RoofBeam',(-3.15,y,3.4),(3.15,y,3.4),.20,.20,1)
        for post in [p for p in posts if abs(p.location.y-y)<.01 or abs(bounds(p)[1][0]-y)<.3]:overlap(post,rail,2)
    for side in (-1,1):
        beam('Rafter',(-3.3,side*2.4,3.45),(-3.3,0,4.7),.16,.16,1)
        beam('Rafter',(3.3,side*2.4,3.45),(3.3,0,4.7),.16,.16,1)
        for j in range(12):
            y=side*(j+.5)*.2;z=4.70-abs(y)*.52
            box('RoofBoard',(0,y,z),(6.85,.245,.15),1,.025)
    box('WorkTable',(0,.8,.85),(3,1.2,.15),1)
    for x in (-1.2,1.2):beam('TableLeg',(x,.8,0),(x,.8,.86),.13,.13,1)
    export_asset('woodcutters_shelter')
    begin('wooden_footbridge')
    for i in range(48):box('DeckPlank',(-11.75+i*.5,0,.10),(.53,5.8,.20),1,.025)
    for y in (-2.55,2.55):
        beam('Stringer',(-12.1,y,-.15),(12.1,y,-.15),.18,.18,1)
        rail=beam('Handrail',(-12,y,1.17),(12,y,1.17),.11,.11,1)
        for x in (-11.5,-6,0,6,11.5):
            post=beam('RailPost',(x,y,-.13),(x,y,1.25),.12,.10,1)
            overlap(post,rail,2)
    export_asset('wooden_footbridge')
    begin('trail_sign');post=beam('SignPost',(0,0,-.12),(0,0,2.4),.13,.10,1)
    for z,width in [(1.9,1.5),(1.5,1.2)]:overlap(post,box('SignBoard',(0,0,z),(width,.18,.27),1),2)
    export_asset('trail_sign')
    begin('camp_tent')
    for x in (-1.5,1.5):beam('TentPole',(x,0,0),(x,0,2.25),.075,.065,1)
    beam('Ridge',(-1.65,0,2.21),(1.65,0,2.21),.075,.075,1)
    for side in (-1,1):
        a=(-1.8,0,2.26);b=(1.8,0,2.26);c=(1.8,side*1.85,.06);d=(-1.8,side*1.85,.06)
        mesh=bpy.data.meshes.new('Canvas');mesh.from_pydata([a,b,c,d],[],[(0,1,2,3)])
        ob=bpy.data.objects.new('Canvas',mesh);COLLECTION.objects.link(ob);finish_part(ob,6)
        bpy.context.view_layer.objects.active=ob;ob.select_set(True);mod=ob.modifiers.new('CanvasThickness','SOLIDIFY');mod.thickness=.025;bpy.ops.object.modifier_apply(modifier=mod.name);ob.select_set(False)
    export_asset('camp_tent')
    begin('fire_ring')
    for i in range(12):
        a=i*math.tau/12;blob('FireStone',(math.cos(a),math.sin(a),.18),(.32,.25,.23),4,1)
    for i in range(4):beam('CharredWood',(-.6,i*.2-.3,.13),(.6,i*.2-.3,.18),.095,.075,11)
    export_asset('fire_ring')
    begin('charcoal_mound');blob('EarthMound',(0,0,.35),(2.6,2.6,.8),11,3)
    for i in range(8):
        a=i*math.tau/8;beam('WoodVent',(math.cos(a)*2.1,math.sin(a)*2.1,.2),(math.cos(a)*1.4,math.sin(a)*1.4,.9),.10,.07,1)
    export_asset('charcoal_mound')

def build_assets():
    global SCENE
    for path in (SOURCE/'textures',GAME,SOURCE/'reports'):path.mkdir(parents=True,exist_ok=True)
    if bpy.context.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    SCENE=bpy.data.scenes.new('Beech_AssetLibrary');bpy.context.window.scene=SCENE
    SCENE.unit_settings.system='METRIC';SCENE.unit_settings.scale_length=1
    painted_atlas()
    tree('beech_mature_a',13,11);tree('beech_mature_b',11.5,27);tree('beech_young',7,55);tree('beech_dead',9,77,True)
    vegetation('hazel_shrub','shrub');vegetation('fern_cluster','fern');vegetation('meadow_flowers','flower');vegetation('grass_tuft','grass')
    props()
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_asset_library.blend'))
    (SOURCE/'catalog.json').write_text(json.dumps({'units':'metres','axes':'Blender Z-up / GLB Y-up','style':'Stylized Hand-Painted Anime 3D','assets':CATALOG},ensure_ascii=False,indent=2),encoding='utf-8')
    return {'assets':len(CATALOG),'triangles':sum(x['triangles'] for x in CATALOG),'blend':str(SOURCE/'beech_asset_library.blend'),'catalog':CATALOG}
