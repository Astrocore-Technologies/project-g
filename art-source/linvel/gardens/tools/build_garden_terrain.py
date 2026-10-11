"""Run inside Blender MCP. A continuous sculpted agricultural valley, not flat tiles."""
# Terrain and water share river coordinates. Bridges overlap both banks by
# 2m and stand .15m above their level, within the .25m nav climb budget.
import bpy,math,json,contextlib,io
from pathlib import Path
import numpy as np
SOURCE=Path('D:/projects/project-g/art-source/linvel/gardens')
GAME=Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/Gardens')
ROUTE=[[-450,290],[-445,240],[-355,210],[-255,170],[-165,105],[-90,60],[-16.234,60],[35,60],[125,20],[210,-55],[295,-130],[370,-200],[445,-225]]
PATHS=[[[-255,170],[-255,290],[-145,315],[-80,300]],[[125,20],[140,160],[260,195],[355,230]],[[210,-55],[205,-220],[70,-270],[-75,-260]],
       [[-165,105],[-145,-40],[-190,-145],[-295,-205]]]
PLACES=[{'id':'WestFarms','name':'Ближние хозяйства','x':-420,'z':230,'level':14,'radius':28},
{'id':'TerracedFarm','name':'Хозяйство на склоне','x':-260,'z':180,'level':23,'radius':38},
{'id':'MillHamlet','name':'Мельничная низина','x':-57,'z':82,'level':7.85,'radius':40},
{'id':'SunnyOrchard','name':'Солнечные сады','x':212,'z':-65,'level':27,'radius':34},
{'id':'HillPasture','name':'Верхнее пастбище','x':265,'z':200,'level':41,'radius':35},
{'id':'ForestApproach','name':'Дорога к Буковой долине','x':428,'z':-220,'level':25,'radius':28}]
FIELDS=[{'x':-295,'z':260,'w':85,'d':65,'yaw':-.22},{'x':-340,'z':125,'w':105,'d':62,'yaw':.3},
{'x':-180,'z':-45,'w':90,'d':75,'yaw':-.13},{'x':115,'z':110,'w':65,'d':85,'yaw':.17},
{'x':295,'z':-65,'w':70,'d':90,'yaw':-.25},{'x':155,'z':-220,'w':95,'d':60,'yaw':.35}]
def river_x(z):return -45+60*math.sin(z/120)
def smooth(t):t=max(0,min(1,t));return t*t*(3-2*t)
def height(x,z):
    h=12+3*math.sin(x/130)+3*math.cos(z/115)
    for a,b,amp,rx,rz in [(-280,40,25,150,115),(-170,305,21,135,110),(240,200,34,150,115),(280,-120,26,150,130),(-320,-230,28,120,110),(130,-300,24,120,90),(430,100,24,100,125)]:
        h+=amp*math.exp(-((x-a)/rx)**2-((z-b)/rz)**2)
    h+=.65*math.sin(x/23)*math.cos(z/31)
    # Secondary knolls break the long smooth slopes at ordinary camera scale.
    for a,b,amp,r in [(-350,-40,8,45),(-145,235,7,40),(215,285,8,45),(355,-285,9,48)]:
        h+=amp*math.exp(-((x-a)**2+(z-b)**2)/(r*r))
    for p in PLACES:
        w=1-smooth((math.hypot(x-p['x'],z-p['z'])-p['radius'])/38)
        h=h*(1-w)+p['level']*w
    # A broad floodplain is carved before a steep-sided narrow channel.
    r=abs(x-river_x(z));w=1-smooth((r-20)/38)
    h=h*(1-w)+7.85*w
    if r<11:h=.2 if r<8 else .2+(h-.2)*smooth((r-8)/3)
    return h
def emit(name,vs,fs,mat):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vs,[],fs);mesh.update();mesh.materials.append(mat)
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
    uv=mesh.uv_layers.new(name='PaintedUV')
    for p in mesh.polygons:
        p.use_smooth=True
        for li in p.loop_indices:
            v=mesh.vertices[mesh.loops[li].vertex_index].co;uv.data[li].uv=((v.x+480)/960,(400-v.y)/800)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    with contextlib.redirect_stdout(io.StringIO()):bpy.ops.export_scene.gltf(filepath=str(GAME/(name+'.glb')),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_apply=True,export_animations=False)
    return obj
