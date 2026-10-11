"""Author original Garden Valley models inside Blender MCP, never external Blender CLI."""
# Connection map, metres/Z-up: footings meet plaster at .04m; timber frame
# penetrates plaster by .06m; rafters enter ridge/eaves by .06m; overlapping
# shingle rows cover the roof shell by .02m; wheel spokes meet hub/rim by .08m;
# tree roots enter trunk by .12m, forks share endpoints, crowns enclose branch
# tips by .15m; bridge joists meet deck/rails by .04m. Decorative foliage and
# crops intentionally consist of separate closed clusters. Bounds/joints are
# recorded before export. Directed members use explicit endpoint frames.
import bpy, bmesh, math, json, random, importlib.util, contextlib, io
from pathlib import Path
from mathutils import Vector, Matrix
import numpy as np

SOURCE=Path('D:/projects/project-g/art-source/linvel/gardens')
GAME=Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/Gardens')
spec=importlib.util.spec_from_file_location('garden_joinery','D:/projects/project-g/art-source/linvel/tools/linvel_builder.py')
lv=importlib.util.module_from_spec(spec);spec.loader.exec_module(lv)
SCENE=None; MAT=None; CATALOG=[]; RNG=random.Random(1684)

def finish(b):
    col=bpy.data.collections.new('GV_'+b.key);SCENE.collection.children.link(col)
    vs=[];fs=[];uvs=[];smooth=[];names=[]
    for g in b.groups.values():
        offset=len(vs);vs.extend(g['v']);fs.extend([tuple(offset+i for i in f) for f in g['f']])
        uvs.extend(g['uv']);smooth.extend(g['smooth']);names.extend(g['parts'])
    mesh=bpy.data.meshes.new(b.key);mesh.from_pydata(vs,[],fs);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new(b.key,mesh);col.objects.link(obj);mesh.materials.append(MAT)
    layer=mesh.uv_layers.new(name='PaintedUV')
    for p,uv,sm in zip(mesh.polygons,uvs,smooth):
        p.use_smooth=sm
        for li,coord in zip(p.loop_indices,uv):layer.data[li].uv=coord
    ids={n:i for i,n in enumerate(sorted(set(names)))}
    attr=mesh.attributes.new('GardenPartID','INT','FACE');attr.data.foreach_set('value',[ids[n] for n in names])
    mesh.calc_loop_triangles();obj.asset_mark();obj.asset_data.description=b.label+' · Линвель · Stylized Hand-Painted Anime 3D'
    assert all(math.isfinite(c) for v in vs for c in v)
    assert all(p.area>1e-10 for p in mesh.polygons),b.key
    assert obj.scale==Vector((1,1,1)) and max(abs(v) for v in obj.rotation_euler)<1e-7
    bounds=[(min(v[i] for v in vs),max(v[i] for v in vs)) for i in range(3)]
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    with contextlib.redirect_stdout(io.StringIO()):
        bpy.ops.export_scene.gltf(filepath=str(GAME/(b.key+'.glb')),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_apply=True,export_animations=False)
    info={'id':b.key,'label':b.label,'triangles':len(mesh.loop_triangles),'bounds':bounds,'parts':len(ids),'checked_joints':b.joints,'uv':'PaintedUV','origin':'ground','materials':1}
    CATALOG.append(info);(SOURCE/'reports'/(b.key+'.json')).write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf8')
    obj.hide_set(True);obj.hide_render=True;obj.select_set(False)
    return obj

