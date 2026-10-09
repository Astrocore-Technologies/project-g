#!/usr/bin/env python3
"""Generate a small native Godot 4 UI starter, not a game client.
The eight design boards are not used as flattened runtime textures.
Native runtime scope: HUD, inventory, guild panel, shared theme/components.
"""
from pathlib import Path
import shutil,json
ROOT=Path(__file__).resolve().parents[1]; G=ROOT/'godot'
T=json.loads((ROOT/'design/tokens.json').read_text())['colors']
def color(value,alpha=1.0):
 v=value.lstrip('#');return f'Color({int(v[0:2],16)/255:.6f}, {int(v[2:4],16)/255:.6f}, {int(v[4:6],16)/255:.6f}, {alpha})'
def q(s): return json.dumps(str(s),ensure_ascii=False)
def write(path,s):
 p=G/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(s,encoding='utf-8')

def theme():
 styles={
 'Paper':('paper','line',13), 'Dark':('dark','line',13),
 'Normal':('navy','navy',9),'Hover':('navy2','goldLight',9),'Pressed':('dark','gold',9),
 'Disabled':('paper2','line',9),'Secondary':('light','line',9),'SecondaryHover':('paper2','gold',9),
 'Gold':('goldLight','gold',9),'Field':('light','line',7),'BarBg':('dark','line',7),'BarFill':('green','green',6),
 }
 out=[f'[gd_resource type="Theme" load_steps={len(styles)+4} format=3]\n']
 for name,(bg,border,r) in styles.items():
  out.append(f'[sub_resource type="StyleBoxFlat" id="Style_{name}"]\ncontent_margin_left = 16.0\ncontent_margin_top = 12.0\ncontent_margin_right = 16.0\ncontent_margin_bottom = 12.0\nbg_color = {color(T[bg],.9 if name=="Dark" else 1)}\nborder_width_left = 1\nborder_width_top = 1\nborder_width_right = 1\nborder_width_bottom = 1\nborder_color = {color(T[border])}\ncorner_radius_top_left = {r}\ncorner_radius_top_right = {r}\ncorner_radius_bottom_right = {r}\ncorner_radius_bottom_left = {r}\n')
 out.append(f'[sub_resource type="StyleBoxFlat" id="Focus"]\nbg_color = {color(T["goldLight"],0)}\ndraw_center = false\nborder_width_left = 3\nborder_width_top = 3\nborder_width_right = 3\nborder_width_bottom = 3\nborder_color = {color(T["goldLight"])}\ncorner_radius_top_left = 9\ncorner_radius_top_right = 9\ncorner_radius_bottom_right = 9\ncorner_radius_bottom_left = 9\n')
 out.append('[sub_resource type="SystemFont" id="BodyFont"]\nfont_names = PackedStringArray("Arial", "Liberation Sans", "DejaVu Sans")\n')
 out.append('[sub_resource type="SystemFont" id="HeaderFont"]\nfont_names = PackedStringArray("Georgia", "Liberation Serif", "DejaVu Serif")\n')
 out.append(f'[resource]\ndefault_font = SubResource("BodyFont")\ndefault_font_size = 20\nLabel/colors/font_color = {color(T["ink"])}\nHeader/base_type = &"Label"\nHeader/fonts/font = SubResource("HeaderFont")\nHeader/font_sizes/font_size = 34\nMuted/base_type = &"Label"\nMuted/colors/font_color = {color(T["muted"])}\nOnDark/base_type = &"Label"\nOnDark/colors/font_color = {color(T["white"])}\nOnDarkHeader/base_type = &"Label"\nOnDarkHeader/colors/font_color = {color(T["white"])}\nOnDarkHeader/fonts/font = SubResource("HeaderFont")\nOnDarkHeader/font_sizes/font_size = 30\nPanel/styles/panel = SubResource("Style_Paper")\nPanelContainer/styles/panel = SubResource("Style_Paper")\nParchment/base_type = &"PanelContainer"\nParchment/styles/panel = SubResource("Style_Paper")\nDarkPanel/base_type = &"PanelContainer"\nDarkPanel/styles/panel = SubResource("Style_Dark")\n')
 for b in ['Button','PrimaryButton','SecondaryButton','GoldButton','SkillButton']:
  if b!='Button':out.append(f'{b}/base_type = &"Button"\n')
  normal='Secondary' if b=='SecondaryButton' else 'Gold' if b=='GoldButton' else 'Normal'
  for state,s in [('normal',normal),('hover','SecondaryHover' if b in ['SecondaryButton','GoldButton'] else 'Hover'),('pressed','Pressed'),('disabled','Disabled'),('focus','Focus')]:out.append(f'{b}/styles/{state} = SubResource("{s if s=="Focus" else "Style_"+s}")\n')
  out.append(f'{b}/colors/font_color = {color(T["ink"] if b in ["SecondaryButton","GoldButton"] else T["white"])}\n{b}/colors/font_hover_color = {color(T["ink"] if b in ["SecondaryButton","GoldButton"] else T["white"])}\n{b}/colors/font_pressed_color = {color(T["white"])}\n{b}/colors/font_disabled_color = {color(T["muted"])}\n')
 out.append(f'LineEdit/styles/normal = SubResource("Style_Field")\nLineEdit/styles/focus = SubResource("Focus")\nLineEdit/colors/font_color = {color(T["ink"])}\nLineEdit/colors/caret_color = {color(T["navy"])}\nLineEdit/colors/font_placeholder_color = {color(T["muted"])}\nProgressBar/styles/background = SubResource("Style_BarBg")\nProgressBar/styles/fill = SubResource("Style_BarFill")\nHBoxContainer/constants/separation = 12\nVBoxContainer/constants/separation = 10\n')
 # load_steps includes each subresource plus the resource itself.
 out[0]=f'[gd_resource type="Theme" load_steps={len(styles)+4} format=3]\n'
 write(Path('themes/adventurer_theme.tres'),'\n'.join(out))

