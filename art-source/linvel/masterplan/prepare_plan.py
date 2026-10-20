"""Authored Linvel paper layout. Original design, metres, no runtime generation."""
from pathlib import Path
import json, math, random, html
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
GAME = ROOT.parents[2] / 'Content.Client'
OUT = GAME / 'Assets/World/Linvel/City/Masterplan'
OUT.mkdir(parents=True, exist_ok=True)
ROOT.mkdir(parents=True, exist_ok=True)

plan = dict(schema_version=1, stage='whole-city-blockout-awaiting-review',
    intent='Тёплая приморская гавань раскрывается в большой террасный город: оживлённый нижний порт, уютные жилые улицы, открытая площадь и заметная Гильдия; над крышами видны тренировочная терраса и дорога в горы.',
    reference='First user-approved Linvel panoramic art; composition and scale, not a pixel trace.',
    art_direction='Stylized Hand-Painted Anime 3D; this phase uses untextured architectural masses.',
    units='metres', city_extent_m=[400,350], sea_height=0,
    assumptions=['Dimensions and placement are reviewable prototype assumptions.',
      'No server placement, travel, persistence, encounters or economy migration.',
      'Existing detailed models remain available; blockout proxies are not replacements.',
      'Automated route clearance is not a human playtest sign-off.'],
    source_skill=dict(url='https://mcpmarket.com/tools/skills/level-designer-spatial-architect',
      method='Full SKILL.md read; MCP server configured, unavailable as a live tool in this session.',
      rules=['Intent before editor','Paper layout before blockout','Legible landmarks and primary routes',
        'Greybox playtest before art dressing','Before/after evidence for revisions']),
    route_widths_m=dict(primary=12,secondary=8,local=5,main_stairs=12,cart_ramp=10),
    platforms=[], buildings=[], links=[], streets=[], anchors=[], districts=[])
plan['layout_approved_by_user']='2026-10-20; proceed to art production, extend land on both flanks'
plan['landmasses']=[
 dict(id='WestMainland',points=[[-485,-610],[-60,-340],[-70,-170],[-156,-125],[-180,-55],[-170,50],[-210,89],[-292,86],[-350,25],[-425,-82],[-485,-210]]),
 dict(id='EastMainland',points=[[45,-350],[420,-610],[495,-210],[424,-28],[350,59],[263,100],[168,98],[147,76],[156,30],[180,-36],[204,-130],[110,-177]])]

def platform(id,part,h,points,color):
    plan['platforms'].append(dict(id=id,part=part,height=h,points=points,color=color))

platform('LowerQuay','HarborQuarter',4,[[-165,91],[-130,80],[-94,83],[-66,93],[-24,91],[16,96],[70,108],[96,128],[72,144],[-40,141],[-114,135],[-162,120]],'#cbb88f')
platform('LowerTown','ResidentialTerraces',12,[[-130,80],[-115,37],[-84,22],[-38,32],[8,29],[52,51],[70,108],[16,96],[-24,91],[-66,93],[-94,83]],'#d7c69f')
platform('CivicTerrace','CityCenter',23,[[-62,30],[-65,-26],[-44,-49],[54,-49],[68,-28],[63,32],[35,53],[-18,49]],'#ead9b2')
platform('CraftsTerrace','CraftsQuarter',18,[[-176,25],[-167,-38],[-121,-65],[-68,-46],[-65,-26],[-62,30],[-84,44],[-122,42]],'#cdbd9a')
platform('WestHomes','ResidentialTerraces',29,[[-152,-42],[-148,-98],[-115,-123],[-68,-115],[-48,-86],[-44,-49],[-68,-46],[-121,-65]],'#ded0b1')
platform('UpperTown','UpperTown',32,[[-68,-115],[-48,-153],[5,-164],[60,-151],[86,-113],[68,-65],[39,-49],[-44,-49],[-48,-86]],'#dbcfac')
platform('TrainingAcropolis','TrainingQuarter',36,[[68,-65],[86,-113],[90,-151],[145,-151],[184,-126],[181,-77],[152,-48],[106,-48]],'#d7c49b')
platform('EastHomes','SanctuaryQuarter',20,[[63,32],[65,-28],[106,-48],[152,-48],[169,-12],[156,31],[140,65],[111,82],[70,108],[52,51]],'#dcd1b0')
platform('LighthouseHeadland','HarborQuarter',5,[[-202,151],[-188,141],[-172,144],[-163,160],[-171,180],[-191,184],[-207,168]],'#cbb88f')
platform('GardenApproach','GardensQuarter',32,[[-50,-151],[-45,-224],[25,-237],[91,-212],[109,-159],[60,-151],[5,-164]],'#a6b77b')
platform('TavernCourt','ResidentialTerraces',12,[[-153,60],[-125,54],[-113,66],[-116,92],[-146,94],[-158,78]],'#d7c69f')
platform('GateApproach','GateQuarter',32,[[22,-168],[86,-168],[86,-140],[22,-140]],'#dbcfac')

