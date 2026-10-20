"""Original assets, execute in Blender through MCP after inspecting the library.

Connection map (metres): tavern foundation top .48 overlaps floor .46..50;
walls .46..6.9, roof .06 embeds into eaves; porch beams embed .05 in posts.
Merchant hull continuous rings, deck z3.1 closes hull; mast feet z2.9 overlap
deck by .2; yards overlap mast centreline; sails attach along yards.
Ground roots remain unscaled, all dimensions baked by Builder. No other scene
or source collection is modified. 2D references are in production/concepts/.
"""
import bpy, math, json, io, contextlib, hashlib
from pathlib import Path
ROOT = Path('D:/projects/project-g/art-source/linvel/production')
MODEL = Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/City/Production/Models')
assert not bpy.data.scenes.get('Linvel_NewCityAssets'), 'Use the saved editable source for subsequent edits'
ctx = {'ASSETS': {}}
source=(ROOT.parent/'tools/linvel_builder.py').read_text(encoding='utf-8').replace('Linvel_AssetLibrary','Linvel_NewCityAssets')
exec(compile(source,'linvel_builder.py','exec'),ctx)
ctx['ROOT']=ROOT/'new-assets'
scene=ctx['setup']()
B=ctx['Builder']; opening=ctx['opening']; arch=ctx['arch']

def roof(b,name,cx,cy,w,d,z,rise,kind='tile'):
    # Broad curved courses retain silhouette without individual tile geometry.
    for sign in (-1,1):
        for row in range(14):
            t0=row/14;t1=(row+1.07)/14
            def p(t,y):return (cx+sign*(w*.5+.34)*t,cy+y,z+rise*(1-t)+.12*math.sin(t*math.pi))
            verts=[p(t0,-d*.5-.35),p(t0,d*.5+.35),p(t1,d*.5+.35),p(t1,-d*.5-.35)]
            b.add(name+str(sign)+'Course'+str(row),verts,[(0,1,2,3)],kind,'Roof')
        b.span(name+'Eave'+str(sign),(cx+sign*(w*.5+.34),cy-d*.5-.4,z-.05),(cx+sign*(w*.5+.34),cy+d*.5+.4,z-.05),.11,.13,'darkwood','Roof')
    b.span(name+'Ridge',(cx,cy-d*.5-.4,z+rise),(cx,cy+d*.5+.4,z+rise),.12,.12,kind,'Roof')

