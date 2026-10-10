"""Original synthesized placeholder Foley, reproducible without third-party samples."""
import math, random, wave, struct
from pathlib import Path
import argparse
p=argparse.ArgumentParser(); p.add_argument('--out',required=True); a=p.parse_args(); out=Path(a.out); out.mkdir(parents=True,exist_ok=True)
RATE=22050
spec={'basic_left':(.23,500,'swing'),'basic_right':(.25,420,'swing'),'thrust':(.16,900,'swing'),
'sweep':(.38,330,'swing'),'rend':(.26,750,'swing'),'breaker':(.42,210,'swing'),
'pommel':(.16,170,'thud'),'hamstring':(.30,380,'swing'),'riposte':(.19,820,'swing'),
'whirl':(.55,310,'swing'),'finisher':(.44,190,'swing'),'dash':(.40,450,'swing'),
'breath':(1.2,180,'breath'),'recover':(.35,540,'tone'),'hit':(.18,125,'thud'),
'block':(.30,740,'metal'),'parry':(.45,1450,'metal'),'parry_attempt':(.15,650,'swing'),
'footstep':(.13,90,'thud'),'equip':(.35,920,'metal'),'death':(.52,75,'thud'),
'cast_release':(.40,600,'tone'),'cast_self':(.45,440,'tone')}
for name,(duration,freq,kind) in spec.items():
    rng=random.Random(name); data=[]; smooth=0
    for i in range(int(RATE*duration)):
        t=i/RATE; u=t/duration; noise=rng.uniform(-1,1); smooth=.86*smooth+.14*noise
        if kind=='swing': v=(noise-smooth)*.50*math.sin(math.pi*u)**2 + smooth*.15
        elif kind=='metal': v=sum(math.sin(math.tau*freq*r*t)*math.exp(-t*(9+j*8))*.16 for j,r in enumerate([1,1.43,2.81,4.17]))+noise*.13*math.exp(-t*70)
        elif kind=='thud': v=(math.sin(math.tau*freq*t)*.45+smooth*.9+noise*.20)*math.exp(-u*9)
        elif kind=='breath': v=smooth*.6*(math.sin(math.pi*u)**2)
        else: v=(math.sin(math.tau*freq*t)+math.sin(math.tau*freq*1.5*t)*.3)*.16*math.sin(math.pi*u)**2
        fade=min(1,t/.003,(duration-t)/.012); data.append(struct.pack('<h',int(max(-.9,min(.9,v*fade))*32767)))
    with wave.open(str(out/(name+'.wav')),'wb') as f: f.setnchannels(1); f.setsampwidth(2); f.setframerate(RATE); f.writeframes(b''.join(data))
print('AUDIO_OK',len(spec),'original mono cues')