def inside(p, poly):
    x,z=p; odd=False
    for i,a in enumerate(poly):
        b=poly[i-1]
        if (a[1]>z)!=(b[1]>z) and x < (b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]: odd=not odd
    return odd

def height(x,z):
    return max([p['height'] for p in plan['platforms'] if inside((x,z),p['points'])] or [-8])

kits={'house':[9,11,10.8],'tall_house':[9,11,14.2],'workshop':[13,15,10],
      'warehouse':[15,21,12],'guild':[29,22,29], 'hall':[22,16,17],
      'tavern':[19,15,15], 'shrine':[15,20,20], 'tower':[8,8,18],
      'lighthouse':[11,11,34], 'ship':[13,48,32]}
plan['kits']=kits
rng=random.Random(4037)
def building(name,part,kind,x,z,y=None,yaw=0,role='housing',scale=1):
    dims=[round(v*scale,3) for v in kits[kind]]
    plan['buildings'].append(dict(id=name,part=part,kit=kind,position=[x,height(x,z) if y is None else y,z],yaw=yaw,dimensions=dims,scale=scale,role=role))

# Each plot is deliberately placed; row variation only changes silhouette.
for i,(x,z) in enumerate([(-104,48),(-86,49),(-106,66),(-87,68),(-68,64),(-84,83),(-65,82),(-13,66),(8,66),(29,69),(-14,84),(8,85),(29,88),(48,92)]):
    building(f'LowerHome_{i+1:02}','ResidentialTerraces','tall_house' if i%4==0 else 'house',x,z,12,yaw=[0,0,5,-6][i%4])
for i,(x,z) in enumerate([(-131,-77),(-111,-80),(-91,-77),(-79,-98),(-101,-102),(-122,-103),(-132,-56),(-111,-58),(-70,-68),(-63,-90)]):
    building(f'WestHome_{i+1:02}','ResidentialTerraces','tall_house' if i%3==0 else 'house',x,z,29,yaw=[0,4,-5][i%3])
for i,(x,z) in enumerate([(-151,-17),(-129,-23),(-105,-31),(-83,-26),(-150,7),(-128,4),(-106,0),(-84,0),(-137,28),(-115,26),(-86,25)]):
    building(f'Workshop_{i+1:02}','CraftsQuarter','workshop' if i%3!=1 else 'house',x,z,18,role='workshop',yaw=[0,0,90][i%3])
for i,(x,z) in enumerate([(-43,-24),(-43,-2),(-43,22),(44,-23),(44,-1),(44,23),(-27,-37),(29,-37)]):
    role=['weapon_shop','armor_shop','camp_shop','market_shop','market_shop','market_shop','housing','housing'][i]
    building(f'CivicBuilding_{i+1:02}','CityCenter','tall_house' if i in [0,3,6] else 'house',x,z,23,yaw=90 if x<0 else -90,role=role)
building('AdventurersGuild','CityCenter','guild',0,-32,23,role='guild')
building('HarborTavern','ResidentialTerraces','tavern',-108,87,12,role='tavern')
for i,(x,z) in enumerate([(-28,-108),(-9,-110),(12,-111),(32,-116),(-25,-132),(-4,-136),(19,-137),(59,-103),(-38,-65),(35,-63)]):
    building(f'UpperHome_{i+1:02}','UpperTown','tall_house' if i%3==0 else 'house',x,z,32,yaw=[0,0,-5,5][i%4])
