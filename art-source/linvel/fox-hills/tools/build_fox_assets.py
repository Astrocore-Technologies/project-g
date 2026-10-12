"""Own Fox Hills static assets. Geometry creation runs only inside Blender MCP."""
# Connection map (metres, Z-up): footing/walls embed .04m; timber enters plaster
# .05m; roof underlay overlaps eaves .06m; shingles penetrate shell .02m;
# shelter posts embed footings .05m, braces share measured beam endpoints;
# ruined wall stones overlap adjoining courses .03m; shrubs meet branch tips;
# heather stems meet leaf rosettes .02m. Ground origins and unit scale are kept
# for Godot placement roots. Bounds and joint assertions run before GLB export.
import bpy, bmesh, math, random, json, importlib.util, contextlib, io
from pathlib import Path
from mathutils import Vector
import numpy as np

SRC=Path("D:/projects/project-g/art-source/linvel/fox-hills")
GAME=Path("D:/projects/project-g/Content.Client/Assets/World/Linvel/FoxHills")
spec=importlib.util.spec_from_file_location("fox_joinery","D:/projects/project-g/art-source/linvel/tools/linvel_builder.py")
lv=importlib.util.module_from_spec(spec);spec.loader.exec_module(lv)
SCENE=None; MAT=None; CATALOG=[]; RNG=random.Random(68471)

def finish(b):
    vertices=[]; faces=[]; uvs=[]; smooth=[]; parts=[]
    for group in b.groups.values():
        offset=len(vertices);vertices.extend(group['v'])
        faces.extend(tuple(offset+i for i in face) for face in group['f'])
        uvs.extend(group['uv']);smooth.extend(group['smooth']);parts.extend(group['parts'])
    mesh=bpy.data.meshes.new(b.key);mesh.from_pydata(vertices,[],faces);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new(b.key,mesh);SCENE.collection.objects.link(obj);mesh.materials.append(MAT)
    layer=mesh.uv_layers.new(name="PaintedUV")
    for p,uv,sm in zip(mesh.polygons,uvs,smooth):
        p.use_smooth=sm
        for loop,coord in zip(p.loop_indices,uv):layer.data[loop].uv=coord
    ids={p:i for i,p in enumerate(sorted(set(parts)))}
    mesh.attributes.new("FoxPartID","INT","FACE").data.foreach_set("value",[ids[p] for p in parts])
    assert all(math.isfinite(c) for v in vertices for c in v)
    assert all(p.area>1e-10 for p in mesh.polygons),b.key
    assert obj.scale==Vector((1,1,1)) and max(abs(v) for v in obj.rotation_euler)<1e-7
    mesh.calc_loop_triangles()
    bounds=[(min(v[i] for v in vertices),max(v[i] for v in vertices)) for i in range(3)]
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT");obj.select_set(True);bpy.context.view_layer.objects.active=obj
    with contextlib.redirect_stdout(io.StringIO()):
        bpy.ops.export_scene.gltf(filepath=str(GAME/(b.key+".glb")),export_format="GLB",use_selection=True,use_active_scene=True,export_yup=True,export_apply=True,export_animations=False)
    obj.asset_mark();obj.asset_data.description=b.label+" · Лисьи холмы · Project G"
    info={"id":b.key,"label":b.label,"triangles":len(mesh.loop_triangles),"bounds":bounds,"parts":len(ids),"joints":b.joints,"origin":"ground","materials":1,"uv":"PaintedUV"}
    CATALOG.append(info)
    (SRC/"reports"/(b.key+".json")).write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding="utf8")
    obj.hide_set(True);obj.hide_render=True;obj.select_set(False)
    return obj

def rock(b,name,center,dims,seed):
    rng=random.Random(seed);bm=bmesh.new();bmesh.ops.create_icosphere(bm,subdivisions=3,radius=1)
    c=Vector(center);s=Vector(dims)*.5
    for v in bm.verts:
        p=v.co.copy();w=1+.09*math.sin(p.x*5+p.z*3+seed)+.045*math.cos(p.y*7+seed)
        v.co=c+Vector((p.x*s.x*w,p.y*s.y*w,p.z*s.z*w))
    bm.verts.ensure_lookup_table()
    vertices=[tuple(v.co) for v in bm.verts];faces=[tuple(v.index for v in f.verts) for f in bm.faces];bm.free()
    lo=[min(v[i] for v in vertices) for i in range(3)];hi=[max(v[i] for v in vertices) for i in range(3)]
    uv=[[(.03+.94*(vertices[j][0]-lo[0])/max(.001,hi[0]-lo[0]),.03+.94*(vertices[j][2]-lo[2])/max(.001,hi[2]-lo[2])) for j in f] for f in faces]
    b.add(name,vertices,faces,"stone","Rock",True,uvs=uv)
    # Small closed moss pads sit within the measured top of the rock.
    for i in range(4):
        p=(c.x+rng.uniform(-.25,.25)*dims[0],c.y+rng.uniform(-.2,.2)*dims[1],hi[2]-.12)
        lv.leaf(b,name+"Lichen"+str(i),p,(.22,.1,.015),.15,"leaf")