def blob(b,name,center,scale,color='leaf',seed=0,detail=2):
    bm=bmesh.new();bmesh.ops.create_icosphere(bm,subdivisions=detail,radius=1)
    c=Vector(center);s=Vector(scale)
    for v in bm.verts:
        p=v.co;w=1+.075*math.sin(p.x*5+p.y*3+seed)+.04*math.cos(p.z*6-p.x*2+seed)
        v.co=c+Vector((p.x*s.x,p.y*s.y,p.z*s.z))*w
    bm.verts.ensure_lookup_table();vs=[tuple(v.co) for v in bm.verts];fs=[tuple(v.index for v in f.verts) for f in bm.faces];bm.free()
    lo=[min(v[i] for v in vs) for i in range(3)];hi=[max(v[i] for v in vs) for i in range(3)]
    # Continuous whole-crown coordinates prevent every triangle repeating an
    # entire painted highlight; smooth normals alone do not hide that pattern.
    uvs=[[(.03+.94*(vs[j][0]-lo[0])/max(.001,hi[0]-lo[0]),
           .03+.94*(vs[j][2]-lo[2])/max(.001,hi[2]-lo[2])) for j in f] for f in fs]
    return b.add(name,vs,fs,color,'Foliage',True,uvs=uvs)

def farmhouse(key='farm_cottage',mill=False):
    b=lv.Builder(key,'Мельничный дом' if mill else 'Сельский дом с верандой','architecture')
    width,depth= (9.2,7.4) if mill else (7.6,6.2)
    base=b.box('FieldstoneFooting',(0,0,.35),(width+.18,depth+.18,.7),'stone',bevel=.07)
    wall=b.box('LimewashedWalls',(0,0,3.15),(width,depth,5.68),'plaster',bevel=.09)
    b.joint(base,wall,2,.03)
    lv.stone_courses(b,width,depth,.72)
    for z in (.85,3.2,5.96):b.box('TimberBelt_'+str(z),(0,0,z),(width+.16,depth+.16,.16),'oak')
    for x in (-width/2,width/2):
        for y in (-depth/2,depth/2):
            post=b.box('Corner_'+str(x)+str(y),(x,y,3.3),(.26,.26,5.5),'darkwood',bevel=.035)
            b.joint(post,wall,0,.05)
    fy=-depth/2-.09
    lv.opening(b,'FrontDoor',-.9,fy,.38,1.35,2.35,door=True)
    for x in (-2.5,2.15):
        lv.opening(b,'LowerWindow'+str(x),x,fy,.98,1.05,1.6,shutters=True)
        lv.opening(b,'UpperWindow'+str(x),x,fy,3.78,1.1,1.7,shutters=True)
    for x in (-2.5,0,2.5):lv.opening(b,'RearWindow'+str(x),x,depth/2+.08,3.72,1.05,1.55)
    for side in (-1,1):
        for j in (-1.9,1.9):
            temp=lv.Builder('side','','');lv.opening(temp,'Side'+str(side)+str(j),j,-width/2-.09,1.1,1.1,1.7,shutters=True)
            lv.merge_component(b,temp,(0,0,0),side*math.pi/2)
    for y in (-depth/2,depth/2):
        b.add('LimeGable'+str(y),[(-width/2,y,5.95),(width/2,y,5.95),(0,y,9.0)],[(0,1,2)],'plaster')
        b.span('GableTie'+str(y),(-width/2,y-.04,5.99),(width/2,y-.04,5.99),.12,.10,'oak')
        for x in (-width*.3,width*.3):b.span('GableBrace'+str(y)+str(x),(x,y-.05,6),(0,y-.05,8.9),.085,.09,'oak')
    lv.roof(b,'ClayRoof',width,depth,6.0,3.0)
    # Broad covered veranda is a separate readable silhouette at game distance.
    pwidth=width*.68;py=fy-1.35
    deck=b.box('PorchDeck',(0,py,.17),(pwidth+1,2.8,.34),'stone',bevel=.04)
    for x in (-pwidth/2,pwidth/2):
        p=b.box('PorchPost'+str(x),(x,py-1.15,1.65),(.22,.22,3.16),'oak',bevel=.03)
        b.joint(deck,p,2,.03)
        b.span('PorchBracket'+str(x),(x,py-1.15,2.7),(x*.72,py-1.15,3.3),.08,.08,'darkwood')
    b.box('PorchFrontBeam',(0,py-1.15,3.2),(pwidth+.3,.25,.24),'oak')
    lv.roof(b,'PorchRoof',pwidth+.5,2.5,3.23,.78,center=(0,py))
    b.box('Chimney',(width*.28,.5,8.05),(.72,.8,3.0),'stone',bevel=.04)
    b.box('ChimneyCoping',(width*.28,.5,9.54),(.94,1.0,.20),'stone',bevel=.04)
    for x in (-2.5,2.15):
        b.box('WindowBox'+str(x),(x,fy-.28,3.56),(1.2,.4,.32),'oak')
        for k in range(7):lv.flower(b,'WindowFlower'+str(x)+str(k),(x-.45+k*.15,fy-.28,3.79),.12,'pink')
    lv.barrel(b,'RainBarrel',(width/2+.8,-depth/2+.2,.05),.8)
    if mill:
        # Wheel plane Y/Z perpendicular to its X axle; endpoints explicit.
        cx=width/2+.85;cy=.4;cz=-1.05;r=2.75
        # Masonry reaches the stream bed below the house insertion point;
        # the undershot wheel bottom is 4.05m when the bank is at 7.85m.
        b.box('StreamsideFoundation',(width/2-.3,0,-3.55),(1.25,depth+.25,7.9),'stone',bevel=.07)
        axle=b.span('WheelAxle',(width/2-.35,cy,cz),(cx+1,cy,cz),.23,.23,'darkwood',sides=16)
        for yy in (cy-.55,cy+.55):
            pts=[(yy-cy+cx,cy+r*math.cos(i*math.tau/64),cz+r*math.sin(i*math.tau/64)) for i in range(65)]
            b.ribbon('WheelRim'+str(yy),pts,.17,'darkwood',sides=10)
            for k in range(10):
                a=k*math.tau/10
                b.span('Spoke'+str(yy)+str(k),(yy-cy+cx,cy,cz),(yy-cy+cx,cy+r*math.cos(a),cz+r*math.sin(a)),.11,.12,'oak')
        for k in range(24):
            a=k*math.tau/24
            start=(cx-.65,cy+(r-.14)*math.cos(a),cz+(r-.14)*math.sin(a));end=(cx+.65,start[1],start[2])
            b.span('Paddle'+str(k),start,end,.25,.10,'oak')
        b.joint(axle,wall,0,.1)
    return finish(b)