building('UpperPublicHall','UpperTown','hall',-5,-81,32,role='public_hall')
for i,(x,z) in enumerate([(90,-28),(110,-28),(133,-27),(92,-7),(113,-6),(141,-5),(91,15),(114,16),(141,17),(84,42),(104,68),(77,77)]):
    building(f'EastHome_{i+1:02}','SanctuaryQuarter','tall_house' if i%4==0 else 'house',x,z,20,yaw=[0,4,-5][i%3])
building('HealingHouse','SanctuaryQuarter','hall',111,44,20,role='healing_house',scale=.83)
building('SeaSanctuary','SanctuaryQuarter','shrine',141,44,20,role='sanctuary',scale=.8)
for i,(x,z) in enumerate([(-133,104),(-105,106),(-77,112),(-6,116),(25,121),(54,129)]):
    building(f'HarborWarehouse_{i+1:02}','HarborQuarter','warehouse' if i%2==0 else 'workshop',x,z,4,role='warehouse',yaw=-4 if i<3 else 8)
building('GreatLighthouse','HarborQuarter','lighthouse',-184,163,5,role='landmark')
for i,x in enumerate([31,61]): building(f'NorthGateTower_{i+1}','GateQuarter','tower',x,-155,32,role='gate')
for i,(x,z) in enumerate([(-148,-108),(-79,-126),(106,-157),(164,-142),(179,-91)]):
    building(f'WallTower_{i+1}','GateQuarter','tower',x,z,29 if i<2 else 36,role='wall',scale=.76)
for i,(x,z) in enumerate([(-21,-190),(8,-202),(72,-186)]): building(f'OrchardHouse_{i+1}','GardensQuarter','house',x,z,32,role='orchard',scale=.9)
for i,(x,z) in enumerate([(-78,182),(-22,183),(38,180)]): building(f'LargeShipEnvelope_{i+1}','HarborQuarter','ship',x,z,0,role='ship_clearance')

# Explicit paper-layout revisions to reserve usable street and stair widths.
revisions={'WestHome_01':[-140,-73], 'WestHome_02':[-111,-79.5], 'WestHome_04':[-91,-100], 'WestHome_07':[-143,-56], 'WestHome_10':[-58,-83],
 'UpperHome_08':[67,-80], 'EastHome_04':[93,-13], 'EastHome_05':[113,-12],
 'EastHome_06':[143,-13], 'Workshop_05':[-150,3], 'Workshop_09':[-137,30],
 'Workshop_06':[-129,4], 'Workshop_10':[-145,-37], 'Workshop_11':[-86,28],
 'LowerHome_01':[-111,48], 'LowerHome_03':[-110,66],
 'LowerHome_04':[-87,63], 'LowerHome_06':[-68,43], 'LowerHome_07':[-65,88.5],
 'LowerHome_08':[-13,62], 'LowerHome_09':[12,61], 'LowerHome_10':[34,63],
 'LowerHome_11':[-14,90], 'LowerHome_12':[8,91], 'LowerHome_13':[30,92],
 'HarborTavern':[-137,76], 'HarborWarehouse_03':[-77,108],
 'HarborWarehouse_05':[25,115], 'HarborWarehouse_06':[54,118],
 'UpperHome_04':[32,-113], 'EastHome_03':[138,-27], 'WestHome_08':[-111,-66], 'CivicBuilding_04':[34,-25],
 'WallTower_1':[-139,-96], 'WallTower_2':[-104,-118], 'WallTower_3':[106,-147],
 'WallTower_4':[161,-133], 'WallTower_5':[176,-91]}
for b in plan['buildings']:
    if b['id'] in revisions: b['position'][0],b['position'][2]=revisions[b['id']]
    if b['id']=='Workshop_06': b['position'][0]-=1.5
    if b['id']=='WestHome_02': b['position'][2]+=.2

def link(id,part,a,b,width,kind='ramp'):
    plan['links'].append(dict(id=id,part=part,start=a,end=b,width=width,kind=kind))
