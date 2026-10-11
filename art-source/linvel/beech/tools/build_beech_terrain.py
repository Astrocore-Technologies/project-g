"""Execute only inside Blender MCP; exported terrain remains editable in Blender."""
# Ground and river share an explicit height field. Bridge banks are level at 6 m;
# the deck reaches 6.2 m, within the existing 0.25 m navigation climb budget.
import bpy, json, math, contextlib, io
from pathlib import Path
from mathutils import Vector
import numpy as np

SOURCE=Path('D:/projects/project-g/art-source/linvel/beech')
GAME=Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/Beech')
ROUTE=[[-425,330],[-365,280],[-305,220],[-335,140],[-230,80],[-175,150],[-70,125],[68,80],[92,80],[155,140],[240,50],[195,-20],[280,-80],[320,-160],[265,-210],[340,-290],[430,-340]]
PLACES=[{'id':'ManagedEdge','name':'Обжитая опушка','x':-425,'z':330,'radius':26},
        {'id':'Woodcutters','name':'Двор лесозаготовителей','x':-305,'z':220,'radius':34},
        {'id':'RavineBridge','name':'Буковый мост','x':80,'z':80,'radius':30},
        {'id':'SunlitGlade','name':'Светлая поляна','x':240,'z':50,'radius':38},
        {'id':'KagerCamp','name':'Дальний лагерь кагеров','x':340,'z':-290,'radius':32}]
SIDE_PATHS=[[[-305,220],[-390,190],[-410,95]],[[240,50],[320,25],[375,80]],[[265,-210],[160,-215],[150,-280]]]

def segment_distance(x,z,a,b):
    dx,dz=b[0]-a[0],b[1]-a[1];t=max(0,min(1,((x-a[0])*dx+(z-a[1])*dz)/(dx*dx+dz*dz)))
    return math.hypot(x-a[0]-dx*t,z-a[1]-dz*t)

def path_distance(x,z,paths):
    return min(segment_distance(x,z,a,b) for p in paths for a,b in zip(p,p[1:]))

def river_x(z):return 80+35*math.sin((z-80)/120)
def smooth(t):t=max(0,min(1,t));return t*t*(3-2*t)
def height(x,z):
    h=8+8*(-z+400)/800+4*math.sin(x/120)*math.cos(z/130)+2*math.sin((x+z)/70)
    h+=18*math.exp(-((x+210)**2+(z+210)**2)/26000)+15*math.exp(-((x-385)**2+(z-200)**2)/18000)
    # Flatten inhabited clearings with a feathered edge instead of sharp platforms.
    for p in PLACES:
        feather=60 if p['id']=='RavineBridge' else 24
        d=math.hypot(x-p['x'],z-p['z']);w=1-smooth((d-p['radius'])/feather)
        level={'ManagedEdge':7,'Woodcutters':10,'RavineBridge':6,'SunlitGlade':15,'KagerCamp':20}[p['id']]
        h=h*(1-w)+level*w
    d=abs(x-river_x(z));bank=smooth((d-5)/10)
    h=1.0*(1-bank)+h*bank
    return h

def export_mesh(name,verts,faces,material,colors=None):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(material)
    uv=mesh.uv_layers.new(name='PaintedUV')
    for poly in mesh.polygons:
        poly.use_smooth=True
        for li in poly.loop_indices:
            co=mesh.vertices[mesh.loops[li].vertex_index].co;uv.data[li].uv=((co.x+480)/960,(400-co.y)/800)
    if colors:
        attribute=mesh.color_attributes.new(name='PaintedPalette',type='FLOAT_COLOR',domain='POINT')
        for i,color in enumerate(colors):attribute.data[i].color=(*color,1)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    with contextlib.redirect_stdout(io.StringIO()):
        bpy.ops.export_scene.gltf(filepath=str(GAME/(name+'.glb')),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_apply=True,export_animations=False)
    obj.select_set(False);return obj