class Scene:
 def __init__(self):self.ext=[];self.nodes=[]
 def resource(self,type,path,key):self.ext.append(f'[ext_resource type="{type}" path="res://{path}" id="{key}"]')
 def node(self,name,type=None,parent=None,**props):
  s=f'[node name="{name}"'+(f' type="{type}"' if type else '')+(f' parent="{parent}"' if parent is not None else '')+']\n'
  for k,v in props.items():s+=k.replace('__','/')+' = '+str(v)+'\n'
  self.nodes.append(s)
 def raw_node(self,s):self.nodes.append(s)
 def save(self,path):write(Path(path),f'[gd_scene load_steps={len(self.ext)+1} format=3]\n\n'+'\n'.join(self.ext)+'\n\n'+'\n'.join(self.nodes))

def label(s,name,parent,text,x,y,w,h=32,var=None,size=None,light=False):
 p=dict(layout_mode=0,offset_left=float(x),offset_top=float(y),offset_right=float(x+w),offset_bottom=float(y+h),text=q(text),mouse_filter=2)
 if var:p['theme_type_variation']=q(var)
 if size:p['theme_override_font_sizes__font_size']=size
 if light:p['theme_override_colors__font_color']=color(T['white'])
 s.node(name,'Label',parent,**p)

def button(s,name,parent,text,x,y,w,h=52,action=None,var='PrimaryButton'):
 p=dict(layout_mode=0,offset_left=float(x),offset_top=float(y),offset_right=float(x+w),offset_bottom=float(y+h),text=q(text),theme_type_variation=q(var),mouse_default_cursor_shape=2)
 if action:p['metadata__action']=q(action)
 s.node(name,'Button',parent,**p)

def panel(s,name,parent,x,y,w,h,dark=False):
 s.node(name,'PanelContainer',parent,layout_mode=0,offset_left=float(x),offset_top=float(y),offset_right=float(x+w),offset_bottom=float(y+h),theme_type_variation=q('DarkPanel' if dark else 'Parchment'),mouse_filter=0)
 # Free-layout children live inside a plain Control; PanelContainer manages it.
 s.node('Content','Control',parent+'/'+name if parent!='.' else name,layout_mode=2,custom_minimum_size=f'Vector2({w-32}, {h-24})',mouse_filter=2)
 return (parent+'/'+name if parent!='.' else name)+'/Content'

