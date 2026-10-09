#!/usr/bin/env python3
"""Export small editable component assets and raster icon conveniences."""
from pathlib import Path
import re
from build_design import ROOT, Canvas, C, skill

def save(c,name,w,h):
 s=c.finish().replace('width="1920" height="1080" viewBox="0 0 1920 1080"',f'width="{w}" height="{h}" viewBox="0 0 {w} {h}"',1)
 (ROOT/'design/components'/f'{name}.svg').write_text(s,encoding='utf-8')

def main():
 for name,style in [('default','primary'),('hover','hover'),('pressed','pressed'),('disabled','disabled'),('secondary','secondary'),('gold','gold')]:
  c=Canvas('Button / '+name);c.button(4,4,220,'Продолжить',style);save(c,'button_'+name,228,62)
 c=Canvas('Button / focus');c.button(6,6,220,'Продолжить');c.rect(2,2,228,62,'none',C['gold'],13,3);save(c,'button_focus',232,66)
 for name,dark in [('parchment',False),('dark',True)]:
  c=Canvas('Panel / '+name);c.panel(2,2,356,196,dark=dark);save(c,'panel_'+name,360,206)
 for name,cd in [('ready',0),('cooldown',8)]:
  c=Canvas('Skill / '+name);skill(c,3,3,'sword','Q',0,cd);save(c,'skill_'+name,82,91)
 for r in 'FEDCBAS':
  c=Canvas('Rank / '+r);c.badge(2,2,r,'gold',48);save(c,'rank_'+r,52,36)
 for name,col in [('default',C['line']),('selected',C['gold']),('legendary',C['purple'])]:
  c=Canvas('Item / '+name);c.rect(2,2,92,92,C['paper'],col,9,3 if name=='selected' else 1);c.icon('sword',24,17,48,col);c.rect(8,85,80,3,col,r=1);save(c,'item_'+name,96,96)
 print('Individual SVG components generated.')
if __name__=='__main__':main()