def build_terrain():
    for p in (SOURCE/'textures',SOURCE/'reports',GAME):p.mkdir(parents=True,exist_ok=True)
    bpy.context.window.scene=bpy.data.scenes.new('Garden_Terrain_Source');sc=bpy.context.scene;sc.unit_settings.system='METRIC'
    n=2048;yy,xx=np.mgrid[0:n,0:n].astype(np.float32)/n;wx=xx*960-480;wz=400-yy*800
    dist=np.full((n,n),10000,np.float32)
    for path in [ROUTE,*PATHS]:
        for a,b in zip(path,path[1:]):
            dx,dz=b[0]-a[0],b[1]-a[1];t=np.clip(((wx-a[0])*dx+(wz-a[1])*dz)/(dx*dx+dz*dz),0,1)
            dist=np.minimum(dist,np.hypot(wx-a[0]-t*dx,wz-a[1]-t*dz))
    road=np.clip((6.5-dist)/3,0,1);road=road*road*(3-2*road)
    for p in PLACES:
        if p['id']!='HillPasture':road=np.maximum(road,np.clip((p['radius']*.7-np.hypot(wx-p['x'],wz-p['z']))/8,0,1)*.7)
    grass=np.stack([.30+.04*np.sin(wx/80),.46+.03*np.cos(wz/95),.20+.025*np.cos((wx+wz)/55)],axis=-1)
    soil=np.array((.59,.48,.32));rgb=grass*(1-road[:,:,None])+soil*road[:,:,None]
    for f in FIELDS:
        co,si=math.cos(f['yaw']),math.sin(f['yaw']);a=(wx-f['x'])*co+(wz-f['z'])*si;b=-(wx-f['x'])*si+(wz-f['z'])*co
        mask=np.clip(np.minimum(f['w']/2-abs(a),f['d']/2-abs(b))/2,0,1)*(1-road)
        furrow=(np.sin(a*2.3)+1)*.5;field=np.stack([.37+furrow*.035,.31+furrow*.11,.17+furrow*.035],axis=-1)
        rgb=rgb*(1-mask[:,:,None])+field*mask[:,:,None]
    river=np.clip((16-abs(wx-(-45+60*np.sin(wz/120))))/7,0,1)
    rgb=rgb*(1-river[:,:,None])+np.array((.45,.46,.32))*river[:,:,None]
    brush=.965+.025*np.sin(wx*.37+np.sin(wz*.3))+.015*np.cos(wz*.7+wx*.23)
    px=np.ones((n,n,4),np.float32);px[:,:,:3]=np.clip(rgb*brush[:,:,None],0,1)
    im=bpy.data.images.new('Garden_Ground_Paint',width=n,height=n,alpha=True);im.pixels.foreach_set(px.ravel());im.filepath_raw=str(SOURCE/'textures/garden_ground.png');im.file_format='PNG';im.save();im.pack()
    mat=bpy.data.materials.new('Garden_Ground');mat.use_nodes=True;tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;mat.node_tree.links.new(tex.outputs['Color'],mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    # Curvilinear topology follows both banks; 5m longitudinal samples preserve
    # small channel details while the uplands remain a smooth editable mesh.
    cols=sorted(set([-480+i*8 for i in range(121)]+[-57,-56,-54,-53,-37,-36,-34,-33]))
    nx=len(cols);nz=161;vs=[]
    for iz in range(nz):
        z=-400+iz*5;shift=river_x(z)+45
        for base in cols:
            x=base+shift*math.exp(-((base+45)/95)**2);vs.append((x,-z,height(x,z)))
    fs=[]
    for iz in range(nz-1):
        for ix in range(nx-1):
            a=iz*nx+ix;fs.extend([(a,a+nx,a+1),(a+1,a+nx,a+nx+1)])
    emit('garden_terrain',vs,fs,mat)
    water=bpy.data.materials.new('Garden_Stream');water.diffuse_color=(.17,.43,.44,1)
    wv=[];wf=[]
    for i in range(161):
        z=-400+i*5;cx=river_x(z);wv.extend([(cx-8.4,-z,4.1),(cx+8.4,-z,4.1)])
        if i:wf.append((2*i-2,2*i,2*i+1,2*i-1))
    emit('garden_stream',wv,wf,water)
    hrows=[[height(-480+ix*4,-400+iz*4) for ix in range(241)] for iz in range(201)]
    length=sum(math.dist(a,b) for a,b in zip(ROUTE,ROUTE[1:]))
    profile={'version':1,'bounds':[-480,480,-400,400],'spacing':4,'heights':hrows,'route':ROUTE,'side_paths':PATHS,'places':PLACES,'fields':FIELDS,'route_metres':length,'walking_seconds_at_3_5':length/3.5}
    (GAME/'layout.json').write_text(json.dumps(profile,ensure_ascii=False,separators=(',',':')),encoding='utf8')
    info={'vertices':len(vs),'triangles':len(fs),'route_metres':length,'walking_minutes':length/210,'height_min':min(v[2] for v in vs),'height_max':max(v[2] for v in vs)}
    (SOURCE/'reports/terrain.json').write_text(json.dumps(info,indent=2),encoding='utf8')
    bpy.data.libraries.write(str(SOURCE/'garden_terrain.blend'),{sc},fake_user=True,compress=True)
    return info