def granary():
    b=lv.Builder('raised_granary','Приподнятый амбар','architecture')
    for x in (-2.3,2.3):
        for y in (-2,2):
            b.lathe('Pad'+str(x)+str(y),(x,y,0),[(.38,0),(.40,.3),(.29,.55),(.56,.66)],'stone',16)
            b.box('Stilt'+str(x)+str(y),(x,y,1.05),(.35,.35,.94),'oak')
    floor=b.box('Sill',(0,0,1.47),(5.6,4.8,.28),'darkwood')
    for j in range(22):
        x=-2.7+j*.25;b.box('FrontBoard'+str(j),(x,-2.25,3.08),(.24,.13,3.2),'oak')
        b.box('RearBoard'+str(j),(x,2.25,3.08),(.24,.13,3.2),'oak')
    for side in (-1,1):
        b.box('SideWall'+str(side),(side*2.73,0,3.08),(.14,4.5,3.2),'oak')
        for z in (1.5,3.0,4.62):b.box('SideTie'+str(side)+str(z),(side*2.82,0,z),(.18,4.8,.22),'darkwood')
    for x in (-2.78,2.78):b.span('Diagonal'+str(x),(x,-2.38,1.5),(-x,-2.38,4.62),.10,.10,'darkwood')
    lv.roof(b,'GranaryRoof',5.6,4.6,4.67,2.2,'slate')
    lv.opening(b,'LoadingDoor',0,-2.42,1.5,1.3,2.35,door=True)
    for i in range(5):b.box('Steps'+str(i),(0,-3.45+i*.26,.15+i*.26),(1.55,.4,.3),'oak')
    finish(b)

