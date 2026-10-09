"""Build text-free native UI resources from the supplied, editable source kit.
Run from any directory: python3 tools/import-ui-source-kit.py
No demo scenes, game data, character art, source scripts or game rules are imported.
"""
from pathlib import Path
import json
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
KIT = ROOT / "Project_G_UI_Source_Kit"
ASSETS = ROOT / "Content.Client/Assets/UI"
THEME = ROOT / "Content.Client/UI/Theme"
SVG = "{http://www.w3.org/2000/svg}"
ET.register_namespace("", "http://www.w3.org/2000/svg")
TOKENS = json.loads((KIT / "design/tokens.json").read_text())
COLORS = TOKENS["colors"]
for folder in [ASSETS / "Icons/SourceKit", ASSETS / "Textures/SourceKit", THEME]:
    folder.mkdir(parents=True, exist_ok=True)

def color(key, alpha=1):
    value = COLORS[key].lstrip("#")
    channels = [int(value[i:i+2],16)/255 for i in (0,2,4)] + [alpha]
    return "Color(" + ", ".join(f"{x:.6f}" for x in channels) + ")"

def export_component(name):
    root = ET.parse(KIT / "design/components" / (name + ".svg")).getroot()
    for parent in root.iter():
        for child in list(parent):
            if child.tag in [SVG+"text", SVG+"title", SVG+"desc"]:
                parent.remove(child)
    if name.startswith("item_"):
        for child in list(root):
            if child.tag == SVG+"g": root.remove(child)
    if name == "button_focus":
        rects = root.findall(SVG+"rect")
        for rect in rects[:-1]: root.remove(rect)
    root.attrib.pop("aria-label",None)
    ET.ElementTree(root).write(ASSETS / "Textures/SourceKit" / (name+".svg"),encoding="unicode")

components=["panel_parchment","panel_dark","button_default","button_hover","button_pressed","button_disabled","button_focus","button_gold","button_secondary","item_default","item_selected"]
for name in components: export_component(name)
icons=sorted((KIT / "godot/icons").glob("*.svg"))
for icon in icons: shutil.copyfile(icon,ASSETS / "Icons/SourceKit" / icon.name)

# StyleBoxTexture keeps source outlines/gradients and stretches only the middle.
parts=['[gd_resource type="Resource" script_class="UiSkin" load_steps=LOAD_STEPS format=3]',
       '[ext_resource type="Script" path="res://Scripts/UI/Assets/UiSkin.cs" id="skin"]']
for name in components:
    parts.append(f'[ext_resource type="Texture2D" path="res://Assets/UI/Textures/SourceKit/{name}.svg" id="{name}"]')
styles={"PaperPanel":"panel_parchment","DarkPanel":"panel_dark","ButtonNormal":"button_default","ButtonHover":"button_hover","ButtonPressed":"button_pressed","ButtonDisabled":"button_disabled","ButtonFocus":"button_focus","SlotNormal":"item_default","SlotSelected":"item_selected","SlotHover":"item_selected","TabNormal":"button_default","TabSelected":"button_gold","TabDisabled":"button_disabled","InputNormal":"button_secondary","InputFocus":"button_focus","TooltipPanel":"panel_dark"}
for key,texture in styles.items():
    edge=20 if key in ["PaperPanel","DarkPanel","TooltipPanel"] else 14
    padding=18 if key=="PaperPanel" else 12 if key in ["DarkPanel","TooltipPanel"] else 4 if key.startswith("Slot") else 10
    lines=[f'[sub_resource type="StyleBoxTexture" id="{key}"]',f'texture = ExtResource("{texture}")']
    for side in ["left","top","right","bottom"]:
        lines += [f'texture_margin_{side} = {edge}.0',f'content_margin_{side} = {0 if key.endswith("Focus") else padding}.0']
    parts.append("\n".join(lines))
