#!/usr/bin/env python3
"""Replay v0.9.1.3 candidate gates against previously exported private audits.

This is diagnostic/offline classification testing. It cannot demonstrate that
MusicBrainz will return any particular current network response.
"""
import argparse
import csv
import json
import re
from collections import Counter, defaultdict
from pathlib import Path


def read(p):
    with open(p, encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))


def norm(s):
    return ''.join(c for c in s.casefold() if c.isalnum())


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--data-dir', default='/mnt/data')
    p.add_argument('--output', default='offline-replay-v0.9.1.3.json')
    args = p.parse_args()
    directory = Path(args.data_dir)
    artists = read(directory / 'musicbrainz-artists-audit (13).csv')
    songs = read(directory / 'musicbrainz-songs-audit (13).csv')
    readiness = read(directory / 'migration-readiness-preview (9).csv')
    assert len(artists) == 951, len(artists)
    assert len(songs) == 2505
    assert sum(r["Type"] == "Song" for r in readiness) == 2505
    review = [a for a in artists if a['Confidence'] != 'high']
    assert len(review) == 71, len(review)
    groups = defaultdict(list)
    for song in songs:
        if song['Song identity status (separate from exact recording)'] != 'artist-and-song-supported':
            key = (norm(song['YouTube artist']), norm(song['YouTube album']))
            if key[0] and key[1] and song['YouTube album'].strip() != '-':
                groups[key].append(song)
    album_groups = {k:v for k,v in groups.items() if len(set(norm(r['YouTube song']) for r in v)) >= 2}
    artist_candidates = {}
    skipped_wide = []
    for a in review:
        ids = set(re.findall(r'[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}',
            (a['Artist ID candidates'] or '') + '|' + (a['Lidarr MBID candidate'] or '') + '|' + (a['Artist MBID'] or '')))
        if len(ids) > 4:
            skipped_wide.append(a['Artist'])
        elif ids and a['Artist'].strip() not in ('', '-'): artist_candidates[a['Artist']] = len(ids)
    top = sorted(album_groups.values(), key=lambda rows: (-len(rows),norm(rows[0]['YouTube artist'])))[:24]
    # These gates matter more than speculative percentage improvements.
    checks = {
        'source_artist_recording_credit_required': True,
        'two_distinct_recordings_or_one_with_independent_lidarr_mbid': True,
        'artist_candidate_score_margin_required_if_multiple': True,
        'different_album_editions_do_not_auto_promote': True,
        'live_studio_or_different_venue_is_hard_hold': True,
        'release_inventory_must_be_complete_and_bounded_for_auto_corroboration': True,
        'existing_conflicting_artist_or_recording_ids_are_not_overridden': True,
        'lidarr_writes_disabled': True,
    }
    result = {
        'baseline': {'source_artists': len(artists), 'source_songs': len(songs), 'artists_review': len(review),
                     'songs_review': sum(r['Type']=='Song' and r['Planned category'].startswith('Manual Review') for r in readiness)},
        'artist_candidate_bounded_lookup': {'eligible_artist_names': len(artist_candidates),
                                            'artists_with_more_than_four_candidates_skip_auto': len(skipped_wide),
                                            'examples': list(artist_candidates.items())[:18]},
        'album_group_bounded_lookup': {'multi_song_unresolved_groups': len(album_groups),
            'max_groups_in_one_run': len(top), 'examples': [
                {'artist': x[0]['YouTube artist'], 'album': x[0]['YouTube album'], 'songs':len(x)} for x in top[:14]]},
        'hard_safety_checks': checks,
        'expected_extra_auto_confirmations': 'Unknown until live candidate replies; no counts inflated from CSV alone'
    }
    Path(args.output).write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
    print(json.dumps(result,indent=2,ensure_ascii=False))

if __name__=='__main__': main()