def drainage_props():
    b=lv.Builder('irrigation_trough','Каменный водоотвод','construction')
    base=b.box('ChannelBase',(0,0,.06),(6.1,.9,.16),'stone',bevel=.045)
    for y in (-.43,.43):
        wall=b.box('ChannelWall'+str(y),(0,y,.26),(6.1,.14,.50),'stone',bevel=.04)
        b.joint(base,wall,2,.1)
    b.add('Water', [(-3,-.35,.26),(3,-.35,.26),(3,.35,.26),(-3,.35,.26)],[(0,1,2,3)],'water','Water')
    finish(b)
    b=lv.Builder('vegetable_bed','Небольшой огород','nature')
    b.box('Soil',(0,0,.08),(2.5,4,.2),'soil',bevel=.1)
    for x in (-1.22,1.22):b.box('Border'+str(x),(x,0,.13),(.12,4.1,.22),'oak')
    for y in (-2,2):b.box('End'+str(y),(0,y,.13),(2.55,.12,.22),'oak')
    for i in range(3):
        for j in range(5):
            p=Vector((-.8+i*.8,-1.5+j*.7,.18))
            for k in range(7):
                a=k*math.tau/7;lv.leaf(b,'Leaf'+str(i)+str(j)+str(k),p,(math.cos(a)*.28,math.sin(a)*.28,.24),.13)
    finish(b)

def shed():
    b=lv.Builder('farm_shed','Навес для инструментов','architecture')
    for x in (-2.6,2.6):
        for y in (-1.8,1.8):
            p=b.box('Post'+str(x)+str(y),(x,y,1.7),(.28,.28,3.5),'oak')
            b.span('Brace'+str(x)+str(y),(x,y,2.65),(x*.7,y,3.4),.09,.1,'darkwood')
    b.box('Back',(0,1.8,1.6),(5.3,.12,3.1),'oak')
    b.box('RoofBeam',(0,0,3.41),(5.6,4,.24),'darkwood')
    lv.roof(b,'ShedRoof',5.5,3.8,3.45,1.25,'tile')
    lv.barrel(b,'Barrel',(-1.85,.95,0),.85);lv.crate(b,'Box',(1.2,.9,0),(1.3,.8,.95))
    for i in range(4):b.span('ToolShaft'+str(i),(-.65+i*.35,1.54,.04),(-.65+i*.35,1.6,2.2),.035,.035,'oak',sides=10)
    finish(b)

def orchard_tree(key,blossom=False,seed=1):
    rnd=random.Random(seed);b=lv.Builder(key,'Цветущее плодовое дерево' if blossom else 'Плодовое дерево','nature')
    trunk=b.branch('Trunk',[(0,0,-.15),(.13,.1,1.4),(-.17,.05,2.9),(.1,.15,4.2)],[.43,.32,.23,.12],'oak',14)
    for k in range(5):
        a=k*math.tau/5
        root=b.branch('Root'+str(k),[(0,0,.35),(math.cos(a)*.8,math.sin(a)*.8,.12),(math.cos(a)*1.1,math.sin(a)*1.1,.02)],[.18,.10,.035],'oak')
        b.joint(trunk,root,2,.1)
    for k in range(9):
        a=k*2.399+.2;z=2.6+(k%3)*.53;tip=Vector((math.cos(a)*(1.8+rnd.random()*.7),math.sin(a)*(1.8+rnd.random()*.7),z+1.7))
        fork=b.branch('Bough'+str(k),[(0,.06,z),tuple(tip*.6+Vector((0,0,z*.4))),tuple(tip)],[.19,.11,.038],'oak',12)
        b.joint(trunk,fork,2,.05)
        crown=blob(b,'Crown'+str(k),tip,(1.7,1.45,1.25),'pink' if blossom else 'leaf',k+seed,3)
        b.joint(crown,fork,2,.1)
        for j in range(3):
            p=tip+Vector((rnd.uniform(-1.2,1.2),rnd.uniform(-1.1,1.1),rnd.uniform(-.25,.65)))
            blob(b,'CrownTip'+str(k)+str(j),p,(.5,.48,.35),'ivory' if blossom and j==0 else 'pink' if blossom else 'leaf',k*j+seed,2)
    blob(b,'CrownTop',(.0,.1,5.65),(1.5,1.4,1.15),'pink' if blossom else 'leaf',seed,3)
    finish(b)

