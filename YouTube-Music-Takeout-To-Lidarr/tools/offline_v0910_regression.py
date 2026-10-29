#!/usr/bin/env python3
"""Read-only C# album-rule regression replay using the v0.9.0.9 exported CSVs.
No network, no MusicBrainz/Lidarr calls, no changes to source library.
"""
import argparse, csv, collections, re, pathlib

def rows(path):
    with open(path,encoding='utf-8-sig',newline='') as f: return list(csv.DictReader(f))

def normalize(s): return ''.join(x.lower() for x in s if x.isalnum())

def drop_ep(s):
    return re.sub(r'\s*(?:\(\s*e\.?p\.?\s*\)|\[\s*e\.?p\.?\s*\]|\b(?:e\.?p\.?|extended\s+play))\s*$', '',s.strip(),flags=re.I).strip()

def ep_relation(a,b):
    x,y=drop_ep(a),drop_ep(b)
    return len(normalize(x))>=5 and normalize(x)==normalize(y) and (x!=a.strip() or y!=b.strip())

def run(readiness,candidates,plan):
    allrows=rows(readiness); cand=rows(candidates); planrows=rows(plan)
    songrows=[r for r in allrows if r['Type']=='Song']
    assert len(songrows)==2505
    assert len(planrows)==2505
    byrow=collections.defaultdict(list)
    for c in cand: byrow[c['YouTube source row']].append(c)
    review=[r for r in songrows if r['Planned category'].startswith('Manual Review')]
    ep_recoverable=[]
    for r in review:
        candidatesForSong=byrow[r['YouTube source row']]
        compatible=[]
        for c in candidatesForSong:
            if not ep_relation(r['YouTube album'],c['Lidarr album title']):continue
            if c['Title agrees'].lower() not in ('true','yes'):continue
            if c['Trackfile reported'].lower() in ('true','yes'):continue
            compatible.append(c)
        if compatible:
            ep_recoverable.append((r['YouTube source row'],r['YouTube artist'],r['YouTube song'],r['YouTube album'],sorted(set(c['Lidarr album title'] for c in compatible))))
    print('Baseline manual rows:',len(review))
    print('Explicit EP variant release-choice candidates:',len(ep_recoverable))
    for row in ep_recoverable[:30]:print('  ',row)
    for artist,song in [('Skrillex','Summit'),('Skrillex','Bangarang'),('Skrillex',"Breakn' a Sweat"),('Green Day','21 Guns'),('Krewella','Alive')]:
        assert any(x[1].lower()==artist.lower() and x[2].lower()==song.lower() for x in ep_recoverable),(artist,song)
    assert not ep_relation('Bangarang EP','Triple Vinyl Box Set')
    assert not ep_relation('EP','Something Else')
    assert ep_relation('Bangarang EP','Bangarang') and ep_relation('21 Guns E.P.','21 Guns') and ep_relation('Play Hard (EP)','Play Hard')
    stageable=[r for r in songrows if r['Planned category'].startswith('To Be Added')
        and r['Action eligibility (proposal only)'] in ('eligible-after-user-approval','requires-final-search-approval')
        and r['Lidarr track ID'] and not r['Lidarr trackfile ID']
        and r['Recording MBID disputed'].lower()=='false' and r['Lidarr album ID']]
    grouped=collections.Counter(r['Lidarr album ID'] for r in stageable)
    print('Stageable source rows (not searches):',len(stageable))
    print('Unique stageable album IDs:',len(grouped))
    assert all(r['Recording MBID disputed'].lower()=='false' for r in stageable)
    print('PASS: EP-specific conservative review candidates, rejected unrelated titles, staged album IDs, no file writes')
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--readiness',required=True);p.add_argument('--candidates',required=True);p.add_argument('--plan',required=True);a=p.parse_args()
    run(a.readiness,a.candidates,a.plan)
