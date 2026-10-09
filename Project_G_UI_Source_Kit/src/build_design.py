#!/usr/bin/env python3
"""Generate editable SVG screens, reusable icons, and prototype screen data.
Requires Python 3.10+ and Pillow. Does not require internet access or font files.
All screen coordinates are in a 1920x1080 design viewport.
"""
from __future__ import annotations
import base64, html, json, math, re, shutil, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
C = dict(ink='#273D49',muted='#6F756D',paper='#F5EBD6',paper2='#E8D9B9',light='#FFF8E8',line='#C9B48A',gold='#B68A43',goldLight='#F3D99B',navy='#274B64',navy2='#365E77',dark='#1D303A',white='#FFF9EA',green='#387957',blue='#5095AF',red='#A1443D',purple='#806994',dim='#AFB6AA')
BODY = 'Arial, Liberation Sans, sans-serif'
HEAD = 'Georgia, Liberation Serif, serif'
# Existing design tokens take precedence, so user edits survive a rebuild.
if (ROOT/'design/tokens.json').exists():
    _saved = json.loads((ROOT/'design/tokens.json').read_text(encoding='utf-8'))
    C.update(_saved.get('colors', {}))
    BODY = _saved.get('typography', {}).get('body', BODY)
    HEAD = _saved.get('typography', {}).get('heading', HEAD)
ICONS = {
 'sword':'<path d="M10 35L31 8l9-2-1 10-22 24M7 31l14 12M12 37l-7 8M3 43l5 5"/>',
 'shield':'<path d="M24 4L42 11v15c0 10-9 16-18 21C15 42 6 36 6 26V11Z"/><path d="M24 12v25M14 23h20"/>',
 'bag':'<path d="M10 17h28l4 27H6Z"/><path d="M17 17v-6a7 7 0 0 1 14 0v6M8 27h32M18 27v7h12v-7"/>',
 'map':'<path d="M4 11l13-6 14 6 13-6v34l-13 6-14-6-13 6ZM17 5v34M31 11v34"/><path d="M23 21l5 4-5 4"/>',
 'book':'<path d="M24 11C17 5 8 7 4 9v32c6-3 13-3 20 2 7-5 14-5 20-2V9c-7-3-14-2-20 2ZM24 11v32M10 15h8M10 22h8M30 15h8M30 22h8"/>',
 'star':'<path d="M24 4l5 13 15 7-15 6-5 14-6-14L4 24l14-7Z"/>',
 'person':'<circle cx="24" cy="13" r="8"/><path d="M7 44V34c0-15 34-15 34 0v10M14 36h20"/>',
 'guild':'<path d="M7 44h34M10 44V17h28v27M5 17L24 4l19 13ZM16 22v10M32 22v10M21 44V32h6v12"/>',
 'leaf':'<path d="M8 39C-1 16 20 7 42 6c1 28-18 42-34 33ZM8 39l25-25M16 31l-2-12M24 23h11"/>',
 'potion':'<path d="M18 5h12M20 5v13L8 38c-2 4 0 7 4 7h24c4 0 6-3 4-7L28 18V5M13 31h22M18 38h10"/>',
 'coin':'<circle cx="24" cy="24" r="20"/><circle cx="24" cy="24" r="15"/><path d="M24 12v24M30 17c-13-5-16 7-5 7s9 12-7 7"/>',
 'gem':'<path d="M24 3l15 13-4 20-11 10L13 36 9 16ZM24 3v43M9 16l15 5 15-5M13 36l11-15 11 15"/>',
 'helmet':'<path d="M8 38V21a16 16 0 0 1 32 0v17l-10 7V28H18v17ZM8 23h32M24 5v18"/>',
 'boot':'<path d="M13 4h17v23l13 8v9H7V30h6ZM13 12h10M13 19h10M8 36h34"/>',
 'bow':'<path d="M8 5c30-2 33 24 32 37L8 5ZM8 5l32 37M8 40L40 8M29 8h11v11M8 31v9h9"/>',
 'scroll':'<path d="M13 5h25a6 6 0 0 1 0 12h-3v26H12a6 6 0 0 1 0-12h3V11a6 6 0 0 0-12 0h12M35 11v6M20 18h9M20 24h9M19 31h10"/>',
 'compass':'<circle cx="24" cy="24" r="18"/><path d="M24 1v6M24 41v6M1 24h6M41 24h6M31 15l-4 15-13 5 6-15Z"/>',
 'pin':'<path d="M24 46S8 28 8 18a16 16 0 0 1 32 0c0 10-16 28-16 28Z"/><circle cx="24" cy="18" r="6"/>',
 'check':'<path d="M6 25l11 11L43 9"/>',
 'close':'<path d="M12 12l24 24M36 12L12 36"/>',
 'menu':'<path d="M7 12h34M7 24h34M7 36h34"/>',
 'search':'<circle cx="20" cy="20" r="13"/><path d="M30 30l14 14"/>',
 'lock':'<rect x="10" y="22" width="28" height="23" rx="4"/><path d="M16 22V13a8 8 0 0 1 16 0v9M24 30v7"/>',
 'heart':'<path d="M24 44C8 34 0 22 6 12c4-7 14-7 18 1 4-8 14-8 18-1 6 10-2 22-18 32Z"/>',
 'chat':'<path d="M5 7h38v28H21L10 44v-9H5ZM12 17h24M12 25h17"/>',
 'chevron':'<path d="M17 9l15 15-15 15"/>',
 'eye':'<path d="M2 24c14-21 30-21 44 0-14 21-30 21-44 0Z"/><circle cx="24" cy="24" r="7"/>',
 'clock':'<circle cx="24" cy="24" r="20"/><path d="M24 11v14l10 6"/>',
 'hammer':'<path d="M7 10l9-7 22 20-9 9ZM22 22L6 42l7 5 17-21M32 10l11 11-5 5"/>',
 'feather':'<path d="M9 44L38 6M14 35C5 17 26 1 40 3c7 13-5 37-26 32ZM19 26l-1-10M25 18h11"/>',
 'sun':'<circle cx="24" cy="24" r="10"/><path d="M24 2v7M24 39v7M2 24h7M39 24h7M8 8l5 5M35 35l5 5M8 40l5-5M35 13l5-5"/>',
 'moon':'<path d="M35 6C6 10 11 39 39 35 29 54 4 45 4 24 4 10 18 0 35 6Z"/>',
 'flame':'<path d="M24 3c7 15-8 14 0 23 3-4 9-7 9-15 20 21 12 35-8 35S0 29 12 15c-2 11 7 13 12-12Z"/>',
 'bolt':'<path d="M29 2L8 27h14l-3 19 22-27H27Z"/>',
 'snow':'<path d="M24 2v44M5 13l38 22M5 35l38-22M18 5l6 6 6-6M18 43l6-6 6 6M5 20l9-1-2-9M43 28l-9 1 2 9M5 28l9 1-2 9M43 20l-9-1 2-9"/>',
 'dash':'<path d="M3 17h18M1 27h12M6 37h11M24 9l17 15-17 15M15 24h26"/>',
 'group':'<circle cx="18" cy="14" r="7"/><path d="M3 42V32c0-13 30-13 30 0v10M31 7a7 7 0 0 1 0 14M36 27c10 0 10 8 10 15"/>',
 'warning':'<path d="M24 3L46 43H2ZM24 16v13"/><circle cx="24" cy="35" r="1.8"/>',
 'settings':'<path d="M8 8h32M8 24h32M8 40h32"/><rect x="15" y="3" width="6" height="10" rx="2"/><rect x="29" y="19" width="6" height="10" rx="2"/><rect x="17" y="35" width="6" height="10" rx="2"/>',
 'scale':'<path d="M24 4v38M12 44h24M8 12h32M10 12L3 28h14ZM38 12l-7 16h14Z"/>',
}

