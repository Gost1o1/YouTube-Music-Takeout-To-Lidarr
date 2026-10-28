#!/usr/bin/env python3
"""Offline source and UI wiring checks; not a C# compiler substitute."""
from pathlib import Path
import re, subprocess, tempfile, csv, sys
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Comment, String
root=Path(__file__).resolve().parents[1]
cs=sorted((root/'src').glob('*.cs'))
assert len(cs)>=13, f'Expected 13+ complete C# files: {len(cs)}'
for file in cs:
    stack=[]
    for token,content in lex(file.read_text(encoding='utf-8'),CSharpLexer()):
        if token in Comment or token in String: continue
        for ch in content:
            if ch in '([{': stack.append(ch)
            elif ch in ')]}':
                assert stack and '([{'.index(stack.pop())==')]}'.index(ch), (file.name,ch)
    assert not stack, file.name
program=(root/'src/Program.cs').read_text()
new=(root/'src/GuidedReviewV917.cs').read_text()
ui=(root/'AppUI/index.html').read_text()
for route in ['/api/review/queue','/api/review/decision','/api/review/decisions.csv','/api/review/reapply-album','/api/review/forget-album']:
    assert f'case "{route}":' in program, route
for name in ['SaveReviewDecision', 'SendReviewQueue', 'SendReviewDecisionsCsv', 'SaveAlbumReviewBookmark',
             'ReapplySavedAlbumReview', 'ForgetAlbumReview', 'GuidedReviewSelfTests', 'SaveReviewLedger']:
    assert name in new, name
assert 'GuidedReviewSelfTests();' in (root/'src/SupplementalEvidence.cs').read_text()
assert 'SaveAlbumReviewBookmark(savedGroup, requestedAlbumId)' in program
assert 'GetAlbumReviewBookmark(g)?.SelectedCandidate' in program
assert 'ReviewKey("song", ReviewSongSource(' in new
assert 'stale-candidate-requires-review' in new
assert 'candidate-for-planning' in new and 'NO - user review only' in new
assert 'ReviewLedgerPath' in new and 'File.Move(tmp, path, true)' in new
assert 'candidate-for-planning' in new and 'auditConfidenceChanged = false' in program
assert 'v0.9.1.7' in ui and 'Load Guided Review Queue' in ui
assert 'reapplySavedAlbum' in ui and 'forgetSavedAlbum' in ui
assert 'exportReviewDecisions' in ui
assert '503-retry:' in (root/'src/ReleaseDiscoveryV915.cs').read_text()
assert 'ParsePreviousCandidateScores' in new and 'candidateScoresCsv' in program and 'previous-candidate-scores' in ui
assert "'src/**'" in (root/'.github/workflows/build-windows.yml').read_text()
# Inline HTML JS parser check; no dynamic imports or server dependencies.
match=re.search(r'<script>([\s\S]*?)</script>', ui)
assert match, 'UI script absent'
with tempfile.NamedTemporaryFile(suffix='.js',mode='w',encoding='utf8') as f:
    f.write(match[1]); f.flush()
    subprocess.run(['node','--check',f.name],check=True)
print(f'PASS: {len(cs)} C# files lexically balanced; new UI syntax; API routes; persisted-review integrity checks; Windows CI and 503 diagnostics')