def build_native_hud():
 s=Scene();s.resource('Theme','themes/adventurer_theme.tres','theme');s.resource('Texture2D','art/hero_cutout.png','hero');s.resource('PackedScene','ui/components/SkillSlot.tscn','slot')
 for i,ic in enumerate(['sword','bolt','snow','shield','flame','bow','star','dash']):s.resource('Texture2D','icons/'+ic+'.svg','icon'+str(i))
 s.node('NativeHud','Control',layout_mode=3,anchors_preset=15,anchor_right=1.0,anchor_bottom=1.0,grow_horizontal=2,grow_vertical=2,mouse_filter=2,theme='ExtResource("theme")')
 p=panel(s,'Region','.',35,30,515,167,True);label(s,'Name',p,'Ривермут',8,0,465,42,'OnDarkHeader');label(s,'Safety',p,'Город · безопасная зона',8,48,460,28,'OnDark');button(s,'Risk',p,'PvP-тег выключен',8,85,460,42,'pvp','SecondaryButton')
 for i,(txt,action) in enumerate([('C  Персонаж','character'),('B  Инвентарь','inventory'),('N  Эхо','echoes'),('J  Журнал','journal'),('M  Карта','map')]):button(s,'Nav'+str(i),'.',txt,1085+i*164,36,153,52,action)
 p=panel(s,'Quest','.',36,243,398,141,True);label(s,'Title',p,'Лесная дорога',6,0,360,34,'OnDarkHeader',25);label(s,'Body',p,'Передать материалы смотрителю.\nТочное место пока неизвестно.',6,43,366,66,'OnDark',18)
 p=panel(s,'Echoes','.',1632,268,247,249,True)
 for i,(txt,act) in enumerate([('1 · Сиэль — готова','echo_1'),('2 · Каэн — готов','echo_2'),('3 · Торен — 12 с','echo_3')]):button(s,'Echo'+str(i),p,txt,2,5+i*72,210,60,act,'SecondaryButton')
 s.node('Hero','TextureRect','.',layout_mode=0,offset_left=909.0,offset_top=555.0,offset_right=1006.0,offset_bottom=728.0,texture='ExtResource("hero")',expand_mode=1,stretch_mode=5,mouse_filter=2)
 p=panel(s,'Chat','.',35,827,433,202,True);label(s,'Channels',p,'Общий   Мир   Группа   Гильдия',4,0,390,28,'OnDark',17);label(s,'Messages',p,'[Группа] Лира: встречаемся у моста.\n[Группа] Марк: припасы уже у меня.',4,39,390,69,'OnDark',17)
 s.node('Input','LineEdit',p,layout_mode=0,offset_left=4.0,offset_top=123.0,offset_right=391.0,offset_bottom=163.0,placeholder_text=q('Enter · сообщение (демо)'),max_length=120)
 p=panel(s,'Actionbar','.',570,844,824,185,True);label(s,'Name',p,'Каэль · Ур. 24 · Мечник',4,0,780,25,'OnDark',18)
 s.node('Health','ProgressBar',p,layout_mode=0,offset_left=4.0,offset_top=35.0,offset_right=788.0,offset_bottom=55.0,value=87.0,show_percentage='false',mouse_filter=2)
 label(s,'HealthText',p,'2 480 / 2 850',312,29,180,30,'OnDark',16)
 for i,key in enumerate('QWERASDF'):
  s.raw_node(f'[node name="Skill{key}" parent="{p}" instance=ExtResource("slot")]\nlayout_mode = 0\noffset_left = {4+i*98}.0\noffset_top = 72.0\noffset_right = {80+i*98}.0\noffset_bottom = 148.0\nhotkey = "{key}"\nicon_texture = ExtResource("icon{i}")\nmetadata/action = "skill_{key.lower()}"\n')
 label(s,'CombatHints','.','ЛКМ — атака    ПКМ — движение    Shift — парирование    Tab — блок    Space — уклонение',580,1034,1110,32,'OnDark',18)
 s.save('ui/screens/NativeHud.tscn')

def modal_root(s,title):
 s.resource('Theme','themes/adventurer_theme.tres','theme')
 s.node(title,'Control',layout_mode=3,anchors_preset=15,anchor_right=1.0,anchor_bottom=1.0,grow_horizontal=2,grow_vertical=2,mouse_filter=0,theme='ExtResource("theme")')
 s.node('Shade','ColorRect','.',layout_mode=1,anchors_preset=15,anchor_right=1.0,anchor_bottom=1.0,grow_horizontal=2,grow_vertical=2,color='Color(0.04, 0.10, 0.14, 0.65)',mouse_filter=0)
 return panel(s,'Window','.',70,76,1780,927)