def prepare_art() -> None:
    art=ROOT/'assets/art'; art.mkdir(parents=True,exist_ok=True)
    # The original user image is retained separately, not flattened with the UI.
    original=ROOT/'references/world_reference.png'
    if not original.exists():
        original.parent.mkdir(exist_ok=True)
        src=Path('/mnt/data/image.png')
        if src.exists(): shutil.copy2(src,original)
    if not (art/'world.jpg').exists():
        im=Image.open(original).convert('RGB'); w,h=im.size
        ch=int(w*9/16); top=max(0,int((h-ch)*.45))
        im.crop((0,top,w,top+ch)).resize((1920,1080),Image.Resampling.LANCZOS).save(art/'world.jpg',quality=93)
    concept=ROOT/'references/concept_board.png'
    if not concept.exists():
        src=Path('/mnt/data/a_large_collage_of_game_ui_mockups_in_a_clean_col.png')
        if src.exists(): shutil.copy2(src,concept)
    if not concept.exists(): return
    im=Image.open(concept).convert('RGBA')
    # Illustration placeholders cropped from the previously generated concept.
    crops={'hero_portrait':(238,448,313,526),'echo_portrait':(222,770,300,845),'barton_portrait':(1100,764,1203,859),'echo_art':(206,763,361,978),'barton_art':(1070,760,1252,979)}
    for name,box in crops.items():
        if (art/(name+'.png')).exists():
            continue
        crop=im.crop(box)
        if name == 'echo_art':
            # Exclude adjacent labels from the flattened concept. This remains
            # a raster placeholder; only its alpha silhouette is reconstructed.
            pts=[(8,154),(10,134),(20,112),(15,109),(18,95),(7,87),(10,72),(20,64),(14,62),(10,55),(17,45),(33,36),(43,17),(45,7),(50,14),(63,10),(73,10),(80,5),(84,6),(86,19),(91,31),(93,50),(94,61),(101,80),(108,90),(108,99),(106,103),(113,108),(115,115),(111,123),(117,142),(127,146),(127,151),(145,156),(151,160),(146,180),(142,204),(139,215),(57,215),(56,205),(61,195),(67,172),(72,161),(49,160),(36,155),(24,157),(0,157),(0,151)]
            mask=Image.new('L',crop.size,0);ImageDraw.Draw(mask).polygon(pts,fill=255)
            mask=mask.filter(ImageFilter.GaussianBlur(.3));crop.putalpha(mask)
        crop.save(art/(name+'.png'))
    hero=im.crop((205,442,372,713))
    # Editable UI does not depend on this low-resolution concept-art cutout.
    pts=[(241,18),(245,35),(280,25),(278,47),(302,55),(287,77),(305,106),(296,153),(280,173),(264,197),(259,226),(282,245),(278,270),(308,326),(312,359),(327,439),(342,467),(364,477),(355,501),(446,655),(459,688),(432,680),(340,557),(330,550),(284,600),(287,662),(320,721),(330,747),(304,762),(218,749),(213,718),(219,676),(207,641),(192,625),(189,561),(194,521),(173,552),(174,591),(164,622),(144,660),(146,711),(155,750),(146,781),(106,791),(74,781),(73,755),(84,719),(85,678),(81,657),(77,632),(99,599),(87,566),(74,547),(62,521),(49,506),(54,473),(31,487),(16,480),(30,432),(18,432),(40,389),(53,356),(67,339),(84,311),(102,282),(101,263),(117,239),(153,219),(149,208),(126,192),(121,181),(115,166),(128,156),(113,155),(110,141),(107,133),(116,112),(98,108),(123,84),(139,70),(145,49),(160,29),(171,49),(188,36),(219,23)]
    mask=Image.new('L',hero.size,0); ImageDraw.Draw(mask).polygon([(x/3,y/3) for x,y in pts],fill=255)
    mask=mask.filter(ImageFilter.GaussianBlur(.35)); hero.putalpha(mask)
    if not (art/'hero_cutout.png').exists(): hero.save(art/'hero_cutout.png')


