from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import difflib, json, re

root=Path(__file__).resolve().parents[2]
folder=root/'Captures/vfx_CX007'
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',20)
title=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
rows=[('刀光：白亮芯 / 连续本色刀锋','slash_015.png'),('地火：白亮地斑 / 持续立体火舌','fire_042.png'),('护盾：镜头贴片 / 跟随人物的六面盾壳','shield_015.png')]
sheet=Image.new('RGB',(960,951),'#10151d'); draw=ImageDraw.Draw(sheet)
draw.text((12,8),'同镜头、同种子、同阶段  |  左：修改前   右：修改后',font=title,fill='#d5e4ef')
for i,(label,name) in enumerate(rows):
    y=45+i*302
    draw.text((12,y+3),label,font=font,fill='#a3dce8')
    for col,stage in enumerate(['before','after']):
        shot=Image.open(folder/stage/name).convert('RGB').resize((480,270),Image.Resampling.LANCZOS)
        sheet.paste(shot,(col*480,y+32))
sheet.save(folder/'comparison.png')
diff=[]
for p in (root/'Backups/CX007').glob('*'):
    if p.suffix not in ['.cs','.shader']: continue
    matches=list((root/'Assets/BlackTide').rglob(p.name))
    if len(matches)!=1: continue
    new=matches[0]
    diff.extend(difflib.unified_diff(p.read_text(encoding='utf-8-sig').splitlines(True),new.read_text(encoding='utf-8-sig').splitlines(True),fromfile='before/'+p.name,tofile=str(new.relative_to(root))))
(folder/'runtime-changes.diff').write_text(''.join(diff),encoding='utf-8')
log=(folder/'final-build.log').read_text(encoding='utf-8-sig',errors='replace') if (folder/'final-build.log').exists() else ''
checks=(folder/'integration/checks.txt').read_text(encoding='utf-8-sig').splitlines()
summary={'before_captures':len(list((folder/'before').glob('*.png'))),'after_captures':len(list((folder/'after').glob('*.png'))),
         'integration_checks':len(checks),'failed_checks':[s for s in checks if not s.endswith('PASS')],
         'final_build_results':re.findall(r'BUILD_RESULT (?:Succeeded|Failed) size=\d+ errors=\d+',log),'final_cs_errors':len(re.findall(r'error CS\d+',log)),
         'final_shader_errors':len(re.findall(r'Shader error',log))}
(folder/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False))