link('GrandCivicStair','CityCenter',[0,23,44],[0,12,77],12,'stairs')
link('HarborStair','HarborQuarter',[-36,12,83],[-36,4,110],12,'stairs')
link('WestCartAscent','CraftsQuarter',[-99,12,78],[-99,18,39],10)
link('CraftsCivicAscent','CityCenter',[-79,18,12],[-57,23,12],10)
link('UpperTownAscent','UpperTown',[49,23,-39],[49,32,-83],12)
link('EastCivicAscent','SanctuaryQuarter',[60,23,0],[84,20,0],10)
link('ArenaEastStair','TrainingQuarter',[132,20,-40],[132,36,-83],10,'stairs')
link('ArenaUpperAscent','TrainingQuarter',[75,32,-108],[98,36,-108],10)
link('WestHomesStair','ResidentialTerraces',[-126,18,-49],[-126,29,-82],8,'stairs')
link('LighthouseCauseway','HarborQuarter',[-152,4,116],[-184,5,155],8,'bridge')
for i,(x,start,length) in enumerate([(-105,130,77),(-50,136,82),(10,139,76),(65,138,62)]):
    link(f'GreatPier_{i+1}','HarborQuarter',[x,4.05,start],[x,4.05,start+length],9,'pier')
link('WestBreakwater','CoastAndBackdrop',[-188,3,172],[-155,3,208],8,'breakwater')
link('WestBreakwaterArm','CoastAndBackdrop',[-155,3,208],[-101,3,224],8,'breakwater')
link('EastBreakwater','CoastAndBackdrop',[100,3,131],[127,3,181],8,'breakwater')
link('EastBreakwaterArm','CoastAndBackdrop',[127,3,181],[96,3,211],8,'breakwater')

def street(id,part,pts,width):
    plan['streets'].append(dict(id=id,part=part,points=pts,width=width))
street('CivicProcessional','CityCenter',[[0,23,43],[0,23,26],[12,23,22],[12,23,-2],[0,23,-12],[0,23,-14]],12)
street('CivicUpperConnector','CityCenter',[[12,23,-2],[23,23,-11],[49,23,-11],[49,23,-39]],10)
street('LowerTownLoop','ResidentialTerraces',[[-113,12,78],[-99,12,78],[-38,12,78],[0,12,78],[48,12,78]],8)
street('LowerWestLane','ResidentialTerraces',[[-77,12,45],[-77,12,78]],6)
street('LowerEastLane','ResidentialTerraces',[[21.5,12,55],[21.5,12,91]],6)
street('TavernCourtLane','ResidentialTerraces',[[-113,12,78],[-120,12,87],[-137,12,87]],5)
street('HarborPromenade','HarborQuarter',[[-152,4,121],[-104,4,124],[-50,4,127],[0,4,132],[68,4,139]],12)
street('CraftsSpine','CraftsQuarter',[[-163,18,16],[-119,18,16],[-79,18,16]],10)
street('CraftsLane','CraftsQuarter',[[-118,18,-42],[-118,18,35]],7)
street('UpperMainStreet','UpperTown',[[49,32,-83],[48,32,-145],[46,32,-166]],12)
street('UpperHomeLane','UpperTown',[[-41,32,-121],[32,32,-125],[48,32,-125]],7)
street('UpperCrossStreet','UpperTown',[[-37,32,-94],[22,32,-95],[75,32,-108]],8)
street('ArenaWestEntry','TrainingQuarter',[[98,36,-108],[113,36,-109],[133,36,-109]],8)
street('WestHomeLane','ResidentialTerraces',[[-137,29,-89],[-77,29,-88],[-63,29,-105]],6)
street('EastNeighborhood','SanctuaryQuarter',[[84,20,3],[101,20,3],[132,20,3],[153,20,3]],8)
street('SanctuaryLane','SanctuaryQuarter',[[126,20,-36],[126,20,40],[126,20,65]],8)
street('RoadToValley','GardensQuarter',[[46,32,-166],[47,32,-190],[39,32,-221]],12)

for id,label,part,p in [('Arrival','Гавань','HarborQuarter',[-36,4,117]),('LowerSquare','Нижний город','ResidentialTerraces',[-37,12,78]),
 ('MainSquare','Центральная площадь','CityCenter',[0,23,12]),('Guild','Гильдия','CityCenter',[0,23,-19]),
 ('Crafts','Мастерские','CraftsQuarter',[-119,18,16]),('Tavern','Таверна','ResidentialTerraces',[-137,12,87]),
 ('Sanctuary','Святилище','SanctuaryQuarter',[141,20,59]),('Arena','Тренировочная арена','TrainingQuarter',[133,36,-109]),
 ('Dorn','Двор Дорна','TrainingQuarter',[137,36,-64]),('Gate','Городские ворота','GateQuarter',[46,32,-155]),
 ('Gardens','Сады и дорога в котловину','GardensQuarter',[46,32,-198]),('Lighthouse','Маяк','HarborQuarter',[-184,5,163])]:
    plan['anchors'].append(dict(id=id,label=label,part=part,position=p))