def esc(s): return html.escape(str(s),quote=True)
class Canvas:
    def __init__(self,title): self.title=title; self.parts=[]; self.layer_idx=0
    def raw(self,s): self.parts.append(s)
    def layer(self,name):
        if self.layer_idx: self.raw('</g>')
        self.layer_idx+=1
        self.raw(f'<g id="layer-{self.layer_idx}" inkscape:groupmode="layer" inkscape:label="{esc(name)}">')
    def rect(self,x,y,w,h,fill,stroke='none',r=0,sw=1,opacity=1,id=None):
        self.raw(f'<rect '+(f'id="{id}" ' if id else '')+f'x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" opacity="{opacity}"/>')
    def line(self,x1,y1,x2,y2,c=None,sw=1): self.raw(f'<path d="M{x1} {y1}H{x2}" fill="none" stroke="{c or C["line"]}" stroke-width="{sw}"/>' if y1==y2 else f'<path d="M{x1} {y1}L{x2} {y2}" fill="none" stroke="{c or C["line"]}" stroke-width="{sw}"/>')
    def text(self,x,y,s,size=22,c=None,weight=400,anchor='start',serif=False,id=None):
        self.raw(f'<text '+(f'id="{id}" ' if id else '')+f'x="{x}" y="{y}" fill="{c or C["ink"]}" font-family="{HEAD if serif else BODY}" font-size="{size}" font-weight="{weight}" text-anchor="{anchor}">{esc(s)}</text>')
    def para(self,x,y,lines,size=22,c=None,gap=33):
        for i,l in enumerate(lines): self.text(x,y+i*gap,l,size,c)
    def icon(self,name,x,y,size=32,color=None):
        self.raw(f'<g transform="translate({x} {y}) scale({size/48})" fill="none" stroke="{color or C["ink"]}" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">{ICONS[name]}</g>')
    def image(self,name,x,y,w,h,clip=None,opacity=1):
        self.raw(f'<image href="@ART/{name}" xlink:href="@ART/{name}" x="{x}" y="{y}" width="{w}" height="{h}" preserveAspectRatio="xMidYMid slice" opacity="{opacity}"'+(f' clip-path="url(#{clip})"' if clip else '')+'/>')
    def action(self,action,label='',id=None): self.raw(f'<g class="interactive" data-action="{esc(action)}" role="button" tabindex="0" aria-label="{esc(label or action)}"'+(f' id="{id}"' if id else '')+'>')
    def end(self): self.raw('</g>')
    def button(self,x,y,w,label,style='primary',icon=None,action=None,h=54):
        if action:self.action(action,label)
        fill={'primary':'url(#buttonBlue)','secondary':C['light'],'gold':'url(#buttonGold)','ghost':'none','disabled':'#DDD6C6','danger':C['red'],'hover':C['navy2'],'pressed':C['dark']}[style]
        fg=C['white'] if style in ['primary','danger','hover','pressed'] else C['ink']
        self.rect(x,y,w,h,fill,C['line'] if style not in ['primary','danger'] else '#163449',9)
        if icon:self.icon(icon,x+16,y+(h-25)/2,25,fg)
        self.text(x+(w+24)/2 if icon else x+w/2,y+h/2+7,label,20,fg,600,'middle')
        if action:self.end()
    def badge(self,x,y,label,kind='paper',w=None):
        w=w or len(label)*11+28
        bg,fg={'paper':(C['paper2'],C['ink']),'gold':(C['goldLight'],'#604A28'),'green':('#DCE8CB','#365D40'),'blue':('#D7E4E8',C['navy']),'red':('#F0D8CF',C['red']),'dark':(C['dark'],C['white']),'purple':('#E5DCED','#66507F')}[kind]
        self.rect(x,y,w,32,bg,'none',5); self.text(x+14,y+22,label,17,fg,600)
    def panel(self,x,y,w,h,title=None,dark=False):
        self.rect(x+1,y+5,w,h,'#091E28','none',13,opacity=.20)
        self.rect(x,y,w,h,'url(#darkPanel)' if dark else 'url(#paper)',C['gold'] if not dark else '#C7B48B',13,1.2)
        self.rect(x+5,y+5,w-10,h-10,'none','#D6C39D' if not dark else '#54717B',9,.7,opacity=.65)
        if title:self.text(x+26,y+43,title,28,C['white'] if dark else C['ink'],serif=True)
    def bg(self,dim=.25):
        self.layer('01 — Мир / растровый референс'); self.image('world.jpg',0,0,1920,1080); self.rect(0,0,1920,1080,C['dark'],opacity=dim)
    def shell(self,title,subtitle,active=''):
        self.bg(.53); self.layer('02 — Окно и навигация'); self.panel(44,54,1832,976)
        self.rect(50,60,1820,114,'url(#paperTop)',r=8)
        self.icon({'character':'person','inventory':'bag','echoes':'star','guild':'guild','map':'map','journal':'book'}.get(active,'star'),80,86,45,C['gold'])
        self.text(148,107,title,39,serif=True); self.text(150,141,subtitle,18,C['muted'])
        self.button(1778,85,54,'×','secondary',action='screen:hud',h=50)
        self.line(64,174,1856,174)
        self.line(64,971,1856,971)
        self.text(84,1008,'Esc  Закрыть',18,C['muted']); self.text(1834,1008,'Онлайн-мир продолжает жить',18,C['muted'],anchor='end')
    def side_nav(self,items,active,x=74,y=219,w=236):
        for i,(key,label,ic) in enumerate(items):
            yy=y+i*63
            self.action('screen:'+key,label)
            self.rect(x,yy,w,50,'url(#buttonBlue)' if key==active else 'none',r=8)
            self.icon(ic,x+15,yy+12,27,C['white'] if key==active else C['muted'])
            self.text(x+55,yy+32,label,21,C['white'] if key==active else C['ink'],600 if key==active else 400)
            self.end()
    def stat(self,x,y,w,label,value,accent=None):
        self.text(x,y,label,21,C['muted']); self.text(x+w,y,value,22,accent or C['ink'],600,'end'); self.line(x,y+17,x+w,y+17,'#DBCEB5')
    def finish(self,embed=True):
        body=''.join(self.parts)+('</g>' if self.layer_idx else '')
        if embed:
            for f in (ROOT/'assets/art').iterdir():
                mime='image/jpeg' if f.suffix=='.jpg' else 'image/png'
                body=body.replace('@ART/'+f.name,'data:'+mime+';base64,'+base64.b64encode(f.read_bytes()).decode())
        else:body=body.replace('@ART/','../assets/art/')
        defs='''<defs>
<linearGradient id="paper" x2="1" y2="1"><stop stop-color="#FFF7E4"/><stop offset="1" stop-color="#EDE0C4"/></linearGradient>
<linearGradient id="paperTop" x2="1" y2="0"><stop stop-color="#FFF9EA"/><stop offset="1" stop-color="#EEDDBB"/></linearGradient>
<linearGradient id="buttonBlue" x2="0" y2="1"><stop stop-color="#426982"/><stop offset="1" stop-color="#26485D"/></linearGradient>
<linearGradient id="buttonGold" x2="0" y2="1"><stop stop-color="#F9E5B2"/><stop offset="1" stop-color="#D9B472"/></linearGradient>
<linearGradient id="darkPanel" x2="0" y2="1"><stop stop-color="#213B4B" stop-opacity=".92"/><stop offset="1" stop-color="#203744" stop-opacity=".86"/></linearGradient>
<radialGradient id="portraitBg"><stop stop-color="#D5E6CF"/><stop offset="1" stop-color="#ADBFAA"/></radialGradient>
<clipPath id="portrait-round"><circle cx="96" cy="96" r="92"/></clipPath>
<clipPath id="mapClip"><rect x="76" y="203" width="1270" height="738" rx="12"/></clipPath>
</defs>'''
        defs=defs.replace('#FFF7E4',C['light']).replace('#EDE0C4',C['paper']).replace('#426982',C['navy2']).replace('#26485D',C['navy']).replace('#D9B472',C['gold']).replace('#F9E5B2',C['goldLight'])
        return f'''<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" width="1920" height="1080" viewBox="0 0 1920 1080" role="img" aria-label="{esc(self.title)}"><title>{esc(self.title)}</title><desc>Project G — editable UI source. Text, panels and icons are separate objects. Illustrations are raster concept placeholders.</desc>{defs}{body}</svg>'''

