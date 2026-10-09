#!/usr/bin/env python3
"""Build a single offline HTML preview without external requests."""
from pathlib import Path
import base64,re
ROOT=Path(__file__).resolve().parents[1]
html=(ROOT/'prototype/index.html').read_text()
for css in ['tokens.css','styles.css']:
 html=html.replace(f'<link rel="stylesheet" href="{css}">','<style>\n'+(ROOT/'prototype'/css).read_text()+'\n</style>')
for js in ['screens.js','app.js']:
 text=(ROOT/'prototype'/js).read_text()
 if js=='screens.js':
  for art in (ROOT/'assets/art').iterdir():
   mime='image/jpeg' if art.suffix=='.jpg' else 'image/png'
   text=text.replace('../assets/art/'+art.name,'data:'+mime+';base64,'+base64.b64encode(art.read_bytes()).decode())
 html=html.replace(f'<script src="{js}"></script>','<script>\n'+text.replace('</script','<\\/script')+'\n</script>')
(ROOT/'Preview.html').write_text(html,encoding='utf-8')
print('Standalone preview:',len(html.encode('utf-8')),'bytes')
