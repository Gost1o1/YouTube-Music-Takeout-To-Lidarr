#!/usr/bin/env python3
"""Offline scope/safety replay; requires exported audits, does NOT contact providers.

This determines which unresolved cohorts the live targeted MusicBrainz pass will inspect.
It does not simulate remote candidate matches or claim improved counts.
"""
import argparse, csv, collections, json, re
from pathlib import Path

def norm(x): return re.sub(r'[^\w]', '', x.casefold())
def load(path):
    with path.open(encoding='utf-8-sig',newline='') as f: return list(csv.DictReader(f))
def report(base):
    songs=load(base/'musicbrainz-songs-audit (14).csv')
    artists={x['Artist']:x for x in load(base/'musicbrainz-artists-audit (14).csv')}
    manual=load(base/'lidarr-manual-review-triage (3).csv')
    assert len(songs)==2505 and len(artists)==951 and len(manual)==299
    assert sorted(int(x['Source row']) for x in songs)==list(range(1,2506))
    cohorts=collections.defaultdict(list)
    for x in songs:
        artist=artists.get(x['YouTube artist'],{})
        if x['Song identity status (separate from exact recording)']=='artist-and-song-supported':continue
        if artist.get('Confidence')!='high':continue
        if not re.fullmatch(r'[a-fA-F0-9]{8}-(?:[a-fA-F0-9]{4}-){3}[a-fA-F0-9]{12}',artist.get('Artist MBID','')):continue
        if x['YouTube album'].strip() in ('','-'):continue
        if not norm(x['YouTube song']):continue
        cohorts[(norm(x['YouTube artist']),norm(x['YouTube album']))].append(x)
    ranked=sorted(cohorts.values(),key=lambda g:(-sum(x['External catalog evidence'] in ('one-catalog-supports-title-and-artist','both-catalogs-support-title-and-artist') for x in g),-len(g),norm(g[0]['YouTube artist']),norm(g[0]['YouTube album'])))
    selected=ranked[:160]
    selected_rows={int(x['Source row']) for g in selected for x in g}
    manual_rows={int(x['Source row']) for x in manual}
    assert len(selected_rows)==sum(len(g) for g in selected)
    return {
        'full_source_rows':len(songs), 'artist_rows':len(artists), 'previous_manual_rows':len(manual),
        'eligible_unresolved_cohorts':len(cohorts),
        'targeted_cohorts_selected':len(selected),
        'unresolved_source_rows_eligible_for_targeted_checks':len(selected_rows),
        'selected_rows_also_in_previous_manual_review':len(selected_rows&manual_rows),
        'externally_corroborated_selected_rows':sum(x['External catalog evidence'] in ('one-catalog-supports-title-and-artist','both-catalogs-support-title-and-artist') for g in selected for x in g),
        'provider_calls_executed':0,
        'new_verified_recordings_claimed':0,
        'note':'Offline eligibility only. Live MusicBrainz release/track checks and Windows build still required.'
    }
if __name__=='__main__':
    a=argparse.ArgumentParser();a.add_argument('--data-dir',required=True);a.add_argument('--output')
    args=a.parse_args();data=report(Path(args.data_dir));print(json.dumps(data,indent=2))
    if args.output:Path(args.output).write_text(json.dumps(data,indent=2)+'\n')
