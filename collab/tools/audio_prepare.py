"""Reproducible CC0 sample preparation; no asset database or scene mutations."""
from pathlib import Path
import sys, subprocess, json, hashlib
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
CAP=ROOT/'Captures/audio_20261011'
sys.path.insert(0,str(CAP/'tools'))
import soundfile as sf
import imageio_ffmpeg
FF=imageio_ffmpeg.get_ffmpeg_exe()
SRC=CAP/'source'; OUT=CAP/'prepared'; OUT.mkdir(exist_ok=True)
RATE=44100
cache={}; manifest=[]
def read(rel):
    if rel not in cache:
        p=SRC/rel
        if not p.exists():
            matches=list((SRC/Path(rel).parts[0]).rglob(Path(rel).name))
            if len(matches)!=1: raise FileNotFoundError(rel)
            p=matches[0]
        raw=subprocess.run([FF,'-v','error','-i',str(p),'-f','f32le','-ar',str(RATE),'-ac','1','pipe:1'],capture_output=True,check=True).stdout
        a=np.frombuffer(raw,dtype='<f4').copy()
        active=np.where(np.abs(a)>.002)[0]
        if len(active): a=a[max(0,active[0]-220):min(len(a),active[-1]+882)]
        cache[rel]=a
    return cache[rel].copy()
