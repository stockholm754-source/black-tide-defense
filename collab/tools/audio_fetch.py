from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urljoin
import re, json, concurrent.futures, hashlib, html, zipfile
root=Path(__file__).resolve().parents[2]
out=root/'Captures/audio_20261011/source'
out.mkdir(parents=True,exist_ok=True)
pages={
 'impact':'https://kenney.nl/assets/impact-sounds',
 'rpg':'https://kenney.nl/assets/rpg-audio',
 'interface':'https://kenney.nl/assets/interface-sounds',
 'scifi':'https://kenney.nl/assets/sci-fi-sounds',
 'melee':'https://opengameart.org/content/3-melee-sounds',
 'sword':'https://opengameart.org/content/20-sword-sound-effects-attacks-and-clashes',
 'explore':'https://opengameart.org/content/emptycity-background-music',
 'combat':'https://opengameart.org/content/battle-theme-0',
 'boss':'https://opengameart.org/content/the-final-battle',
}
def get(url):
 with urlopen(Request(url,headers={'User-Agent':'Mozilla/5.0'}),timeout=45) as r:return r.read()
def fetch(item):
 name,url=item
 page=get(url).decode('utf-8','replace')
 (out/(name+'.html')).write_text(page,encoding='utf-8')
 links=[html.unescape(x) for x in re.findall(r'(?:href|src|data-download)=["\']([^"\']+)',page)]
 links=[urljoin(url,x) for x in links if re.search(r'\.(zip|ogg|mp3)(?:[?\"\']|$)',x)]
 links=list(dict.fromkeys(links))
 if name=='explore':links=[x for x in links if 'EmptyCity.ogg' in x]
 if name=='combat':links=[x for x in links if 'Battle.mp3' in x]
 if name=='boss':links=[x for x in links if 'the_final_battle.ogg' in x]
 if name=='sword':links=[x for x in links if 'starninjas' in x and x.endswith('.zip')]
 if name=='melee':links=[x for x in links if x.endswith('.zip')]
 records=[]
 for i,asset in enumerate(links):
  ext=asset.split('?')[0].rsplit('.',1)[-1]
  data=get(asset)
  dest=out/f'{name}_{i}.{ext}';dest.write_bytes(data)
  records.append({'name':name,'page':url,'download':asset,'file':str(dest.relative_to(root)),'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'license':'CC0'})
  if ext=='zip':
   with zipfile.ZipFile(dest) as z:
    target=out/f'{name}_{i}'
    for member in z.infolist():
     p=(target/member.filename).resolve()
     if not p.is_relative_to(target.resolve()):raise ValueError('unsafe zip path')
    z.extractall(target)
 return name,records
all_records=[]
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
 for f in concurrent.futures.as_completed([pool.submit(fetch,x) for x in pages.items()]):
  try:
   name,records=f.result();all_records+=records;print(name,[(Path(x['file']).name,x['bytes']) for x in records],flush=True)
  except Exception as e:print(type(e).__name__,str(e),flush=True)
(out.parent/'download-manifest.json').write_text(json.dumps(all_records,ensure_ascii=False,indent=2),encoding='utf-8')