def inventory():
 s=Scene();p=modal_root(s,'InventoryPanel');label(s,'Title',p,'Инвентарь и снаряжение',15,4,1400,60,'Header');button(s,'Close',p,'×',1670,4,56,50,'close','SecondaryButton')
 label(s,'Subtitle',p,'Каэль · Мечник · Уровень 24 · Ранг авантюриста F',16,67,1640,37,'Muted')
 s.node('Search','LineEdit',p,layout_mode=0,offset_left=16.0,offset_top=131.0,offset_right=737.0,offset_bottom=185.0,placeholder_text=q('Поиск по названию... (нативный элемент)'),max_length=64)
 nativeitems=['Дорожный клинок +3','Клинок стражи +2','Щит дозора','Шлем странника','Лесная трава','Малое зелье','Осколок памяти','Сапоги следопыта','Полевой дневник','Свет Старого пути','Медный знак','Карта переправы']
 for i,text in enumerate(nativeitems):button(s,'Item'+str(i),p,text,16+i%3*243,210+i//3*90,229,75,'item_'+str(i),'SecondaryButton')
 label(s,'Hint',p,'Поиск фильтрует доступные в демо предметы.\nНативная сетка: отдельные Button-узлы, не картинка.',16,637,712,70,'Muted',19)
 label(s,'ItemName',p,'Дорожный клинок +3',794,137,890,55,'Header',31)
 label(s,'ItemOrigin',p,'Авторская работа · мастер Тален',794,198,890,42,'Muted')
 label(s,'Stats',p,'Урон                              148\nМодификатор защиты                −12\nПрочность                         68 / 80',794,268,890,145,size=23)
 label(s,'RiskInfo',p,'Нелегендарная экипировка может выпасть\nпри PvP-смерти с включённым тегом.',794,449,890,95,size=23)
 button(s,'Compare',p,'Сравнить с надетым',794,578,432,58,'compare','SecondaryButton');button(s,'Equip',p,'Экипировать',1244,578,432,58,'equip')
 button(s,'Lock',p,'Защитить от случайной продажи',794,661,882,58,'lock','SecondaryButton')
 label(s,'LockInfo',p,'Защита от продажи не меняет правила PvP-дропа.',794,748,890,40,'Muted',19)
 label(s,'Footer',p,'Esc — закрыть. Онлайн-мир не останавливается; серверная логика в комплект не входит.',16,834,1660,40,'Muted',18)
 s.save('ui/screens/InventoryPanel.tscn')

def guild():
 s=Scene();p=modal_root(s,'GuildPanel');label(s,'Title',p,'Гильдия Авантюристов',15,4,1400,60,'Header');button(s,'Close',p,'×',1670,4,56,50,'close','SecondaryButton')
 label(s,'Subtitle',p,'Отделение Ривермута · Ранг F · Местное доверие: новичок',16,67,1650,36,'Muted')
 for i,title in enumerate(['F · Лесная дорога','F · Лекарства для лечебницы','E · Пропавший караван','D · Следы у старой мельницы']):button(s,'Quest'+str(i),p,title,16,146+i*117,710,91,'quest_'+str(i),'SecondaryButton')
 button(s,'Rank',p,'Повышение ранга: F → S',16,677,710,56,'rank','GoldButton')
 label(s,'QuestTitle',p,'Лесная дорога',777,136,894,53,'Header');label(s,'InfoStatus',p,'Ранг F · Сведения предварительные',777,199,893,38,'Muted')
 label(s,'Body',p,'Доставьте материалы смотрителю дороги\nи выясните, почему больше нет отчётов.\n\nТочное местонахождение не подтверждено.\n\nОтчёт о новой опасности — полезный результат.',777,265,914,241,size=23)
 label(s,'Reward',p,'Награда: 180 монет и запись в послужном списке.',777,547,899,50,size=22)
 button(s,'Accept',p,'Принять поручение',777,644,892,60,'accept','PrimaryButton');button(s,'Journal',p,'Мои контракты',777,730,892,52,'journal','SecondaryButton')
 label(s,'Footer',p,'Это локальная демонстрация. При интеграции право на контракт и награду проверяет сервер.',16,834,1658,42,'Muted',18)
 s.save('ui/screens/GuildPanel.tscn')

def component():
 s=Scene();s.resource('Theme','themes/adventurer_theme.tres','theme');s.resource('Script','scripts/skill_slot.gd','script')
 s.node('SkillSlot','Button',custom_minimum_size='Vector2(76, 76)',offset_right=76.0,offset_bottom=76.0,theme='ExtResource("theme")',theme_type_variation=q('SkillButton'),script='ExtResource("script")',mouse_default_cursor_shape=2)
 s.node('Icon','TextureRect','.',layout_mode=0,offset_left=16.0,offset_top=10.0,offset_right=60.0,offset_bottom=54.0,expand_mode=1,stretch_mode=5,mouse_filter=2,modulate='Color(0.96, 0.86, 0.64, 1)')
 s.node('Key','Label','.',layout_mode=0,offset_left=18.0,offset_top=52.0,offset_right=58.0,offset_bottom=76.0,text=q('Q'),horizontal_alignment=1,theme_type_variation=q('OnDark'),mouse_filter=2)
 s.save('ui/components/SkillSlot.tscn')
 s=Scene();s.resource('Theme','themes/adventurer_theme.tres','theme')
 s.node('PrimaryButton','Button',custom_minimum_size='Vector2(220, 54)',offset_right=220.0,offset_bottom=54.0,theme='ExtResource("theme")',theme_type_variation=q('PrimaryButton'),text=q('Продолжить'))
 s.save('ui/components/PrimaryButton.tscn')
 s=Scene();s.resource('Theme','themes/adventurer_theme.tres','theme');s.node('ParchmentPanel','PanelContainer',custom_minimum_size='Vector2(360, 180)',offset_right=360.0,offset_bottom=180.0,theme='ExtResource("theme")',theme_type_variation=q('Parchment'));s.node('Text','Label','.',layout_mode=2,text=q('Панель · содержимое заменяется'))
 s.save('ui/components/ParchmentPanel.tscn')

def main_scene():
 s=Scene();s.resource('Theme','themes/adventurer_theme.tres','theme');s.resource('Texture2D','art/world.jpg','world');s.resource('Script','scripts/main.gd','script')
 for key in ['NativeHud','InventoryPanel','GuildPanel']:s.resource('PackedScene','ui/screens/'+key+'.tscn',key)
 s.node('UIStarter','Control',layout_mode=3,anchors_preset=15,anchor_right=1.0,anchor_bottom=1.0,grow_horizontal=2,grow_vertical=2,theme='ExtResource("theme")',script='ExtResource("script")',mouse_filter=2)
 s.node('WorldReference','TextureRect','.',layout_mode=1,anchors_preset=15,anchor_right=1.0,anchor_bottom=1.0,grow_horizontal=2,grow_vertical=2,texture='ExtResource("world")',expand_mode=1,stretch_mode=6,mouse_filter=2)
 s.raw_node('[node name="NativeHud" parent="." instance=ExtResource("NativeHud")]\nlayout_mode = 1\n')
 for key in ['InventoryPanel','GuildPanel']:s.raw_node(f'[node name="{key}" parent="." instance=ExtResource("{key}")]\nvisible = false\nlayout_mode = 1\n')
 s.node('Message','AcceptDialog','.',title=q('Project G · UI demo'),initial_position=2,size='Vector2i(610, 260)',dialog_text=q('Демонстрация интерфейса'))
 s.node('Toast','Label','.',layout_mode=0,offset_left=560.0,offset_top=749.0,offset_right=1410.0,offset_bottom=808.0,text=q('Нативный UI starter · B — инвентарь · G — Гильдия'),horizontal_alignment=1,theme_type_variation=q('OnDark'))
 s.save('Main.tscn')

def main():
 for src in (ROOT/'assets/icons').glob('*.svg'):(G/'icons'/src.name).write_text(src.read_text().replace('#274B64','#FFFFFF'))
 for name in ['world.jpg','hero_cutout.png','hero_portrait.png','echo_portrait.png','barton_portrait.png']:shutil.copy2(ROOT/'assets/art'/name,G/'art'/name)
 theme();component();build_native_hud();inventory();guild();main_scene()
 write(Path('project.godot'),'''; Standalone UI kit. No game backend, no .NET dependency.
config_version=5

[application]
config/name="Project G — Native UI Starter"
run/main_scene="res://Main.tscn"
config/features=PackedStringArray("4.3", "GL Compatibility")

[display]
window/size/viewport_width=1920
window/size/viewport_height=1080
window/size/window_width_override=1280
window/size/window_height_override=720
window/stretch/mode="canvas_items"

[rendering]
renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
textures/default_filters/use_nearest_mipmap_filter=false
''')
 print('Godot starter generated: native HUD / inventory / guild + 3 components')

if __name__=='__main__':main()