plan['districts']=[dict(id=id,scene=f'res://Scenes/Regions/Linvel/Masterplan/Parts/{id}.tscn') for id in ['CityCenter','ResidentialTerraces','CraftsQuarter','UpperTown','TrainingQuarter','SanctuaryQuarter','GateQuarter','GardensQuarter','HarborQuarter','CoastAndBackdrop']]
plan['review_questions']=['Читается ли Гильдия при подъёме из гавани?', 'Достаточны ли ширины главных маршрутов?',
 'Удобны ли перепады высот и обходы?', 'Соответствуют ли силуэт и плотность первому арту?',
 'Какой квартал первым переводить из макета в художественную сборку?']

# Design-time clearance report: full street segments against building footprints.
def dist_seg(px,pz,a,b):
    vx,vz=b[0]-a[0],b[2]-a[2]; t=max(0,min(1,((px-a[0])*vx+(pz-a[2])*vz)/(vx*vx+vz*vz or 1)))
    return math.hypot(px-a[0]-t*vx,pz-a[2]-t*vz)
plan['building_count']=sum(b['role']!='ship_clearance' for b in plan['buildings'])
clearance=[]
for route in plan['streets']+plan['links']:
    if route.get('kind') in ['pier','breakwater','bridge']: continue
    pts=route.get('points',[route.get('start'),route.get('end')])
    for a,b in zip(pts,pts[1:]):
        count=max(2,math.ceil(math.dist(a,b)))
        for house in plan['buildings']:
            if house['role']=='ship_clearance':continue
            x,y,z=house['position']; w,dep,h=house['dimensions'];ang=math.radians(house['yaw'])
            for i in range(count+1):
                t=i/count; px=a[0]+(b[0]-a[0])*t;pz=a[2]+(b[2]-a[2])*t;py=a[1]+(b[1]-a[1])*t
                if not(y-.3<=py<=y+h): continue
                dx,dz=px-x,pz-z;lx=dx*math.cos(ang)-dz*math.sin(ang);lz=dx*math.sin(ang)+dz*math.cos(ang)
                distance=math.hypot(max(abs(lx)-w/2,0),max(abs(lz)-dep/2,0))
                if distance<route['width']/2+.35:
                    clearance.append([route['id'],house['id'],round(distance-route['width']/2,2)]);break
plan['paper_clearance_conflicts']=clearance
(OUT/'layout.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
(ROOT/'layout.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')

# A legible technical paper plan is produced before the Blender proxy geometry.
W,H=1800,1600; image=Image.new('RGB',(W,H),'#f4efe2'); d=ImageDraw.Draw(image)
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',24); small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',19)
title=ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf',43)
s=2.65; ox,oz=900,800
def xy(x,z):return (round(ox+x*s),round(oz+z*s))
d.rectangle([0,100,W,1510],fill='#94c9d8')
for x in range(-250,251,25): d.line([xy(x,-245),xy(x,245)],fill='#acd4dc',width=1)
for z in range(-225,251,25): d.line([xy(-250,z),xy(250,z)],fill='#acd4dc',width=1)
for land in plan['landmasses']:
    d.polygon([xy(*p) for p in land['points']],fill='#a9b982',outline='#7e9671',width=3)
for p in sorted(plan['platforms'],key=lambda p:p['height']):
    pts=[xy(*v) for v in p['points']];d.polygon(pts,fill=p['color'],outline='#82765d',width=3)
for q in plan['streets']:
    d.line([xy(p[0],p[2]) for p in q['points']],fill='#fbefd0',width=max(2,round(q['width']*s)),joint='curve')
