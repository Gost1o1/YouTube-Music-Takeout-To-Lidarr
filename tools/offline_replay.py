#!/usr/bin/env python3
"""Maintainer-only offline replay of v0.9.0.7.1 inventory evidence against v0.9.0.8.

Inputs: CSVs the user exported (NEVER commit private audit files to a public repo).
No network, no Lidarr requests, no external Python packages, no file modifications.

This is a prospective category-count replay, NOT a successful live .NET test and
NOT a measurement of identity correctness. The C# app is authoritative at runtime.
"""
import argparse
from collections import Counter,defaultdict
import csv
from pathlib import Path
import re


def rows(path):
    with open(path, newline='', encoding='utf-8-sig') as f:
        return list(csv.DictReader(f))


def normalize(text):
    return re.sub(r'[^a-z0-9]', '', text.lower())


def album_core(text):
    text = re.sub(r'\s*[\(\[]\s*(?:deluxe(?:\s+edition)?|expanded(?:\s+edition)?|special\s+edition|remaster(?:ed)?(?:\s+edition)?|bonus\s+tracks?)\s*[\)\]]\s*$', '', text.strip(), flags=re.I)
    text = re.sub(r'\s+(?:deluxe\s+edition|deluxe|expanded\s+edition|ep)\s*$', '', text, flags=re.I)
    return normalize(text)


def classify(row, candidates):
    prior = row['Planned category']
    original = row['Comparison method']
    if original not in ('recording-id-conflict', 'title-only-insufficient'):
        return prior, 'unchanged', False
    if not candidates:
        return prior, 'no-candidates', False
    title = [c for c in candidates if c['Title agrees'] == 'True']
    if not title:
        return prior, 'no-title-evidence', False
    if original == 'recording-id-conflict':
        considered = [c for c in title if c['Source album agrees'] == 'True']
    else:
        considered = [c for c in title if row['YouTube album'] and normalize(row['YouTube album']) != normalize(c['Lidarr album title']) and album_core(row['YouTube album']) and album_core(row['YouTube album']) == album_core(c['Lidarr album title'])]
    if not considered:
        return prior, 'no-confirmed-album-or-edition-relationship', False
    # All returned evidence must report no associated file, or status stays manual.
    if any(c['Trackfile reported'] == 'True' for c in considered):
        return prior, 'file-present-needs-version-check', False
    n = len({c['Lidarr album ID'] for c in considered})
    if original == 'recording-id-conflict':
        return ('To Be Added / Songs (album search target; recording version unverified)' if n == 1 else
                'To Be Added / Songs (album choices pending; recording ID disputed)'), 'conflict-retained-search-plan', True
    return 'To Be Added / Songs (edition choice pending; recording version unverified)', 'explicit-edition-review', True


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--readiness',required=True,type=Path)
    ap.add_argument('--candidates',required=True,type=Path)
    ap.add_argument('--detail-output',type=Path)
    args=ap.parse_args()
    decisions = {r['YouTube source row']:r for r in rows(args.readiness) if r['Type']=='Song'}
    byrow=defaultdict(list)
    for c in rows(args.candidates):
        byrow[c['YouTube source row']].append(c)
    totals = Counter()
    details=[]
    for key,row in decisions.items():
        category, explanation, changed=classify(row,byrow.get(key,[]))
        before=row['Planned category']
        totals['source_rows']+=1
        totals['baseline_manual']+=before.startswith('Manual Review')
        totals['projected_manual']+=category.startswith('Manual Review')
        totals['baseline_to_add']+=before.startswith('To Be Added')
        totals['projected_to_add']+=category.startswith('To Be Added')
        totals['new_album_plans']+=changed
        totals['conflict_album_plans']+=changed and row['Comparison method']=='recording-id-conflict'
        totals['edition_review_plans']+=changed and row['Comparison method']=='title-only-insufficient'
        if changed:
            assert 'To Be Added' in category
            assert explanation in ('conflict-retained-search-plan','explicit-edition-review')
        details.append({'Source row':key,'Before':before,'After (projected)':category,'Reason':explanation,
                        'Source artist':row['YouTube artist'],'Song':row['YouTube song'],'Album':row['YouTube album'],
                        'Review still required before search':'YES',
                        'MusicBrainz recording disagreement accepted':'NO',
                        'Lidarr writes performed':'NO'})
    if args.detail_output:
        with args.detail_output.open('w',encoding='utf-8-sig',newline='') as h:
            w=csv.DictWriter(h,fieldnames=details[0].keys());w.writeheader();w.writerows(details)
    print('Offline evidence replay (prospective, NOT a .NET compilation or a live validation)')
    for k,v in totals.items(): print(f'{k}: {v}')
    assert totals['source_rows']==2505, 'Unexpected source library size; investigate before comparison'
    assert totals['baseline_manual']==609, 'Expected baseline changed; do not compare different source versions'
    assert totals['new_album_plans'] == totals['conflict_album_plans']+totals['edition_review_plans']
    assert totals['projected_manual']+totals['projected_to_add']==totals['source_rows']
    assert totals['projected_manual']<totals['baseline_manual']
    print('PASS: totals, no-write labels, unchanged sources and review-promotion accounting')

if __name__=='__main__':main()