def shape(a,pitch=1,limit=None):
    if pitch!=1: a=np.interp(np.arange(0,len(a)-1,pitch),np.arange(len(a)),a).astype('f4')
    if limit: a=a[:int(limit*RATE)]
    f=min(150,len(a)//4); a[:f]*=np.linspace(0,1,f); a[-f:]*=np.linspace(1,0,f)
    return a
def make(name,layers,peak=.65,limit=None):
    items=[]
    for rel,gain,pitch,delay in layers:
        a=shape(read(rel),pitch,limit); a/=max(.01,float(np.max(np.abs(a))))
        items.append((a*gain,int(delay*RATE)))
    result=np.zeros(max(len(a)+t for a,t in items),dtype='f4')
    for a,t in items: result[t:t+len(a)]+=a
    result=shape(result,limit=limit)
    result-=float(np.mean(result)); result*=peak/max(.01,float(np.max(np.abs(result))))
    dest=OUT/'SFX'/f'{name}.wav'; dest.parent.mkdir(exist_ok=True)
    sf.write(dest,result,RATE,subtype='PCM_16')
    manifest.append(dict(name=name,sources=[x[0] for x in layers],seconds=round(len(result)/RATE,3),peak=round(float(np.max(np.abs(result))),3),sha256=hashlib.sha256(dest.read_bytes()).hexdigest()))
def L(file,gain=1,pitch=1,delay=0): return (file,gain,pitch,delay)
I='impact_1/Audio/'; S='scifi_1/Audio/'; R='rpg_1/Audio/'; U='interface_1/Audio/'
for i in range(1,4):
    make(f'swing_{i}',[L(f'sword_0/sword.{i}.ogg')],.5, .55)
    make(f'hit_{i}',[L(I+f'impactPunch_medium_00{i-1}.ogg',1,.95),L(f'sword_1/sword_clash.{i}.ogg',.35,1.15,.018)],.66,.48)
make('swing_heavy',[L('sword_0/sword.4.ogg',1,.75),L(R+'cloth3.ogg',.22,.8)],.62,.68)
make('hit_heavy',[L(I+'impactPunch_heavy_000.ogg'),L(S+'lowFrequency_explosion_000.ogg',.38,1.1,.012),L('sword_1/sword_clash.4.ogg',.3,.85)],.76,.72)
make('hit_armor',[L('sword_1/sword_clash.5.ogg'),L(I+'impactMetal_medium_000.ogg',.5,.9)],.65,.55)
make('dodge',[L('melee_0/melee sound.wav',1,1.2),L(R+'cloth1.ogg',.35,1.1)],.5,.36)
make('dash',[L('melee_0/melee sound.wav',1,.78),L(S+'thrusterFire_000.ogg',.3,1.5)],.6,.62)
make('perfect_dodge',[L(S+'forceField_001.ogg',1,1.5),L(I+'impactBell_heavy_000.ogg',.35,1.6)],.65,.65)
make('player_hurt',[L(I+'impactPunch_heavy_002.ogg'),L(R+'cloth2.ogg',.3,.65)],.72,.42)
make('enemy_death',[L(I+'impactSoft_heavy_001.ogg',1,.75),L(S+'slime_001.ogg',.4,1.2)],.45,.48)
make('stagger_break',[L(I+'impactGlass_heavy_001.ogg',.8,.8),L(S+'forceField_003.ogg',.5,1.3)],.7,.95)
make('boss_swing',[L('sword_0/sword.6.ogg',1,.55),L('melee_0/animal melee sound.wav',.35,.7)],.58,.85)
make('boss_slam',[L(S+'explosionCrunch_000.ogg',.75,.75),L(I+'impactMetal_heavy_000.ogg',.6,.7)],.72,1.2)
make('telegraph',[L(S+'forceField_000.ogg',.8,.9)],.42,.8)
make('attack_warning',[L(I+'impactBell_heavy_003.ogg',1,1.55)],.55,.22)
make('summon',[L(S+'spaceEngineSmall_002.ogg',.7,1.3),L(S+'forceField_004.ogg',.5,1.2)],.6,1.1)
make('chant_loop',[L(S+'engineCircular_002.ogg',1,.72)],.32,2.8)
make('chant_blast',[L(S+'explosionCrunch_002.ogg',.7,.82),L(S+'forceField_003.ogg',.4,.8)],.72,1.4)
make('ult_charge',[L(S+'spaceEngineLarge_002.ogg',.7,1.1),L(S+'forceField_000.ogg',.5,.7,.15)],.62,1.4)
make('ult_hit',[L(S+'lowFrequency_explosion_001.ogg',.65,.85),L(S+'explosionCrunch_003.ogg',.7,1.05),L(I+'impactGlass_heavy_003.ogg',.35,.7,.08)],.78,1.45)
make('shot',[L(S+'laserSmall_001.ogg')],.5,.32)
make('resonance_shot',[L(S+'laserLarge_002.ogg',1,1.25)],.5,.48)
make('ui_move',[L(U+'click_001.ogg')],.27,.1)
make('ui_confirm',[L(U+'confirmation_001.ogg')],.45,.42)
make('ui_back',[L(U+'back_001.ogg')],.36,.28)
make('purchase',[L(R+'handleCoins.ogg',.8,1.1),L(U+'confirmation_002.ogg',.3,1.25,.07)],.52,.7)
make('buff_pick',[L(S+'forceField_004.ogg',.5,1.7),L(U+'confirmation_004.ogg',.7,1.15)],.56,.85)
for i in range(1,4): make(f'footstep_{i}',[L(I+f'footstep_concrete_00{i-1}.ogg')],.28,.28)
# Music is stereo Vorbis. Trim silence and leave a short edge fade; runtime overlaps
# two streams at every repeat and transition, including Final Battle's documented gap.
for name,src in [('explore','explore_0.ogg'),('combat','combat_0.mp3'),('boss','boss_0.ogg')]:
    dest=OUT/'Music'/f'{name}.ogg'; dest.parent.mkdir(exist_ok=True)
    filters='silenceremove=start_periods=1:start_threshold=-50dB,areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse,loudnorm=I=-21:TP=-3:LRA=9,afade=t=in:d=0.04'
    subprocess.run([FF,'-y','-v','error','-i',str(SRC/src),'-af',filters,'-ar','44100','-ac','2','-c:a','libvorbis','-q:a','5',str(dest)],check=True)
    info=sf.info(dest); a,_=sf.read(dest,dtype='float32')
    manifest.append(dict(name='music/'+name,sources=[src],seconds=round(info.duration,3),peak=round(float(np.max(np.abs(a))),3),sha256=hashlib.sha256(dest.read_bytes()).hexdigest()))
# Short arrangement preview; this is an asset preview, not a captured gameplay mix.
preview=np.zeros((24*RATE,2),dtype='f4')
for i,name in enumerate(['explore','combat','boss']):
    a,_=sf.read(OUT/'Music'/f'{name}.ogg',dtype='float32',always_2d=True); a=a[min(10*RATE,len(a)//4):][:8*RATE]
    fade=RATE//3; a[:fade]*=np.linspace(0,1,fade)[:,None];a[-fade:]*=np.linspace(1,0,fade)[:,None]
    preview[i*8*RATE:i*8*RATE+len(a)]+=a*.75
sf.write(CAP/'music-preview.wav',preview,RATE,subtype='PCM_16')
preview=np.zeros(15*RATE,dtype='f4')
for i,name in enumerate(['swing_1','hit_1','swing_heavy','hit_heavy','dodge','perfect_dodge','player_hurt','stagger_break','ult_charge','ult_hit']):
    a,_=sf.read(OUT/'SFX'/f'{name}.wav',dtype='float32');start=int(i*1.4*RATE);preview[start:start+len(a)]+=a*.65
sf.write(CAP/'feedback-preview.wav',preview,RATE,subtype='PCM_16')
(CAP/'prepared-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(dict(samples=len(manifest),music=[m for m in manifest if m['name'].startswith('music/')]),indent=2))