for key,tint in {"GaugeBackground":"dark","HealthFill":"green","ManaFill":"blue","StaminaFill":"goldLight","ExperienceFill":"gold"}.items():
    lines=[f'[sub_resource type="StyleBoxFlat" id="{key}"]',f'bg_color = {color(tint)}']
    for side in ["left","top","right","bottom"]:lines.append(f'content_margin_{side} = 0.0')
    for corner in ["top_left","top_right","bottom_left","bottom_right"]:lines.append(f'corner_radius_{corner} = 5')
    parts.append("\n".join(lines))
parts += ['[sub_resource type="SystemFont" id="BodyFont"]\nfont_names = PackedStringArray("Arial", "Liberation Sans", "DejaVu Sans")',
          '[sub_resource type="SystemFont" id="HeadingFont"]\nfont_names = PackedStringArray("Georgia", "Liberation Serif", "DejaVu Serif")']
resource=['[resource]','script = ExtResource("skin")','Font = SubResource("BodyFont")','HeadingFont = SubResource("HeadingFont")','FontSize = 16','Spacing = 8']
for key,tint in {"Paper":"paper","Ink":"ink","Navy":"dark","LightText":"white","Gold":"goldLight","MutedInk":"muted","DisabledText":"muted","IconTint":"navy","Health":"green","Mana":"blue","Stamina":"goldLight"}.items():
    resource.append(f'{key} = {color(tint,.9 if key=="Navy" else 1)}')
for key in list(styles)+["GaugeBackground","HealthFill","ManaFill","StaminaFill","ExperienceFill"]:resource.append(f'{key} = SubResource("{key}")')
parts.append("\n".join(resource))
text="\n\n".join(parts)+"\n"
text=text.replace("LOAD_STEPS",str(text.count("[ext_resource")+text.count("[sub_resource")+1))
(THEME / "fantasy-skin.tres").write_text(text)

aliases={"equipment.weapon":"sword","equipment.armor":"shield","equipment.unknown":"bag", "nav.character":"person","nav.inventory":"bag","nav.map":"map","nav.quests":"scroll","nav.menu":"menu", "skill.1":"bolt","skill.2":"sun","skill.3":"dash","skill.5":"bolt","skill.6":"star"}
entries={"icon."+p.stem:p.stem for p in icons};entries.update(aliases)
parts=['[gd_resource type="Resource" script_class="UiArtCatalog" load_steps=LOAD_STEPS format=3]',
       '[ext_resource type="Script" path="res://Scripts/UI/Assets/UiArtCatalog.cs" id="catalog"]',
       '[ext_resource type="Script" path="res://Scripts/UI/Assets/UiArtEntry.cs" id="entry"]']
for icon in icons:parts.append(f'[ext_resource type="Texture2D" path="res://Assets/UI/Icons/SourceKit/{icon.name}" id="{icon.stem}"]')
ids=[]
for i,(key,icon) in enumerate(entries.items()):
    identity=f'art_{i}';ids.append(f'SubResource("{identity}")')
    parts.append(f'[sub_resource type="Resource" id="{identity}"]\nscript = ExtResource("entry")\nKey = "{key}"\nTexture = ExtResource("{icon}")\nAttribution = "User-supplied Project_G_UI_Source_Kit; see docs/ASSET_PROVENANCE.md in the source kit."')
parts.append('[resource]\nscript = ExtResource("catalog")\nEntries = Array[ExtResource("entry")](['+", ".join(ids)+'])')
text="\n\n".join(parts)+"\n";text=text.replace("LOAD_STEPS",str(text.count("[ext_resource")+text.count("[sub_resource")+1))
(THEME / "art-catalog.tres").write_text(text)
(ASSETS / "SOURCE_KIT.md").write_text("# Source Kit runtime resources\n\nImported by tools/import-ui-source-kit.py from Project_G_UI_Source_Kit.\nIcons use the supplied white Godot variants; controls apply theme tint.\nComponents retain vector outlines/gradients, with sample text and item illustrations removed.\nFull-screen SVGs, demo scripts/data, map geography and character illustrations are not runtime UI.\nProvenance: Project_G_UI_Source_Kit/docs/ASSET_PROVENANCE.md.\n")
print(f"Imported {len(icons)} icons, {len(components)} component textures, {len(entries)} catalog keys.")