def build_terrain():
    bpy.context.window.scene=bpy.data.scenes.new('Beech_Terrain_Source');scene=bpy.context.scene
    scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    n=2048;yy,xx=np.mgrid[0:n,0:n].astype(np.float32)/n
    wx=xx*960-480;wz=400-yy*800;distance=np.full((n,n),10000,np.float32)
    for path in [ROUTE,*SIDE_PATHS]:
        for a,b in zip(path,path[1:]):
            dx,dz=b[0]-a[0],b[1]-a[1]
            t=np.clip(((wx-a[0])*dx+(wz-a[1])*dz)/(dx*dx+dz*dz),0,1)
            distance=np.minimum(distance,np.hypot(wx-a[0]-t*dx,wz-a[1]-t*dz))
    road=np.clip((10-distance)/5,0,1);road=road*road*(3-2*road)
    for place in PLACES:
        if place['id'] in ('ManagedEdge','Woodcutters','KagerCamp'):
            dirt=np.clip((place['radius']-np.hypot(wx-place['x'],wz-place['z']))/7,0,1)
            road=np.maximum(road,dirt*.85)
    strokes=.94+.045*np.sin(wx*.8+np.sin(wz*.6))+.025*np.cos(wz*1.2+wx*.9)
    grass=np.stack([.24+.025*np.sin(wx/55),.43+.025*np.cos(wz/75),.18+0*wx],axis=-1)
    soil=np.array((.57,.43,.25))
    pixels=np.ones((n,n,4),np.float32)
    pixels[:,:,:3]=np.clip((grass*(1-road[:,:,None])+soil*road[:,:,None])*strokes[:,:,None],0,1)
    image=bpy.data.images.new('Beech_GroundBrush',width=n,height=n,alpha=True);image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw=str(SOURCE/'textures/ground_brush.png');image.file_format='PNG';image.save();image.pack()
    mat=bpy.data.materials.new('Beech_Terrain_Painted');mat.use_nodes=True
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
    bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=1;mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    verts=[];colors=[];heights=[]
    # Bend a strip of columns with the stream. Explicit bank/bed samples keep
    # the water silhouette smooth without a dense uniform mesh across the valley.
    columns=sorted(set([-480+i*10 for i in range(97)]+[75,85]))
    nx=len(columns);nz=161
    for iz in range(nz):
        z=-400+iz*5
        shift=river_x(z)-80
        for base_x in columns:
            x=base_x+shift*math.exp(-((base_x-80)/95)**2)
            h=height(x,z);verts.append((x,-z,h))
            d=path_distance(x,z,[ROUTE,*SIDE_PATHS]);road=1-smooth((d-5)/7)
            shade=.90+.07*math.sin(x/60)*math.cos(z/45)
            grass=np.array((.39,.54,.24))*shade;soil=np.array((.67,.59,.40))
            if abs(x-river_x(z))<12:grass=np.array((.38,.46,.35))
            color=grass*(1-road)+soil*road;colors.append(tuple(color))
    # Placement samples are independent of the render topology and have an
    # explicit spacing. Runtime movement uses Godot's native navigation bake.
    heights=[[height(-480+ix*5,-400+iz*5) for ix in range(193)] for iz in range(161)]
    # Blender winding stays upward after Y-up glTF conversion.
    faces=[]
    for iz in range(nz-1):
        for ix in range(nx-1):
            a=iz*nx+ix;b=a+1;c=a+nx;d=c+1
            faces.extend([(a,c,b),(b,c,d)])
    terrain=export_mesh('beech_terrain',verts,faces,mat,colors)
    watermat=bpy.data.materials.new('Beech_Stream');watermat.diffuse_color=(.12,.43,.49,1);watermat.use_nodes=True
    watermat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.12,.43,.49,1)
    rv=[];rf=[]
    for i in range(161):
        z=-400+i*5;cx=river_x(z);w=3.8+.8*math.sin(z/35)
        rv.extend([(cx-w,-z,1.35),(cx+w,-z,1.35)])
        if i:rf.append((2*i-2,2*i,2*i+1,2*i-1))
    export_mesh('beech_stream',rv,rf,watermat)
    # Isolate future source edits from the original primitive-actor blend file.
    with contextlib.redirect_stdout(io.StringIO()):bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_terrain.blend'))
    length=sum(math.dist(a,b) for a,b in zip(ROUTE,ROUTE[1:]))
    profile={'version':1,'bounds':[-480,480,-400,400],'spacing':5,'heights':heights,'route':ROUTE,'side_paths':SIDE_PATHS,'places':PLACES,'route_metres':length,'walking_seconds_at_3_5':length/3.5}
    (GAME/'layout.json').write_text(json.dumps(profile,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    (SOURCE/'reports/terrain.json').write_text(json.dumps({'vertices':len(verts),'triangles':len(faces),'route_metres':length,'source':'beech_terrain.blend'},indent=2),encoding='utf-8')
    return {'vertices':len(verts),'triangles':len(faces),'route_metres':round(length,1),'walking_minutes':round(length/3.5/60,2)}
