"""Offline source hygiene checks. This is not a C# compiler."""
from pathlib import Path
import csv, collections, re, sys
root=Path(__file__).resolve().parents[1]
cs=list((root/'src').glob('*.cs'))
assert (root/'src/IsrcTitleReconciliation.cs').exists()
assert (root/'src/IdentifierRecovery.cs').exists()
assert len(cs)>=10
mb=(root/'src/IdentifierRecovery.cs').read_text()
title=(root/'src/IsrcTitleReconciliation.cs').read_text()
ui=(root/'AppUI/index.html').read_text()
ci=(root/'.github/workflows/build-windows.yml').read_text()
assert 'inc=artist-credits&fmt=json' in mb
assert 'Evalua' in mb and 'EvaluateRecoveryTitle' in title
assert 'musicbrainz-artist-credits-missing' in mb
assert 'isrc-artist-name-agrees-mbid-unverified' in mb
assert 'isrc-version-conflict-needs-review' in mb
assert 'Matching display name may NOT override a conflicting known MBID' in mb
assert 'featured-artist-not-in-mb-credits' in mb
assert 'version-type-conflict' in title
assert 'version-details-uncertain' in title
assert "'src/**'" in ci
assert 'Prepare Your YouTube Music Export' in ui and 'Connect to Lidarr' in ui
assert 'does not download music' in (root/'README.md').read_text()
# Assert base coverage of the user's latest files, not a simulated live match.
source=Path('/mnt/data/musicbrainz-songs-audit (12).csv')
manual=Path('/mnt/data/lidarr-manual-review-triage (1).csv')
if source.exists() and manual.exists():
 with source.open(encoding='utf-8-sig',newline='') as f: songs=list(csv.DictReader(f))
 with manual.open(encoding='utf-8-sig',newline='') as f: reviews=list(csv.DictReader(f))
 assert len(songs)==2505 and len(reviews)==309
 print('Latest fixture: 2,505 source songs; 309 prior manual-review rows')
print(f'PASS offline static source checks: {len(cs)} C# files; no native compilation performed')