for q in plan['links']:
    d.line([xy(q['start'][0],q['start'][2]),xy(q['end'][0],q['end'][2])],fill='#a97449' if q['kind'] in ['pier','bridge'] else '#f8e7c6',width=round(q['width']*s))
    if q['kind']=='stairs':
        a,b=q['start'],q['end']; L=math.hypot(b[0]-a[0],b[2]-a[2]); ux,uz=-(b[2]-a[2])/L,(b[0]-a[0])/L
        for j in range(1,12):
            x=a[0]+(b[0]-a[0])*j/12;z=a[2]+(b[2]-a[2])*j/12
            d.line([xy(x+ux*q['width']/2,z+uz*q['width']/2),xy(x-ux*q['width']/2,z-uz*q['width']/2)],fill='#b29d7b',width=2)
for b in plan['buildings']:
    x,y,z=b['position'];w,dep,_=b['dimensions']; ang=math.radians(b['yaw'])
    pts=[xy(x+u*math.cos(ang)+v*math.sin(ang),z-u*math.sin(ang)+v*math.cos(ang)) for u,v in [(-w/2,-dep/2),(w/2,-dep/2),(w/2,dep/2),(-w/2,dep/2)]]
    d.polygon(pts,fill='#507d8e' if b['role'] in ['guild','landmark','sanctuary','ship_clearance'] else '#b16b4d',outline='#775743',width=2)
cx,cz=xy(133,-109); d.ellipse([cx-31*s,cz-24*s,cx+31*s,cz+24*s],fill='#dfbf7d',outline='#886f51',width=4)
for a in plan['anchors']:
    x,y,z=a['position'];px,pz=xy(x,z);text=f"{a['label']} · +{y:g} м";tw=d.textbbox((0,0),text,font=small)[2]
    d.rounded_rectangle([px-tw/2-7,pz-15,px+tw/2+7,pz+15],5,fill='#f8f2e2',outline='#8e8269')
    d.text((px-tw/2,pz-11),text,font=small,fill='#3b443b')
d.rectangle([0,0,W,100],fill='#f4efe2');d.text((40,23),'ЛИНВЕЛЬ — ОБЩИЙ ПЛАН · МАКЕТ 01',font=title,fill='#31473f')
d.text((38,1525),f"{plan['building_count']} зданий-объёмов · 10 сцен районов · вода 0 м · городские террасы +4 / +12 / +18 / +20 / +23 / +29 / +32 / +36 м",font=font,fill='#35483e')
d.text((38,1560),'Рабочий план для просмотра. Функции зданий и размеры проверяются до художественного оформления.',font=small,fill='#536052')
d.line([(1300,1440),(1455,1440)],fill='#334e49',width=5);d.text((1320,1450),'50 метров',font=small,fill='#334e49')
image.save(ROOT/'linvel-paper-plan.png')

# Orthographic 2D massing sheet, before authoring the review-only proxy kit.
sheet=Image.new('RGB',(1800,1050),'#f4efe2');sd=ImageDraw.Draw(sheet)
sd.text((35,20),'ЛИНВЕЛЬ — 2D СИЛУЭТЫ ОБЪЁМНОГО МАКЕТА',font=title,fill='#31473f')
for i,(kind,(w,dep,h)) in enumerate(kits.items()):
    col,row=i%6,i//6;cx=145+col*295;baseline=490+row*440;ss=8
    bh=h*.72;sd.rectangle([cx-w*ss/2,baseline-bh*ss,cx+w*ss/2,baseline],fill='#ddcba6',outline='#6e766c',width=2)
    sd.polygon([(cx-w*ss*.57,baseline-bh*ss),(cx,baseline-h*ss),(cx+w*ss*.57,baseline-bh*ss)],fill='#4c7280' if kind in ['guild','lighthouse','shrine'] else '#b77050',outline='#5c625c')
    sd.text((cx-110,baseline+15),kind,font=font,fill='#31473f');sd.text((cx-110,baseline+47),f'{w} × {dep} × {h} м',font=small,fill='#536052')
sheet.save(ROOT/'blockout-silhouettes-2d.png')
print(json.dumps({'buildings':plan['building_count'],'platforms':len(plan['platforms']),'districts':len(plan['districts']),'links':len(plan['links']),'paper_clearance_conflicts':clearance,'paper_plan':str(ROOT/'linvel-paper-plan.png')},ensure_ascii=False))