def portrait(c,name,x,y,size=70,ring=None):
    cid=f'p{x}-{y}'.replace('.','_'); c.raw(f'<defs><clipPath id="{cid}"><circle cx="{x+size/2}" cy="{y+size/2}" r="{size/2-4}"/></clipPath></defs>')
    c.raw(f'<circle cx="{x+size/2}" cy="{y+size/2}" r="{size/2}" fill="{C["navy"]}" stroke="{ring or C["goldLight"]}" stroke-width="2"/>')
    c.image(name+'.png',x+3,y+3,size-6,size-6,cid)

def skill(c,x,y,icon,key,idx,cd=0):
    c.action('skill:'+str(idx),'Навык '+key)
    c.rect(x,y,76,76,'url(#buttonBlue)',C['line'],10,1.5)
    c.rect(x+5,y+5,66,66,'none','#758D90',6,.5)
    c.icon(icon,x+16,y+12,45,C['goldLight'] if idx>3 else '#C9F0FF')
    if cd:
        c.rect(x+2,y+2,72,72,C['dark'],r=8,opacity=.64); c.text(x+38,y+47,str(cd),28,C['white'],600,'middle')
    c.rect(x+23,y+60,30,24,C['paper'],C['line'],4)
    c.text(x+38,y+77,key,16,C['ink'],600,'middle'); c.end()

def hud():
    c=Canvas('01 — Игровой HUD / исследование'); c.bg(.025)
    c.layer('02 — Миникарта и регион')
    c.raw('<circle cx="140" cy="136" r="93" fill="#203744" stroke="#E6CA90" stroke-width="3"/><circle cx="140" cy="136" r="85" fill="#B5C28F"/>')
    c.raw('<path d="M82 72C132 92 102 134 166 154S195 194 193 210" stroke="#72B0C2" stroke-width="25" fill="none"/><path d="M73 170L140 133 203 102M140 133L158 202" stroke="#EFDBA2" stroke-width="10" fill="none"/>')
    for xx,yy in [(117,106),(163,106),(92,173),(192,134)]:c.rect(xx,yy,16,13,'#9D784C','#574D3E',2)
    c.raw('<path d="M140 122l-9 25 9-6 9 6Z" fill="#FFF9EA" stroke="#4A614F" stroke-width="2"/>')
    c.text(140,39,'N',18,C['white'],600,'middle'); c.text(252,90,'Ривермут',32,C['white'],600,serif=True)
    c.icon('sun',252,111,24,C['goldLight']); c.text(289,130,'10:24 · Южные ворота',20,C['white'])
    c.badge(252,153,'Город · безопасная зона','dark',274)
    c.action('pvp','Правила PvP-тега');c.badge(252,196,'PvP-тег выключен','dark',221);c.end()
    c.layer('03 — Навигация')
    for i,(name,label,key,ic) in enumerate([('character','Персонаж','C','person'),('inventory','Инвентарь','B','bag'),('echoes','Эхо','N','star'),('journal','Журнал','J','book'),('map','Карта','M','map')]):
        x=1380+i*103;c.action('screen:'+name,label);c.rect(x,38,89,94,'#233F4B','none',12,opacity=.88);c.icon(ic,x+28,50,34,C['paper']);c.text(x+45,108,label,15,C['white'],anchor='middle');c.text(x+70,60,key,12,C['goldLight']);c.end()
    c.layer('04 — Контекст и группа')
    c.panel(42,269,386,131,dark=True);c.icon('scroll',63,292,27,C['goldLight']);c.text(105,315,'Лесная дорога',24,C['white'],600)
    c.para(68,352,['Передать материалы смотрителю.','Точное место пока неизвестно.'],18,C['paper'],gap=27)
    c.text(50,447,'ГРУППА · 3 / 6',16,C['white'],600)
    for i,(name,hp) in enumerate([('Рен',.84),('Лира',1),('Марк',.72)]):
        yy=467+i*59;c.rect(47,yy,243,49,C['dark'],'none',6,opacity=.81);c.icon('person',60,yy+10,26,C['paper']);c.text(104,yy+21,name,18,C['white']);c.rect(104,yy+30,164,7,'#52605F',r=3);c.rect(104,yy+30,164*hp,7,'#84AF72',r=3)
    c.layer('05 — Персонаж / демонстрационный арт')
    c.raw('<ellipse cx="984" cy="760" rx="44" ry="13" fill="#153B38" opacity=".25"/>')
    c.image('hero_cutout.png',938,589,91,174)
    c.text(982,578,'Каэль',19,C['white'],600,'middle')
    c.layer('06 — Эхо: три спутника, не смена героя')
    for i,(name,art,status) in enumerate([('Сиэль','echo_portrait','Готова'),('Каэн','hero_portrait','Готов'),('Торен','barton_portrait','12 с')]):
        y=279+i*123;c.action('echo:'+str(i),'Эхо '+name);c.rect(1698,y,180,92,C['dark'],'none',11,opacity=.87);portrait(c,art,1784,y+4,85);c.text(1713,y+26,name,19,C['white'],600);c.text(1713,y+51,status,15,C['goldLight']);c.rect(1713,y+67,51,17,C['paper'],r=3);c.text(1738,y+80,str(i+1),13,C['ink'],600,'middle');c.end()
    c.layer('07 — Чат')
    c.panel(39,816,434,210,dark=True)
    for i,label in enumerate(['Общий','Мир','Группа','Гильдия']):c.text(64+i*98,850,label,17,C['goldLight'] if i==2 else C['dim'])
    c.line(59,863,451,863,'#617980')
    c.para(62,891,['[Группа] Лира: встречаемся у моста.','[Группа] Марк: припасы уже у меня.','[Общий] Рен: ищу проводника в лес.'],17,C['paper'],gap=28)
    c.action('chat','Написать в чат');c.rect(58,972,395,37,'#163340','#57717A',5);c.text(74,997,'Enter  Написать сообщение…',16,C['dim']);c.end()
    c.layer('08 — Ресурсы и восемь активных навыков')
    c.rect(586,842,812,187,C['dark'],'none',15,opacity=.72)
    c.text(614,872,'Каэль · Ур. 24',18,C['white'],600);c.text(1370,872,'Мечник',18,C['goldLight'],anchor='end')
    for y,val,label,col in [(884,.87,'2 480 / 2 850', '#81AB68'),(912,.8,'120 / 150','#63A6C0')]:
        c.rect(613,y,757,19,'#20383F',C['line'],8);c.rect(615,y+2,753*val,15,col,r=6);c.text(992,y+15,label,14,C['white'],600,'middle')
    for i,(ic,key) in enumerate(zip(['sword','bolt','snow','shield','flame','bow','star','dash'],'QWERASDF')):skill(c,613+i*94+(8 if i>3 else 0),944,ic,key,i,8 if i==2 else 0)
    c.text(992,1060,'ЛКМ — атака    ПКМ — движение    Shift — парирование    Tab — блок    Space — уклонение',17,C['white'],anchor='middle')
    c.rect(0,1074,1920,6,'#324F54');c.rect(0,1074,782,6,C['goldLight'])
    c.layer('09 — Системный статус');c.text(1875,1043,'42 ms · Demo',15,C['white'],anchor='end')
    return c

