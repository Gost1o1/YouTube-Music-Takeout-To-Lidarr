#!/usr/bin/env python3
from pathlib import Path
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Comment, String
r=Path(__file__).parents[1]
for filename in ('CatalogCorroboration.cs','IdentifierRecovery.cs','Program.cs'):
    content=(r/filename).read_text(encoding='utf-8')
    stack=[]
    for token,text in lex(content,CSharpLexer()):
        if token in Comment or token in String: continue
        for char in text:
            if char in '([{':stack.append(char)
            if char in ')]}':
                assert stack and '([{'.index(stack.pop())==')]}'.index(char), (filename,char)
    assert not stack, filename
ui=(r/'AppUI/index.html').read_text(encoding='utf8')
program=(r/'Program.cs').read_text(encoding='utf8')
recovery=(r/'IdentifierRecovery.cs').read_text(encoding='utf8')
tests=(r/'SupplementalEvidence.cs').read_text(encoding='utf8')
assert 'exportCandidateScores()' in ui and '/api/recovery/candidate-scores.csv' in ui
assert 'case "/api/recovery/candidate-scores.csv":' in program
assert 'await RunCandidateArtistChecksAsync' in recovery
assert 'await RunWholeAlbumChecksAsync' in recovery
assert 'CatalogCorroborationSelfTests();' in tests
assert 'candidate scores' in (r/'README.md').read_text().lower() or 'candidate' in (r/'README.md').read_text().lower()
assert "'CatalogCorroboration.cs'" in (r/'.github/workflows/build-windows.yml').read_text()
print('PASS: static C# brackets, candidate handler/UI wiring, stage calls, self-test registration, Windows CI')