def props():
    b=lv.Builder('fieldstone_wall','Полевая каменная ограда','construction')
    for row in range(3):
        for i in range(8):
            x=-2+(i+.5)*.5+(row%2)*.08
            b.box('Stone'+str(row)+str(i),(x,RNG.uniform(-.05,.05),.17+row*.29),(.51,.58-RNG.random()*.09,.33),'stone',bevel=.065)
    for i in range(8):b.box('Coping'+str(i),(-1.75+i*.5,0,1.0),(.55,.66,.2),'stone',bevel=.06)
    finish(b)
    b=lv.Builder('pasture_fence','Пастбищная ограда','construction')
    for x in (-2.5,2.5):b.box('Post'+str(x),(x,0,.67),(.22,.22,1.45),'oak',bevel=.03)
    for z in (.55,1.02):
        rail=b.span('Rail'+str(z),(-2.6,0,z),(2.6,0,z),.055,.07,'oak')
        b.joint('Post-2.5',rail,0,.1)
    finish(b)
    b=lv.Builder('farm_well','Колодец и журавль','props')
    for row in range(3):
        for i in range(16):
            a=i*math.tau/16
            b.box('Stone'+str(row)+str(i),(math.cos(a)*.9,math.sin(a)*.9,.16+row*.30),(.37,.35,.32),'stone',bevel=.055)
    for x in (-1.3,1.3):b.box('WellPost'+str(x),(x,0,1.7),(.24,.24,3.4),'oak')
    lv.roof(b,'WellRoof',3,2.6,3.12,1.0,'slate')
    b.span('Axle',(-1.4,0,2.1),(1.4,0,2.1),.1,.1,'darkwood',sides=12)
    b.ribbon('Rope',[(0,0,2.1),(0,0,1.6),(0,0,.6)],.02,'soil')
    finish(b)
    b=lv.Builder('garden_trellis','Садовая шпалера','props')
    for x in (-1.5,1.5):b.box('Upright'+str(x),(x,0,1.6),(.17,.17,3.3),'oak')
    for z in (.8,1.5,2.2,3.0):b.span('Tie'+str(z),(-1.6,0,z),(1.6,0,z),.04,.045,'oak')
    for x in (-1,-.5,0,.5,1):b.span('Lattice'+str(x),(x,0,.3),(x,0,3.15),.025,.025,'oak')
    for i in range(20):lv.leaf(b,'Vine'+str(i),(-1.3+i%6*.52,-.08,.5+i//6*.7),(.2,-.1,.5),.16)
    finish(b)
    b=lv.Builder('spring_crop_patch','Весенняя грядка','nature')
    for i in range(4):
        for j in range(5):
            c=Vector((-.6+i*.4,-.8+j*.4,0))
            for k in range(5):
                a=k*2.399;lv.leaf(b,'Shoot'+str(i)+str(j)+str(k),c,(math.cos(a)*.18,math.sin(a)*.18,.34),.07)
    finish(b)
    b=lv.Builder('meadow_patch','Луговые травы и цветы','nature')
    for i in range(16):
        p=(RNG.uniform(-1,1),RNG.uniform(-1,1),0)
        if i%4==0:lv.flower(b,'Daisy'+str(i),p,.14,'ivory')
        else:
            for k in range(3):lv.leaf(b,'Grass'+str(i)+str(k),p,(RNG.uniform(-.18,.18),RNG.uniform(-.18,.18),RNG.uniform(.3,.6)),.04)
    finish(b)
    b=lv.Builder('hedgerow_shrub','Куст живой изгороди','nature')
    for i in range(5):blob(b,'Lobe'+str(i),((i-2)*.48,math.sin(i)*.25,.75+i%2*.3),(.8,.7,.75),'leaf',i,3)
    finish(b)
    for key,seed in [('garden_boulder_a',5),('garden_boulder_b',14)]:
        b=lv.Builder(key,'Светлый камень с мхом','nature')
        blob(b,'Rock',(0,0,.75),(1.65,1.1,1.1),'stone',seed,3)
        blob(b,'Moss',(-.35,.1,1.48),(.85,.65,.18),'leaf',seed,2)
        finish(b)
    b=lv.Builder('farm_bridge','Хозяйственный мост','construction')
    for y in (-1.6,1.6):
        beam=b.box('Stringer'+str(y),(0,y,-.08),(28.4,.32,.46),'darkwood')
        for x in (-13.5,-7,0,7,13.5):
            post=b.box('RailPost'+str(x)+str(y),(x,y,.78),(.22,.22,1.82),'oak')
            b.joint(post,beam,2,.05)
        b.span('TopRail'+str(y),(-14,y,1.45),(14,y,1.45),.09,.09,'oak')
    for i in range(70):
        board=b.box('Deck'+str(i),(-13.8+i*.4,0,.04),(.405,3.6,.22),'oak',bevel=.02)
        b.joint(board,'Stringer-1.6',1,.1)
    finish(b)

def build_assets():
    global SCENE,MAT,CATALOG
    for p in (SOURCE/'reports',SOURCE/'textures',SOURCE/'previews',GAME):p.mkdir(parents=True,exist_ok=True)
    SCENE=bpy.data.scenes.new('Garden_Asset_Library');bpy.context.window.scene=SCENE
    SCENE.unit_settings.system='METRIC';SCENE.unit_settings.scale_length=1
    # Original painterly atlas with broad leaf highlights, plaster wash and wood grain.
    colors=list(lv.COLORS);colors[10]=(.33,.53,.24);colors[11]=(.92,.63,.71);colors[1]=(.66,.67,.57)
    n=2048;s=n//4;yy,xx=np.mgrid[0:s,0:s].astype(np.float32)/s;px=np.ones((n,n,4),np.float32)
    for i,c in enumerate(colors):
        wash=.035*np.sin(xx*13+yy*7)+.025*np.cos(yy*19+np.sin(xx*10))
        if i in (4,5):wash+=.025*np.sin(xx*70+np.sin(yy*8))
        if i in (10,11):wash+=.12*(yy-.3)-.055*np.sin(xx*15+yy*12)**2
        px[(i//4)*s:(i//4+1)*s,(i%4)*s:(i%4+1)*s,:3]=np.clip(np.array(c)+wash[:,:,None],.02,.98)
    im=bpy.data.images.new('Garden_Painted_Atlas',width=n,height=n,alpha=True);im.pixels.foreach_set(px.ravel());im.filepath_raw=str(SOURCE/'textures/garden_painted_atlas.png');im.file_format='PNG';im.save();im.pack()
    MAT=bpy.data.materials.new('Garden_Painted');MAT.use_nodes=True
    tex=MAT.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;p=MAT.node_tree.nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.9;MAT.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
    farmhouse();farmhouse('stream_watermill',True);granary();shed()
    orchard_tree('orchard_apple',False,41);orchard_tree('orchard_blossom',True,52);orchard_tree('orchard_pear',False,62)
    props();drainage_props()
    (SOURCE/'catalog.json').write_text(json.dumps(CATALOG,ensure_ascii=False,indent=2),encoding='utf8')
    bpy.data.libraries.write(str(SOURCE/'garden_asset_library.blend'),{SCENE},fake_user=True,compress=True)
    return {'models':len(CATALOG),'triangles':sum(x['triangles'] for x in CATALOG),'source':str(SOURCE/'garden_asset_library.blend')}
