#!/usr/bin/env python3
"""Reproduce the conservative v0.9.0.9 album-alias and action-plan regression.

Pure offline inspection: never contacts Lidarr/MusicBrainz and never changes inputs.
Run: python tools/offline_v0909_regression.py --readiness /path/to/...csv --candidates /path/to/...csv --plan /path/to/...csv
"""
import argparse,csv,collections,re,sys

def rows(path):
    with open(path,encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))

def normalize(s):
    return ''.join(ch.lower() for ch in s if ch.isalnum())

def album_core(s):
    s=re.sub(r'\s*[\(\[]\s*(?:deluxe(?:\s+edition|\s+version)?|expanded(?:\s+edition)?|special\s+(?:edition|version)|complete\s+edition|remaster(?:ed)?(?:\s+edition|\s+version)?|\d+(?:st|nd|rd|th)\s+anniversary\s+edition|platinum\s+edition|bonus\s+tracks?)\s*[\)\]]\s*$', '',s,flags=re.I)
    s=re.sub(r'\s*[-:]\s*(?:the\s+)?(?:complete\s+confection|paradise\s+edition|platinum\s+edition)\s*$', '',s,flags=re.I)
    s=re.sub(r'\s+(?:deluxe\s+edition|expanded\s+edition)\s*$', '',s,flags=re.I)
    return normalize(s)

def volume(s):
    return re.sub(r'\bvol\.?\s*(\d+)\b',r'vol\1',re.sub(r'\bvolume\s*(\d+)\b',r'vol\1',s,flags=re.I),flags=re.I)

def relationship(a,b):
    if not a or not b:return 'different-title'
    if normalize(a) and normalize(a)==normalize(b):return 'exact-normalized-title'
    if a.strip().replace('º','°')==b.strip().replace('º','°') and a.strip()!=b.strip():return 'unicode-glyph-variant'
    v=normalize(volume(a))
    if len(v)>=5 and v==normalize(volume(b)):return 'volume-notation-variant'
    q=album_core(a)
    if len(q)>=5 and q==album_core(b):return 'edition-or-suffix-variant'
    return 'different-title'

def run(readiness,candidates,plan):
    r=rows(readiness);c=rows(candidates);p=rows(plan)
    songs=[x for x in r if x['Type']=='Song']
    assert len(songs)==2505,(len(songs),'Unexpected source size; use exact regression baseline')
    songrows={int(float(x['YouTube source row'])):x for x in songs}
    cand=collections.defaultdict(list)
    for x in c:cand[int(x['YouTube source row'])].append(x)
    review=[x for x in songs if x['Planned category'].startswith('Manual Review')]
    assert len(review)==349,('Expected v0.9.0.8 baseline with 349 manual songs',len(review))
    assert len({int(x['Source row']) for x in p})==2505,'Action plan must cover all source rows'
    manual_60=[x for x in review if x['Comparison method']=='title-only-insufficient']
    found=[]
    for x in manual_60:
        sr=int(float(x['YouTube source row']))
        choices=[]
        for z in cand[sr]:
            album=z['Lidarr album title']
            rel=relationship(x['YouTube album'],album)
            # Same-gated conditions as the proposed C# path; don't accept a
            # candidate as a master, or if Lidarr already records a media file.
            if rel not in {'edition-or-suffix-variant','volume-notation-variant','unicode-glyph-variant'}:continue
            if str(z['Title agrees']).strip().lower() not in ('true','yes'):continue
            if str(z['Trackfile reported']).strip().lower() in ('true','yes'):continue
            choices.append((z['Lidarr album ID'],album,rel))
        if choices: found.append((sr,x['YouTube artist'],x['YouTube album'],choices))
    prior_174=[x for x in songs if x['Action eligibility (proposal only)']=='requires-final-search-approval' and x['Lidarr track ID']=='' and x['Lidarr album ID']!='']
    assert len(prior_174)==174,('Expected album-only correction cohort',len(prior_174))
    assert len({x[0] for x in found})==len(found)
    assert relationship('<|º_º|>','<|°_°|>')=='unicode-glyph-variant'
    assert relationship('The Best of Nickelback, Vol. 1','The Best of Nickelback, Volume 1')=='volume-notation-variant'
    assert relationship('Album (Deluxe Edition)','Album')=='edition-or-suffix-variant'
    assert relationship('Fush Yu Mang','Greatest Hits')=='different-title'
    assert relationship('<|º_º|>','( )')=='different-title' # safeguard against punctuation-erasing equality
    print('v0.9.0.9 OFFLINE REPLAY — CSV-BASED CLASSIFICATION PROJECTION')
    print('Source rows:',len(songs),'baseline manual:',len(review))
    print('Album-only action exporter hold regression:',len(prior_174),'rows identified for fix (no track MBID substitution)')
    print('Related-album choices among 60 title-only manual cases:',len(found))
    print('Projected manual after alias-review planning:',len(review)-len(found))
    print('Alias source rows:', ', '.join(str(a[0]) for a in found))
    print('These alias rows still need explicit album approval; none is exact-recording verified.')
    return 0

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--readiness',required=True);ap.add_argument('--candidates',required=True);ap.add_argument('--plan',required=True)
    a=ap.parse_args()
    try:sys.exit(run(a.readiness,a.candidates,a.plan))
    except Exception as e: print('FAIL:',e,file=sys.stderr);sys.exit(1)