def watchpost():
    b=lv.Builder("hill_watchpost","Сторожевой пост с каменным основанием","architecture")
    base=b.box("Footing",(0,0,.36),(5.9,5.2,.72),"stone",bevel=.07)
    wall=b.box("Plaster",(0,0,3.25),(5.6,4.9,5.86),"plaster",bevel=.06)
    b.joint(base,wall,2,.04)
    lv.stone_courses(b,5.9,5.2,.72)
    for x in (-2.77,2.77):
        for y in (-2.42,2.42):
            p=b.box("Corner"+str(x)+str(y),(x,y,3.2),(.27,.27,5.8),"oak",bevel=.025);b.joint(p,wall,0,.04)
    for z in (1.0,3.4,6.1):b.box("Belt"+str(z),(0,0,z),(5.82,5.12,.18),"darkwood")
    lv.roof(b,"SlateRoof",5.7,5.0,6.12,2.5,"slate")
    lv.opening(b,"Door",0,-2.55,.68,1.15,2.1,door=True)
    for x in (-1.75,1.75):lv.opening(b,"FrontWindow"+str(x),x,-2.53,3.85,.9,1.25,shutters=True)
    lv.opening(b,"SideWindow",0,2.53,3.85,1.0,1.3)
    for i in range(4):b.box("Step"+str(i),(0,-3.4+i*.27,.11+i*.17),(1.75,.36,.22),"stone",bevel=.025)
    b.box("Chimney",(1.65,1.05,7.1),(.58,.64,2.2),"stone",bevel=.04)
    b.box("ChimneyCap",(1.65,1.05,8.13),(.76,.82,.16),"stone")
    lv.banner(b,"WatchPennant",2.3,-2.63,5.8,.52,1.5,"blue")
    finish(b)

def shelter():
    b=lv.Builder("shepherd_shelter","Навес пастуха с лавкой","architecture")
    for x in (-2.5,2.5):
        for y in (-1.8,1.8):
            pad=b.box("Pad"+str(x)+str(y),(x,y,.13),(.62,.62,.26),"stone",bevel=.07)
            post=b.box("Post"+str(x)+str(y),(x,y,1.6),(.28,.28,3.05),"oak",bevel=.025)
            b.joint(pad,post,2,.05)
    for y in (-1.8,1.8):
        b.span("Tie"+str(y),(-2.68,y,3.02),(2.68,y,3.02),.13,.14,"darkwood")
        for x in (-2.5,2.5):b.span("Brace"+str(x)+str(y),(x,y,2.24),(x-math.copysign(.85,x),y,3.06),.085,.09,"oak")
    lv.roof(b,"ShelterRoof",5.4,4.0,3.12,1.45,"tile")
    b.box("BenchSeat",(0,1.35,.65),(3.7,.6,.14),"oak")
    for x in (-1.45,1.45):b.box("BenchLeg"+str(x),(x,1.35,.34),(.23,.42,.65),"darkwood")
    lv.barrel(b,"RainBarrel",(2.2,1.18,.15),.8)
    finish(b)

def ruin():
    b=lv.Builder("ruined_homestead","Заброшенное хозяйство с остатками очага","architecture")
    b.box("Foundation",(0,0,.13),(8.2,6.4,.26),"stone",bevel=.06)
    for side in (-1,1):
        for i in range(12):
            x=-3.8+i*.68
            rows=5 if i<3 else 3 if i<8 else 2
            for row in range(rows):
                b.box("Rear"+str(side)+str(i)+str(row),(x,side*3.05,.47+row*.47),(.72,.52,.5),"stone",bevel=.065)
    for x in (-3.85,3.85):
        for i in range(8):
            for row in range(3 if i>3 else 1):
                b.box("Side"+str(x)+str(i)+str(row),(x,-2.4+i*.68,.47+row*.47),(.53,.72,.5),"stone",bevel=.055)
    b.box("Hearth",(-2.1,1.7,.32),(1.5,1.2,.64),"stone",bevel=.04)
    b.box("ChimneyRemnant",(-2.1,2.38,1.85),(.9,.8,3.6),"stone",bevel=.08)
    for k in range(6):b.span("FallenRafter"+str(k),(-2.8+k*.58,-1.5,.3),(-2.2+k*.58,1.2,.7),.1,.1,"darkwood")
    for k in range(12):rock(b,"Rubble"+str(k),(RNG.uniform(-3.7,3.7),RNG.uniform(-2.4,2.4),.35),(RNG.uniform(.3,.65),.5,.55),k+99)
    finish(b)

