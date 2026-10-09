"""Static integrity checks only; not a Godot runtime/parser replacement."""
from pathlib import Path
import json,re,xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]; checks=[]
def check(name,ok,detail=''):checks.append({'check':name,'passed':bool(ok),'detail':detail})
for file in list((ROOT/'design').rglob('*.svg'))+list((ROOT/'assets/icons').glob('*.svg')):
 try:
  tree=ET.fromstring(file.read_text());check('XML: '+str(file.relative_to(ROOT)),True)
 except ET.ParseError as e:check('XML: '+str(file.relative_to(ROOT)),False,str(e))
for file in (ROOT/'godot').rglob('*'):
 if file.suffix not in ['.tscn','.tres','.godot','.gd']:continue
 text=file.read_text()
 for path in re.findall(r'res://([^"\s]+)',text):
  check('Godot resource '+path,(ROOT/'godot'/path).exists(),str(file.relative_to(ROOT)))
 if file.suffix in ['.tscn','.tres']:
  expected=1+len(re.findall(r'^\[(?:ext_resource|sub_resource) ',text,re.M))
  match=re.search(r'load_steps=(\d+)',text)
  if match:check('Resource count: '+file.name,int(match.group(1))==expected,f'expected {expected}')
for forbidden in ['.ttf','.otf','.woff','.woff2']:
 check('No bundled '+forbidden+' fonts',not list(ROOT.rglob('*'+forbidden)))
check('Eight design screens',len(list((ROOT/'design/screens').glob('*.svg')))==8)
check('Forty independent icons',len(list((ROOT/'assets/icons').glob('*.svg')))==40)
for file in (ROOT/'design/screens').glob('*.svg'):
 text=file.read_text();check('Editable text '+file.name,'<text ' in text);check('Portable art '+file.name,'@ART/' not in text and '../assets' not in text)
(ROOT/'tests/static_results.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2))
failed=[c for c in checks if not c['passed']]
print(len(checks)-len(failed),'/',len(checks),'static checks passed')
for item in failed:print(item)
if failed:raise SystemExit(1)
