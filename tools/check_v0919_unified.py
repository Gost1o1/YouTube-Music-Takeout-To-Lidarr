#!/usr/bin/env python3
"""Source-level and CSV baseline tests. No .NET compiler or internet required."""
from pathlib import Path
from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Comment,String
import csv,re,subprocess,tempfile
root=Path(__file__).resolve().parents[1]
files=list(root.glob('*.cs'))
assert len(files)>=15, len(files)
for f in files:
 stack=[]
 for typ,val in lex(f.read_text(),CSharpLexer()):
  if typ in Comment or typ in String: continue
  for c in val:
   if c in '([{':stack.append(c)
   elif c in ')]}':
    assert stack and '([{'.index(stack.pop())==')]}'.index(c),f
 assert not stack,f
prog=(root/'Program.cs').read_text(); pipeline=(root/'UnifiedPipelineV919.cs').read_text(); verification=(root/'Verification.cs').read_text(); ui=(root/'AppUI/index.html').read_text(); recovery=(root/'IdentifierRecovery.cs').read_text()
assert 'BeginUnifiedPipeline(automaticSource)' in prog
assert 'case "/api/pipeline/status":' in prog
assert 'case "/api/pipeline/start":' in prog
assert 'RunUnifiedPipelineAsync' in pipeline
assert pipeline.index('await VerifyTracksAsync(')<pipeline.index('await RunSupplementalAsync(')<pipeline.index('await RunIdentifierRecoveryAsync(')<pipeline.index('await RunLidarrComparisonAsync(')
assert 'ResetUnifiedCache();' in pipeline and 'MaxCacheBytes' in pipeline and 'MaxCacheEntries' in pipeline
assert 'sharedMbPacer' in verification and 'sharedMbPacer' in recovery
assert 'IncrementUnifiedCacheHit()' in verification
assert 'RetryAfter' in pipeline and 'status == 429' in pipeline
assert 'if (!previousSuccess) AddEvidence' in verification, 'Duplicate songs may not count as independent credits'
assert '&& mbResponseCache.TryAdd(url, body)' in pipeline
assert 'p.failed' in ui and 'p.done' in ui
assert 'if(s===6)c=unifiedProgressPage();' in ui
assert 'unifiedPolling=true;pollUnifiedPipeline()' in ui
assert 'const BUILD_VERSION=\'v0.9.1.15\'' in ui
assert 'Already completed a previous scan?' not in ui
assert 'Current stage</span>' not in ui
assert 'All Candidate Scores and Album Evidence' not in ui.split('if(s===6)c=unifiedProgressPage();')[0]
assert 'verification.Running' in prog
assert 'stage-albums' in (root/'AppUI/flow918.js').read_text()
assert 'No Lidarr searches or downloads were started.' in prog
for script in ['AppUI/flow918.js','tools/offline_v0918_ui_smoke.js','tools/offline_v0919_progress_smoke.js']:
 subprocess.run(['node','--check',str(root/script)],check=True,capture_output=True)
with tempfile.NamedTemporaryFile(mode='w',suffix='.js') as tmp:
 tmp.write(re.search(r'<script>([\s\S]*?)</script>',ui).group(1));tmp.flush()
 subprocess.run(['node','--check',tmp.name],check=True,capture_output=True)
subprocess.run(['node',str(root/'tools/offline_v0918_ui_smoke.js')],check=True)
subprocess.run(['node',str(root/'tools/offline_v0919_progress_smoke.js')],check=True)
audit=Path('/mnt/data/musicbrainz-songs-audit (19).csv'); artists=Path('/mnt/data/musicbrainz-artists-audit (19).csv')
if audit.is_file() and artists.is_file():
 with audit.open(newline='',encoding='utf-8-sig') as f:songs=list(csv.DictReader(f))
 with artists.open(newline='',encoding='utf-8-sig') as f:arts=list(csv.DictReader(f))
 assert len(songs)==2505 and len(arts)==951
 high=sum(a['Confidence']=='high' for a in arts)
 assert high==882,high
 print('PASS: latest saved CSV benchmark has 2,505 songs, 951 artists, 882 high-confidence artists')
print(f'PASS: {len(files)} C# structural checks, automatic pipeline routes, shared cache/ratelimiter, safeguards, UI syntax and two UI smoke tests')