def vegetation():
    for key,label,seed,height in [("heather_patch","Кусты цветущего вереска",31,.65),("heather_dry","Низкий сухой вереск",44,.45),("thorn_bush","Колючий кустарник",55,1.3)]:
        b=lv.Builder(key,label,"nature");rng=random.Random(seed)
        for i in range(14 if key!="thorn_bush" else 8):
            x=rng.uniform(-.65,.65);y=rng.uniform(-.6,.6);z=rng.uniform(.65,1)*height
            stem=b.span("Stem"+str(i),(x*.5,y*.5,-.04),(x,y,z),.018,.018,"darkwood",sides=6)
            for j in range(5):
                a=j*2.399+i*.7
                lv.leaf(b,"Leaf"+str(i)+str(j),(x,y,z*.45+j*z*.1),(math.cos(a)*.25,math.sin(a)*.25,.12),.075,"leaf")
            if key!="thorn_bush":
                for j in range(5):lv.flower(b,"Flower"+str(i)+str(j),(x,y,z-j*.035),.045,"pink" if key=="heather_patch" else "ivory")
        finish(b)
    b=lv.Builder("drygrass_tuft","Пучок луговой травы","nature")
    for i in range(22):
        a=i*2.399;lv.leaf(b,"Blade"+str(i),(math.cos(a)*.17,math.sin(a)*.17,-.02),(math.cos(a)*.26,math.sin(a)*.26,RNG.uniform(.3,.65)),.033,"leaf")
    finish(b)

def props():
    for key,dims,seed in [("hill_boulder",(3.0,2.3,2.1),6),("rock_shelf",(5.4,3.1,2.7),17),("rock_spire",(2.7,2.1,4.5),23)]:
        b=lv.Builder(key,"Каменный выход холмов","nature");rock(b,"Main",(0,0,dims[2]*.43),dims,seed);finish(b)
    b=lv.Builder("pasture_gate","Ворота пастбища","construction")
    for x in (-2.3,2.3):b.box("Post"+str(x),(x,0,1.05),(.28,.28,2.2),"oak",bevel=.035)
    for z in (.52,1.25,1.95):b.span("Rail"+str(z),(-2.34,0,z),(2.34,0,z),.10,.095,"oak")
    b.span("Diagonal",(-2.3,-.06,.44),(2.3,-.06,1.98),.08,.07,"darkwood")
    finish(b)
    b=lv.Builder("survey_waymarker","Дорожный указатель у седловины","props")
    b.box("Foot",(0,0,.25),(.7,.7,.5),"stone",bevel=.08)
    b.box("Post",(0,0,1.38),(.19,.19,2.45),"oak",bevel=.015)
    for z,direction in [(2.2,1),(1.7,-1)]:
        b.box("Board"+str(z),(direction*.26,0,z),(1.45,.18,.30),"oak",bevel=.025)
        b.span("Arrow"+str(z),(direction*.94,-.11,z),(direction*.72,-.11,z+.09),.025,.025,"ivory")
        b.span("ArrowLower"+str(z),(direction*.94,-.11,z),(direction*.72,-.11,z-.09),.025,.025,"ivory")
    finish(b)

def build_assets():
    global SCENE,MAT,CATALOG
    for p in (SRC/"reports",SRC/"textures",GAME):p.mkdir(parents=True,exist_ok=True)
    SCENE=bpy.data.scenes.new("FoxHills_Asset_Library");bpy.context.window.scene=SCENE
    SCENE.unit_settings.system="METRIC";SCENE.unit_settings.scale_length=1.0
    colors=list(lv.COLORS);colors[1]=(.63,.64,.57);colors[10]=(.43,.55,.26);colors[11]=(.65,.47,.69)
    n=2048;s=n//4;yy,xx=np.mgrid[0:s,0:s].astype(np.float32)/s;pixels=np.ones((n,n,4),np.float32)
    for i,c in enumerate(colors):
        wash=.022*np.sin(xx*13+yy*7)+.019*np.cos(yy*17+np.sin(xx*9))
        if i in (4,5):wash+=.02*np.sin(xx*75+np.sin(yy*7))
        if i in (10,11):wash+=.11*(yy-.4)-.02*np.sin(xx*16+yy*8)**2
        if i==1:wash+=.035*np.cos(xx*5+yy*8)-.015*np.sin(xx*35+yy*17)**4
        pixels[(i//4)*s:(i//4+1)*s,(i%4)*s:(i%4+1)*s,:3]=np.clip(np.array(c)+wash[:,:,None],.03,.97)
    image=bpy.data.images.new("Fox_Painted_Atlas",width=n,height=n,alpha=True)
    image.pixels.foreach_set(pixels.ravel());image.filepath_raw=str(SRC/"textures/fox_painted_atlas.png");image.file_format="PNG";image.save();image.pack()
    MAT=bpy.data.materials.new("Fox_Painted");MAT.use_nodes=True
    tex=MAT.node_tree.nodes.new("ShaderNodeTexImage");tex.image=image
    principled=MAT.node_tree.nodes.get("Principled BSDF");principled.inputs["Roughness"].default_value=.95
    MAT.node_tree.links.new(tex.outputs["Color"],principled.inputs["Base Color"])
    watchpost();shelter();ruin();vegetation();props()
    (SRC/"catalog.json").write_text(json.dumps(CATALOG,ensure_ascii=False,indent=2),encoding="utf8")
    bpy.data.libraries.write(str(SRC/"fox_asset_library.blend"),{SCENE},fake_user=True,compress=True)
    return {"models":len(CATALOG),"triangles":sum(a["triangles"] for a in CATALOG),"source":str(SRC/"fox_asset_library.blend")}