def tavern():
    b=B('grand_harbor_tavern','Большая портовая таверна','building')
    # Body centre is recessed northwards: porch fits the approved courtyard.
    cy=1.5; front=-4.0; back=7.0
    foundation=b.box('Foundation',(0,cy,.23),(14,11,.5),'stone','GroundFloor',.07)
    b.box('GroundFloor',(0,cy,.48),(13.5,10.5,.08),'oak','GroundFloor')
    # Open central 2.4m doorway; no decorative pane or collision across it.
    left=b.box('FrontWallLeft',(-4.1,front,1.98),(5.8,.38,3.04),'plaster','GroundFloor',.04)
    right=b.box('FrontWallRight',(4.1,front,1.98),(5.8,.38,3.04),'plaster','GroundFloor',.04)
    b.box('FrontLintel',(0,front,3.25),(2.42,.38,.5),'plaster','GroundFloor')
    for sign in (-1,1):
        b.box('SideWall'+str(sign),(sign*6.83,cy,1.98),(.36,11,3.04),'plaster','GroundFloor',.04)
    b.box('BackWall',(0,back,1.98),(14,.38,3.04),'plaster','GroundFloor')
    b.joint(foundation,left,2,.01);b.joint(foundation,right,2,.01)
    arch(b,'OpenEntry',0,front-.19,.48,2.4,2.65,'stone',.2,.34,'GroundFloor')
    b.box('UpperFloor',(0,cy,3.55),(14,11,.22),'oak','UpperFloor')
    b.box('UpperWalls',(0,cy,5.19),(14,11,3.2),'plaster','UpperFloor',.04)
    for x in (-6.85,-3.4,0,3.4,6.85):
        b.box('UpperFrontTimber'+str(x),(x,front-.24,5.18),(.18,.18,3.3),'darkwood','UpperFloor')
    for z in (.68,3.55,6.8):
        b.box('FacadeBeam'+str(z),(0,front-.24,z),(14.2,.2,.2),'darkwood','Joinery')
    for i,x in enumerate((-5,-2.5,2.5,5)):
        opening(b,'UpperWindow'+str(i),x,front-.24,4.3,1.1,1.8,shutters=True)
    for i,x in enumerate((-4.5,4.5)):
        opening(b,'GroundWindow'+str(i),x,front-.25,1.2,1.5,1.75,shutters=True)
    # Front and back gable walls; actual roof shell follows the same endpoints.
    for i,y in enumerate((front,back)):
        b.add('Gable'+str(i),[(-7,y,6.75),(7,y,6.75),(0,y,10.45)],[(0,1,2)],'plaster','UpperFloor')
        b.span('GableLeft'+str(i),(-7,y-.1,6.7),(0,y-.1,10.5),.12,.12,'darkwood','Roof')
        b.span('GableRight'+str(i),(7,y-.1,6.7),(0,y-.1,10.5),.12,.12,'darkwood','Roof')
    roof(b,'MainRoof',0,cy,14,11,6.75,3.7)
    for i,yy in enumerate((-1.1,4.4)):
        b.box('Dormer'+str(i),(4.7,yy,8.0),(1.4,1.7,1.4),'plaster','UpperFloor')
        roof(b,'DormerRoof'+str(i),4.7,yy,1.5,1.8,8.6,.65)
    b.box('Chimney',(-4.8,4,9.7),(1.1,1.05,3.8),'stone','Roof',.06)
    b.box('ChimneyCap',(-4.8,4,11.65),(1.36,1.3,.25),'stone','Roof')
    # Open porch and balcony. Furniture remains human-sized.
    b.box('PorchDeck',(0,-5.1,.38),(10.5,2.3,.24),'oak','Porch')
    for i,x in enumerate((-5,5)):
        post=b.box('PorchPost'+str(i),(x,-6.12,2),( .26,.26,3.25),'darkwood','Porch')
        beam=b.box('PorchBeam'+str(i),(x,-5.05,3.57),(.3,2.5,.26),'darkwood','Porch')
        b.joint(post,beam,2,.03)
    b.box('BalconyDeck',(0,-5.06,3.62),(10.5,2.5,.22),'oak','UpperFloor')
    for z in (3.85,4.55):b.box('BalconyRail'+str(z),(0,-6.25,z),(10.5,.12,.12),'darkwood','UpperFloor')
    for i in range(17):b.box('Baluster'+str(i),(-5+i*10/16,-6.25,4.18),(.09,.09,.86),'oak','UpperFloor',.01)
    for i in range(3):b.box('EntryStep'+str(i),(0,-6.5-i*.35,.40-i*.15),(3,.4,.2),'stone','Porch')
    b.span('SignBracket',(5.6,front-.1,3.1),(5.6,-5.1,3.1),.06,.06,'iron')
    b.box('SignBoard',(5.6,-5.1,2.55),(.95,.16,.85),'oak','Porch',.08)
    b.box('MugEmblem',(5.5,-5.2,2.55),(.32,.06,.38),'ivory','Porch')
    b.ribbon('MugHandle',[(5.68,-5.23,2.67),(5.9,-5.23,2.65),(5.9,-5.23,2.45),(5.68,-5.23,2.43)],.045,'ivory','Porch')
    for i,(x,y) in enumerate(((-3.5,-1.8),(3.2,-1.8),(-3.5,1.2),(2.8,1.2))):
        b.lathe('Table'+str(i),(x,y,.5),[(.38,0),(.3,.14),(.14,.24),(.14,.7),(.85,.74),(.85,.84)],'oak',16,'Interior')
        for j in range(3):
            a=(j*2*math.pi/3)+.4
            b.lathe('Stool'+str(i)+'_'+str(j),(x+1.17*math.cos(a),y+1.17*math.sin(a),.5),[(.2,0),(.15,.35),(.29,.39),(.29,.46)],'oak',10,'Interior')
    b.box('BarBody',(-1,4.9,1.05),(7,.9,1.1),'darkwood','Interior',.04)
    b.box('BarTop',(-1,4.9,1.65),(7.2,1.1,.16),'oak','Interior',.05)
    for zz in (1.4,2.3):
        b.box('RearShelf'+str(zz),(-1,6.65,zz),(7,.45,.12),'oak','Interior')
        for i in range(7):b.lathe('Mug'+str(zz)+str(i),(-3.5+i*.8,6.58,zz+.06),[(.09,0),(.095,.2),(.08,.24)],'ivory',8,'Interior')
    b.box('FireplaceBack',(-6.35,3.2,1.35),(.7,2,1.7),'stone','Interior',.06)
    b.box('Hearth',(-5.95,3.2,.59),(1.5,2.1,.18),'stone','Interior')
    for i in range(18):
        y=-1.5+i*.29;h=(i+1)*3.05/18
        b.box('InteriorStair'+str(i),(5.7,y,.5+h*.5),(1.65,.31,h),'oak','Interior',.01)
    info=b.finish()
    info['entry_open_width_m']=2.4;info['walkable_ground_floor']=True
    info['collision_boxes_blender']=[[[0,cy,.23],[14,11,.5]],[[-4.1,front,1.98],[5.8,.38,3.04]],[[4.1,front,1.98],[5.8,.38,3.04]],[[-6.83,cy,1.98],[.36,11,3.04]],[[6.83,cy,1.98],[.36,11,3.04]],[[0,back,1.98],[14,.38,3.04]],[[0,cy,3.55],[14,11,.22]],[[0,-5.1,.38],[10.5,2.3,.24]]]
    return info