NAV=[('character','Персонаж','person'),('inventory','Инвентарь','bag'),('echoes','Эхо Прошлого','star'),('guild','Гильдия','guild'),('journal','Журнал','book'),('map','Карта мира','map')]

def character():
    c=Canvas('02 — Персонаж / экипировка');c.shell('Персонаж','Каэль · Мечник · Уровень 24','character');c.side_nav(NAV,'character')
    c.layer('03 — Герой и слоты экипировки');c.rect(340,201,620,735,'url(#portraitBg)',C['line'],12)
    c.raw('<circle cx="653" cy="524" r="226" fill="none" stroke="#B2AA7D" stroke-width="1"/><circle cx="653" cy="524" r="206" fill="none" stroke="#DBDBB7" stroke-width="1"/>')
    c.text(650,255,'КАЭЛЬ',21,C['navy'],600,'middle');c.image('hero_cutout.png',485,304,342,576)
    for i,ic in enumerate(['helmet','shield','sword','boot']):
        for j in [0,1]:
            xx=371+j*482;yy=322+i*128;c.action('screen:inventory','Изменить экипировку');c.rect(xx,yy,70,77,'url(#buttonBlue)',C['gold'],9);c.icon(ic if not j else ['star','bag','gem','leaf'][i],xx+13,yy+12,44,C['goldLight']);c.end()
    c.text(650,917,'Иллюстрация-заготовка · заменить 3D-моделью',15,C['muted'],anchor='middle')
    c.layer('04 — Характеристики');c.text(1000,241,'Характеристики',33,serif=True);c.badge(1615,212,'Ранг F','gold',117)
    c.text(1000,286,'БАЗОВЫЕ',16,C['gold'],600)
    for i,(k,v) in enumerate([('Сила','42'),('Ловкость','31'),('Живучесть','26'),('Интеллект','12'),('Сноровка','35'),('Удача','9')]):
        x=1000+(i%2)*402;y=331+(i//2)*61;c.stat(x,y,334,k,v)
    c.button(1000,496,353,'Распределить 3 очка','gold',action='stat-points');c.text(1376,531,'Неотрицательные значения',16,C['muted'])
    c.text(1000,604,'Производные параметры',30,serif=True)
    for i,(k,v) in enumerate([('Здоровье','2 850'),('Урон оружия','148'),('Защита','−12'),('Скорость атаки','1,18 / с')]): c.stat(1000,656+i*54,737,k,v,C['red'] if k=='Защита' else None)
    c.text(1000,889,'Отрицательная защита повышает уязвимость.',19,C['red']);c.button(1000,906,737,'Источники модификаторов','secondary',action='modifiers',h=46)
    return c

ITEMS=[('sword','Дорожный клинок +3','weapon','Авторская работа',148,-12),('sword','Клинок стражи +2','weapon','Обычный',136,4),('shield','Щит дозора','armor','Обычный',0,22),('helmet','Шлем странника','armor','Обычный',0,8),('leaf','Лесная трава','material','Материал',0,0),('potion','Малое зелье','supply','Расходуемый',0,0),('gem','Осколок памяти','material','Материал',0,0),('boot','Сапоги следопыта','armor','Обычный',0,3),('book','Полевой дневник','other','Запись',0,0),('star','Свет Старого пути','weapon','Легендарный',142,0),('coin','Медный знак','other','Трофей',0,0),('scroll','Карта переправы','other','Карта',0,0)]

def inventory():
    c=Canvas('03 — Инвентарь / сравнение предметов');c.shell('Инвентарь','Рюкзак и снаряжение · 12 / 60 ячеек','inventory');c.side_nav(NAV,'inventory')
    c.layer('03 — Поиск и сетка');c.action('search','Поиск предмета');c.rect(340,208,688,53,C['light'],C['line'],7);c.icon('search',357,221,26,C['muted']);c.text(399,242,'Поиск по названию…',20,C['muted']);c.end()
    for i,(key,label) in enumerate([('all','Все'),('weapon','Оружие'),('armor','Броня'),('material','Материалы')]):c.button(340+i*174,280,163,label,'gold' if i==0 else 'secondary',action='filter:'+key,h=44)
    for i in range(30):
        x=340+(i%6)*116;y=346+(i//6)*105
        if i<len(ITEMS):c.action('item:'+str(i),ITEMS[i][1],id='item-'+str(i))
        c.rect(x,y,102,92,'#E1D3B8' if i>=len(ITEMS) else '#F9F0DF',C['gold'] if i==0 else '#C6B28A',9,2 if i==0 else 1,opacity=.6 if i>=len(ITEMS) else 1)
        if i<len(ITEMS):
            c.icon(ITEMS[i][0],x+25,y+16,51,C['purple'] if i==9 else C['navy']);c.rect(x+6,y+84,90,3,C['gold'] if i==9 else '#86A196',r=1);c.text(x+86,y+78,str([1,1,1,1,27,4,3,1,1,1,7,1][i]),14,C['muted'],anchor='end');c.end()
    c.text(346,921,'ЛКМ — сведения · кнопка ниже — сравнение',18,C['muted'])
    c.layer('04 — Предмет и его история');c.panel(1074,208,760,742)
    c.rect(1100,233,107,107,'url(#buttonBlue)',C['gold'],10);c.raw('<g id="item-art">');c.icon('sword',1122,251,68,C['goldLight']);c.end()
    c.text(1232,261,'МЕЧ · АВТОРСКАЯ РАБОТА',15,C['gold'],600,id='item-category');c.text(1232,305,'Дорожный клинок +3',30,serif=True,id='item-title');c.text(1232,336,'Работа мастера Талена',19,C['muted'],id='item-origin')
    c.line(1100,364,1806,364)
    c.para(1100,401,['Лёгкий клинок для долгих путешествий.','Цена точного удара — меньшая защита.'],22)
    c.stat(1100,510,700,'Урон','148');c.stat(1100,571,700,'Модификатор защиты','−12',C['red']);c.stat(1100,632,700,'Прочность','68 / 80')
    c.rect(1100,662,707,99,'#EDDDC3',r=7);c.icon('warning',1115,680,26,C['red']);c.para(1161,691,['Не легендарный предмет. Может выпасть','при PvP-смерти с включённым тегом.'],19,C['ink'],gap=30)
    c.button(1100,782,345,'Сравнить с надетым','secondary',action='compare');c.button(1462,782,345,'Экипировать','primary',action='equip')
    c.button(1100,851,707,'Защитить от случайной продажи','secondary',icon='lock',action='lock-item')
    c.text(1100,933,'Защита от продажи не меняет правила PvP-дропа.',17,C['muted'])
    return c

def echoes():
    c=Canvas('04 — Эхо Прошлого / личность');c.shell('Эхо Прошлого','Личности, воспоминания и совместные приключения','echoes')
    c.layer('03 — Коллекция известных Эхо');
    for i,(name,art) in enumerate([('Сиэль','echo_portrait'),('Каэн','hero_portrait'),('Торен','barton_portrait')]):
        y=229+i*150;c.action('choose-echo:'+str(i),name);c.rect(76,y,195,125,'url(#buttonBlue)' if i==0 else '#E7DABF',C['line'],10);portrait(c,art,128,y+9,74);c.text(172,y+110,name,21,C['white'] if i==0 else C['ink'],anchor='middle');c.end()
    c.text(172,759,'Известные Эхо',17,C['muted'],anchor='middle');c.text(172,791,'Призвано: 2 / 3',21,C['ink'],600,'middle')
    c.layer('04 — Иллюстрация');c.rect(301,202,643,735,'url(#portraitBg)',C['line'],11)
    c.image('echo_art.png',426,264,361,544)
    c.rect(326,801,593,116,'url(#paper)',r=8);c.para(357,838,['«Даже в самых обычных днях','есть истории, достойные памяти».'],24,C['navy'],gap=35)
    c.text(623,930,'Концепт-арт · растровая заготовка',14,C['muted'],anchor='middle')
    c.layer('05 — Личность, способности и связь');c.text(985,252,'Сиэль',44,serif=True);c.text(985,294,'Хранительница дорог · Эхо Старого королевства',22,C['muted'])
    for i in range(5):c.icon('star',987+i*29,315,22,C['gold'])
    c.badge(1550,218,'Ур. 12','blue',93);c.badge(1660,218,'В отряде','green',150)
    for i,(key,label) in enumerate([('overview','Обзор'),('skills','Навыки'),('memory','Память'),('relation','Связь')]):c.button(985+i*212,375,199,label,'gold' if i==0 else 'secondary',action='echo-tab:'+key,h=45)
    c.text(985,478,'Знает забытые дороги',30,serif=True);c.para(985,521,['Узнаёт следы старых трактов и символы своей эпохи.','Помогает понять находки, но не отмечает тайны на карте.'],21,gap=33)
    c.stat(985,633,824,'Отношение','Доверяет',C['green']);c.stat(985,692,824,'Состояние','Готова помочь',C['green']);c.stat(985,751,824,'Способность','Путеводная звезда')
    c.button(985,807,394,'Поговорить','gold',icon='chat',action='talk-echo');c.button(1396,807,414,'Отозвать Эхо','secondary',action='summon')
    c.text(985,902,'1 / 2 / 3 — команды призванным Эхо, не смена героя.',19,C['muted'])
    return c

QUESTS=[('Лесная дорога','F','Доставка и проверка сведений','Смотритель дороги'),('Лекарства для лечебницы','F','Открытое поручение · сбор','Городская лечебница'),('Пропавший караван','E','Поиск · предварительные сведения','Торговое товарищество'),('Следы у старой мельницы','D','Разведка · неизвестная угроза','Жители пригородов')]

def guild():
    c=Canvas('05 — Гильдия Авантюристов / поручения');c.shell('Гильдия Авантюристов','Отделение Ривермута · городские поручения','guild')
    c.layer('03 — Удостоверение');c.panel(77,206,268,295);c.icon('guild',180,233,60,C['gold']);c.text(211,360,'F',60,C['navy'],600,'middle',serif=True);c.text(211,402,'Авантюрист Каэль',22,C['ink'],600,'middle');c.text(211,433,'Ранг признаётся везде',16,C['muted'],anchor='middle');c.line(99,452,322,452);c.text(211,477,'В отделении: новичок',17,C['muted'],anchor='middle')
    for i,(label,action) in enumerate([('Доска поручений','screen:guild'),('Мои контракты','screen:journal'),('Послужной список','service-record'),('Повышение ранга','rank')]): c.button(78,531+i*62,267,label,'gold' if i==0 else 'secondary',action=action,h=47)
    c.text(79,906,'Членство добровольное.',17,C['muted']);c.text(79,934,'Не заменяет гильдию игроков.',17,C['muted'])
    c.layer('04 — Доска');c.text(379,239,'Доступные поручения',29,serif=True)
    for i,(title,rank,kind,source) in enumerate(QUESTS):
        y=275+i*146;c.action('quest:'+str(i),title);c.rect(377,y,655,128,'#FFF8E9' if i==0 else '#EDDFC3',C['gold'] if i==0 else C['line'],9,2 if i==0 else 1);c.badge(397,y+18,'Ранг '+rank,'gold',92);c.text(514,y+42,title,24,weight=600);c.text(398,y+79,kind,19,C['muted']);c.text(398,y+109,source,17,C['muted']);c.end()
    c.text(378,906,'Доступность и награды — демонстрационные данные.',17,C['muted'])
    c.layer('05 — Контракт');c.panel(1063,207,769,730)
    c.text(1097,256,'Лесная дорога',34,serif=True);c.badge(1097,282,'Ранг F','gold',101);c.badge(1214,282,'Сведения предварительные','blue',303)
    c.para(1097,363,['Смотритель давно не присылал отчётов.','Доставьте материалы и выясните, что случилось.'],23,gap=36)
    c.text(1097,463,'Что известно',24,weight=600);c.para(1097,505,['Последнее сообщение пришло со старой лесной дороги.','Точное местонахождение смотрителя не подтверждено.'],20,C['muted'],gap=31)
    c.text(1097,603,'Условия',24,weight=600);c.para(1097,642,['Оплата за доставку и подтверждённый отчёт.','Обнаружив неизвестную угрозу, сообщите в отделение.'],20,gap=31)
    c.line(1097,704,1798,704);c.text(1097,744,'Награда',22,weight=600);c.icon('coin',1099,765,40,C['gold']);c.text(1151,794,'180 монет',24);c.icon('scroll',1380,765,39,C['navy']);c.text(1433,794,'Запись в реестре',22)
    c.button(1097,841,449,'Принять поручение','primary',action='accept-quest');c.button(1563,841,236,'На карте','secondary',action='screen:map')
    return c


def draw_map(c):
    c.rect(76,203,1270,738,'#D9DBC1',C['line'],12)
    c.raw('<g clip-path="url(#mapClip)">')
    c.raw('<path d="M140 201L171 283 148 379 207 449 161 522 191 608 254 671 253 784 323 845 318 942H522L481 804 409 737 393 663 348 614 344 536 319 467 314 361 251 290 267 202Z" fill="#94BEC7"/><path d="M76 614C254 549 301 611 388 633s214 19 346-26 231-5 271 38" fill="none" stroke="#94BEC7" stroke-width="20"/>')
    c.raw('<path d="M229 736L386 611 620 506 895 497 1097 359M620 506L645 315M620 506L830 768" fill="none" stroke="#B9A47B" stroke-width="13"/><path d="M229 736L386 611 620 506 895 497 1097 359M620 506L645 315M620 506L830 768" fill="none" stroke="#FAEFCD" stroke-width="7"/>')
    # Decorative forest clusters; no game secrets are embedded in this map.
    for i in range(64):
        x=395+(i*71%784);y=237+(i*137%648)
        if (500<x<734 and 390<y<570):continue
        c.raw(f'<path d="M{x} {y}l-13 24h26ZM{x} {y+12}l-19 26h38Z" fill="{["#819977","#97A386","#698F77"][i%3]}" stroke="#6F8569" stroke-width=".7"/>')
    for x,y in [(954,290),(1003,319),(1032,254),(1130,742),(1200,794)]: c.raw(f'<path d="M{x} {y}l-48 69h96Z" fill="#9FA294"/><path d="M{x} {y}l-14 25 15-8 17 11Z" fill="#F3EFDB"/>')
    c.raw('<path d="M945 204C863 360 935 489 850 601s-7 179-12 239l-65 101h574V204Z" fill="#E8DDC4" opacity=".95"/><path d="M930 204C848 360 920 489 835 601s-7 179-12 239l-65 101" fill="none" stroke="#C1B99F" stroke-width="2" stroke-dasharray="6 8"/>')
    c.text(1070,677,'НЕИЗУЧЕННАЯ',22,'#9B947E',600,'middle');c.text(1070,710,'ТЕРРИТОРИЯ',22,'#9B947E',600,'middle')
    c.raw('<g id="settlement-layer">')
    for x,y in [(571,500),(597,511),(620,470),(647,505)]:c.rect(x,y,28,24,'#B68E5B','#765F45',3);c.raw(f'<path d="M{x-3} {y}l17-13 18 13Z" fill="#916B43"/>')
    c.text(618,558,'Ривермут',26,C['ink'],600,'middle',serif=True);c.end();c.text(309,760,'Переправа',21,C['ink'],serif=True);c.text(645,305,'Северная дорога',20,C['ink'],serif=True)
    c.raw('<g id="rumor-layer"><ellipse cx="794" cy="422" rx="92" ry="61" fill="#B39556" opacity=".17" stroke="#806640" stroke-width="2" stroke-dasharray="6 7" id="rumor-area"/>')
    c.text(782,428,'?',30,C['gold'],600,'middle');c.text(736,383,'По рассказам жителей',17,C['muted']);c.end()
    c.raw('<path d="M644 522l-11 30 11-7 11 7Z" fill="#FFF9EA" stroke="#375A66" stroke-width="2"/>')
    c.end()
    c.icon('compass',113,237,98,C['navy']);c.text(163,242,'N',18,C['navy'],600,'middle');c.text(103,915,'Ваше положение: 124, 352',18,C['muted']);c.text(1298,915,'Известные вам сведения',18,C['muted'],anchor='end')

def map_screen():
    c=Canvas('06 — Карта / исследование и личные отметки');c.shell('Карта мира','Регион Ривермута · только известные персонажу сведения','map');c.layer('03 — Редактируемая векторная карта');draw_map(c)
    c.layer('04 — Легенда и сведения');c.text(1390,250,'Ривермут',35,serif=True);c.badge(1390,275,'Торговый город','gold',204)
    c.para(1390,351,['Город у переправы. Отсюда начинаются','тракт к старой мельнице и лесные дороги.'],19,gap=30)
    c.text(1390,453,'Показывать',26,serif=True)
    for i,(label,ic) in enumerate([('Известные поселения','guild'),('Личные отметки','pin'),('Сведения и слухи','chat')]):
        y=482+i*59;c.action('map-layer:'+str(i),label);c.rect(1390,y,25,25,C['navy'],r=4);c.icon('check',1393,y+3,19,C['white']);c.icon(ic,1431,y-1,27,C['muted']);c.text(1473,y+21,label,21);c.end()
    c.line(1390,684,1798,684);c.para(1390,735,['Слух обозначает примерную область,','а не точную цель или найденный секрет.'],20,C['muted'],gap=31)
    c.button(1390,826,407,'Добавить личную отметку','primary',icon='pin',action='add-pin');c.text(1390,924,'Путешествие — пешком или транспортом.',18,C['muted'])
    return c

def journal():
    c=Canvas('07 — Журнал / поручения, сведения и источники');c.shell('Журнал путешествия','Поручения · свидетельства · личные открытия','journal')
    c.layer('03 — Список')
    for i,(key,label,ic) in enumerate([('current','Текущие','scroll'),('contracts','Контракты','guild'),('rumors','Слухи','chat'),('discoveries','Открытия','compass')]):
        y=219+i*63;c.action('journal-category:'+key,label);c.rect(74,y,236,50,'url(#buttonBlue)' if i==0 else 'none',r=8);c.icon(ic,89,y+12,27,C['white'] if i==0 else C['muted']);c.text(129,y+32,label,21,C['white'] if i==0 else C['ink']);c.end()
    for i,(title,sub) in enumerate([('Лесная дорога','Контракт Гильдии · F'),('Огни за мельницей','Слух · со слов торговца'),('Старая переправа','Личная запись · подтверждено')]):
        y=214+i*158;c.action('journal-entry:'+str(i),title);c.rect(341,y,505,138,C['light'] if i==0 else '#E7D9BD',C['gold'] if i==0 else C['line'],9,2 if i==0 else 1);c.icon('scroll' if i==0 else 'chat' if i==1 else 'map',362,y+22,32,C['gold']);c.text(413,y+46,title,26,serif=True);c.text(363,y+93,sub,20,C['muted']);c.end()
    c.layer('04 — Страница поручения');c.panel(882,204,950,736);c.badge(918,230,'КОНТРАКТ · РАНГ F','gold',228);c.text(918,321,'Лесная дорога',41,serif=True)
    c.para(918,372,['Доставить материалы смотрителю и выяснить,','почему с лесной дороги больше нет отчётов.'],24,gap=37)
    c.text(918,492,'Задача',25,weight=600);c.rect(920,518,22,22,'none',C['muted'],3);c.text(958,539,'Передать материалы и получить сведения.',22)
    c.text(918,621,'Источник информации',25,weight=600);c.para(918,664,['Регистратор отделения Ривермута.','Точное местонахождение смотрителя не подтверждено.'],22,C['muted'],gap=34)
    c.line(918,729,1796,729);c.text(918,779,'Отчёт о новой опасности может быть полезным результатом.',20,C['muted'])
    c.button(918,834,423,'Закрепить в HUD','primary',action='pin-quest');c.button(1359,834,437,'Открыть область на карте','secondary',action='screen:map')
    return c

def dialogue():
    c=Canvas('08 — Диалог / регистрация в Гильдии');c.bg(.21)
    c.layer('02 — Контекст');c.badge(49,42,'Гильдия Авантюристов · Ривермут','dark',430);c.button(1773,41,101,'Esc','secondary',action='screen:hud',h=45)
    c.layer('03 — Иллюстрация NPC');c.image('barton_art.png',81,204,537,652);c.rect(88,793,521,67,'url(#darkPanel)',r=8);c.text(349,836,'Концепт-арт · заменить моделью NPC',17,C['paper'],anchor='middle')
    c.layer('04 — Ответы игрока')
    for i,(label,action) in enumerate([('Я хочу зарегистрироваться.','register'),('Расскажите о рангах.','rank'),('Какие поручения доступны?','screen:guild'),('Вернусь позже.','screen:hud')]): c.button(1080,436+i*77,754,label,'gold' if i==0 else 'secondary',action=action,h=62)
    c.layer('05 — Диалоговая панель');c.panel(55,786,1810,238);c.text(89,838,'Бартон',35,serif=True);c.text(90,873,'Регистратор Гильдии Авантюристов',18,C['muted']);c.line(660,811,660,989);c.para(711,858,['Добро пожаловать, путник. Здесь найдётся работа','и для опытного исследователя, и для того,','кто только начинает свой путь.'],27,gap=42);c.text(1825,1003,'Мир не остановлен',16,C['muted'],anchor='end')
    return c

def components():
    c=Canvas('09 — Компоненты / дизайн-система');c.layer('01 — Основа');c.rect(0,0,1920,1080,C['paper']);c.text(55,74,'Хроники странника',47,serif=True);c.text(57,112,'PROJECT G / UI KIT 01 — WARM FANTASY',17,C['gold'],600);c.line(55,136,1865,136)
    c.layer('02 — Палитра и типографика');c.text(57,184,'Палитра',28,serif=True)
    for i,key in enumerate(['paper','paper2','navy','gold','green','red','purple']):
        x=56+i*125;c.rect(x,205,111,59,C[key],C['line'],8);c.text(x,291,key,17);c.text(x,315,C[key],16,C['muted'])
    c.text(998,184,'Типографика',28,serif=True);c.text(998,236,'Заголовок раздела',39,serif=True);c.text(998,282,'Читаемый текст · кириллица · 22 px',23);c.text(998,318,'123 456 / −12  ·  технические данные: 18–22 px',19,C['muted'])
    c.layer('03 — Состояния кнопок');c.text(57,383,'Кнопки и состояния',28,serif=True)
    for i,(label,style) in enumerate([('Обычная','primary'),('Hover','hover'),('Нажатие','pressed'),('Недоступно','disabled')]):c.button(56+i*230,407,211,label,style)
    c.button(998,407,264,'С фокусом','primary');c.rect(993,402,274,64,'none',C['gold'],12,3);c.button(1287,407,260,'Отмена','secondary');c.button(1571,407,293,'Выполнить','gold',icon='check')
    c.text(58,500,'Фокус не только цветом: внешняя рамка 3 px. Загрузка: текст и индикатор, повторный запрос блокируется.',19,C['muted'])
    c.layer('04 — Иконки');c.text(57,562,'Векторные иконки · 48 × 48',28,serif=True)
    for i,key in enumerate(ICONS):
        x=57+(i%20)*90;y=590+(i//20)*105;c.rect(x,y,77,73,C['light'],C['line'],9);c.icon(key,x+16,y+12,46,C['navy']);c.text(x+38,y+93,key,12,C['muted'],anchor='middle')
    c.layer('05 — Семантика');c.text(57,858,'Статусы и ранг',27,serif=True)
    for i,k in enumerate('FEDCBAS'):c.badge(57+i*62,881,k,'gold',48)
    c.badge(538,881,'Тег выключен','green',186);c.badge(747,881,'Риск имущества','red',210);c.badge(980,881,'Легендарный · без PvP-дропа','purple',340)
    c.text(57,1000,'SVG: отдельные объекты / текст. Godot: Theme + Control. Веб: SVG + CSS + JavaScript. Никаких встроенных шрифтов.',20,C['muted'])
    return c

SCREENS={'hud':hud,'character':character,'inventory':inventory,'echoes':echoes,'guild':guild,'map':map_screen,'journal':journal,'dialogue':dialogue,'components':components}

def main():
    prepare_art()
    icons=ROOT/'assets/icons';icons.mkdir(parents=True,exist_ok=True)
    for key,path in ICONS.items():
        (icons/(key+'.svg')).write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 48 48"><g fill="none" stroke="{C["navy"]}" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">{path}</g></svg>')
    web={}
    for i,(name,factory) in enumerate(SCREENS.items(),1):
        canvas=factory();dest=ROOT/('design/components' if name=='components' else 'design/screens');dest.mkdir(parents=True,exist_ok=True)
        (dest/f'{i:02}_{name}.svg').write_text(canvas.finish(True),encoding='utf-8');web[name]=canvas.finish(False)
    (ROOT/'prototype/screens.js').write_text('/* Generated by src/build_design.py; edit that file to rebuild. */\nwindow.UI_SCREENS = '+json.dumps(web,ensure_ascii=False)+';\n',encoding='utf-8')
    tokens={'name':'Project G / Chronicles','version':'0.1.0','viewport':{'width':1920,'height':1080},'colors':C,'typography':{'body':BODY,'heading':HEAD,'sizes':[14,16,18,20,22,28,34,44]},'spacing':[4,8,12,16,24,32,40,48,64],'radius':{'key':4,'button':9,'panel':13},'stroke':{'default':1,'selected':2,'focus':3},'motion':{'fast_ms':100,'normal_ms':160,'reduced_ms':0}}
    (ROOT/'design/tokens.json').write_text(json.dumps(tokens,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'prototype/tokens.css').write_text('/* Generated from design/tokens.json */\n:root {\n'+''.join(f'  --{k}: {v};\n' for k,v in C.items())+'  --radius: 9px;\n  --motion-fast: 100ms;\n  --motion-normal: 160ms;\n}\n',encoding='utf-8')
    print('Generated',len(web),'SVG sheets and',len(ICONS),'icons')

if __name__=='__main__':main()