def merchant():
    b=B('merchant_ship_large','Большой торговый парусник','ship')
    # Closed continuous hull, actual draft 4m and broad rounded shoulder.
    profiles=[(.04,-4),(.48,-3.4),(.82,-1.7),(.98,.3),(1,2.9)]
    n=40;verts=[]
    for width,z in profiles:
        for i in range(n):
            a=i*2*math.pi/n;long=22*math.cos(a);x=5.5*math.sin(a)
            x*=.55+.45*(1-abs(long)/22)**.35
            verts.append((x*width,long,z+(.75 if long<-16 else .3 if long>16 else 0)))
    faces=[]
    for row in range(len(profiles)-1):
        for i in range(n):faces.append((row*n+i,row*n+(i+1)%n,(row+1)*n+(i+1)%n,(row+1)*n+i))
    faces.append(tuple(range(n-1,-1,-1)))
    b.add('Hull',verts,faces,'oak','Hull',True)
    deck=verts[-n:]
    b.add('MainDeck',[(x,y,3.1+.75*(abs(y)/22)**7) for x,y,z in deck],[tuple(range(n))],'oak','Deck')
    for z,kind in ((.6,'slate'),(2.65,'gold'),(3.15,'darkwood')):
        points=[(x*1.005,y,z+(.75 if y<-16 else .3 if y>16 else 0)) for x,y,_ in deck];points.append(points[0])
        b.ribbon('HullBand'+str(z),points,.13 if kind=='slate' else .07,kind,'Hull',6)
    # Clear centre passage between two broad cargo hatches.
    for i,y in enumerate((-8,4)):
        b.box('CargoHatch'+str(i),(0,y,3.45),(4.1,4,.7),'darkwood','Deck',.08)
        for x in (-1.3,0,1.3):b.box('HatchBoard'+str(i)+str(x),(x,y,3.84),(1.23,3.85,.08),'oak','Deck')
    for i,y in enumerate((-4.8,8)):
        top=31 if i==0 else 26
        mast=b.span('Mast'+str(i),(0,y,2.9),(0,y,top),.25 if i==0 else .22,.25,'oak','Rigging',12)
        foot=b.box('MastFoot'+str(i),(0,y,3.2),(1,1,.6),'darkwood','Deck')
        b.joint(mast,foot,2,.2)
        for j,(zz,ww,hh) in enumerate(((top-1,7.3,5),(top-7,9.5,5.8),(top-14,10.3,6.5)) if i==0 else ((top-1,6.3,5),(top-8,8,6))):
            b.span('Yard'+str(i)+str(j),(-ww,y,zz),(ww,y,zz),.11,.11,'darkwood','Rigging',8)
            vv=[];ff=[];cols=10;rows=6
            for r in range(rows+1):
                v=r/rows
                for c in range(cols+1):
                    u=c/cols
                    vv.append(((u-.5)*2*ww*(1-.08*v),y-.12-1.25*math.sin(u*math.pi)*math.sin(v*math.pi),zz-hh*v+.38*math.sin(u*math.pi)*v))
            for r in range(rows):
                for c in range(cols):
                    k=r*(cols+1)+c;ff.append((k,k+1,k+cols+2,k+cols+1))
            b.add('Sail'+str(i)+str(j),vv,ff,'ivory','Sails',True)
            for sign in (-1,1):b.ribbon('Sheet'+str(i)+str(j)+str(sign),[(sign*ww,y,zz),(sign*ww*.92,y-.1,zz-hh),(sign*4.5,y+1.5,3.7)],.035,'darkwood','Rigging',6)
        for sign in (-1,1):b.ribbon('StandingRig'+str(i)+str(sign),[(sign*4.7,y-6,3.6),(0,y,top-.8),(sign*4.7,y+6,3.6)],.055,'darkwood','Rigging',6)
    b.span('Bowsprit',(0,-19,3.5),(0,-27,7),.16,.16,'oak','Rigging',10)
    b.add('Jib',[(0,-26.2,6.8),(0,-5,27),(0,-8,8)],[(0,1,2)],'ivory','Sails',True)
    b.ribbon('Forestay',[(0,-26.3,6.8),(0,-4.8,30)],.045,'darkwood','Rigging',6)
    b.box('SternCabin',(0,16.5,5.1),(7.7,7.5,3.9),'darkwood','Cabin',.13)
    b.box('QuarterDeck',(0,16.5,7.15),(8.1,8,.25),'oak','Cabin',.1)
    for i,x in enumerate((-2.5,0,2.5)):
        b.box('SternWindow'+str(i),(x,20.29,5.6),(1.3,.12,1.2),'glass','Glazing',.05)
        b.box('SternWindowSill'+str(i),(x,20.4,4.94),(1.5,.22,.12),'gold','Cabin')
    # Broad rails, sparsely spaced. All posts contact the continuous gunwale.
    for i in range(n):
        x,y,_=deck[i]
        if abs(y)<19:
            b.span('RailingPost'+str(i),(x,y,3.05),(x,y,4.1),.075,.075,'oak','Deck',6)
    rail=[(x,y,4.1+(.75 if y<-16 else .3 if y>16 else 0)) for x,y,_ in deck];rail.append(rail[0])
    b.ribbon('RailingTop',rail,.07,'oak','Deck',6)
    # Gold compass on forward sail, slightly forward of its curved fabric.
    for sign in (-1,1):
        b.add('Compass'+str(sign),[(0,-6.22,20.5),(sign*.6,-6.28,19.2),(0,-6.3,16.9),(-sign*.6,-6.28,19.2)],[(0,1,2,3)],'gold','Emblem')
    info=b.finish();info['waterline_m']=0;info['draft_m']=4;info['hull_length_m']=44;info['beam_m']=11
    return info

infos=[tavern(),merchant()]
stream=io.StringIO()
with contextlib.redirect_stdout(stream):
    for info in infos:
        col=bpy.data.collections['LV_'+info['key']]
        for o in bpy.data.objects:o.select_set(False)
        for o in col.all_objects:
            if o.type=='MESH':o.data.validate(clean_customdata=True)
            o.select_set(True)
        bpy.context.view_layer.objects.active=next(o for o in col.objects if o.type=='EMPTY')
        file=MODEL/(info['key']+'.glb')
        bpy.ops.export_scene.gltf(filepath=str(file),export_format='GLB',use_selection=True,use_active_scene=True,export_animations=False,export_extras=True)
        info['sha256']=hashlib.sha256(file.read_bytes()).hexdigest()
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'linvel_new_city_assets.blend'),copy=True)
(ROOT/'new-assets.json').write_text(json.dumps(infos,ensure_ascii=False,indent=2),encoding='utf-8')
result={'assets':[{'key':i['key'],'triangles':i['triangles'],'dimensions_m':i['dimensions_m'],'checked_joints':i['checked_joints']} for i in infos], 'warnings':[s for s in stream.getvalue().splitlines() if 'WARNING' in s or 'ERROR' in s]}
