#!/usr/bin/env python3
"""Targeted static regression for the CS0103 compile error from v0.9.1.15.
This check is NOT a substitute for native dotnet build.
"""
from pathlib import Path
import re

source = (Path(__file__).resolve().parents[1] / "GuidedReviewV917.cs").read_text(encoding="utf-8")
start = source.index("async Task SendReviewQueue(HttpListenerContext ctx)")
end = source.index("async Task SendReviewDecisionsCsv", start)
method = source[start:end]
assert re.search(r"\bHashSet<string>\s+restored\s*;", method), "restored must be declared outside the lock"
assert re.search(r"\brestored\s*=\s*RestoredManualFallbackKeys\(\);", method), "restored should be assigned before constructing the result"
assert "var restored = RestoredManualFallbackKeys();" not in method, "restored must not be scoped to the lock body"
assert "manualOverride = restored.Contains(i.Key)" in method, "manualOverride must reference the outer variable"
assert method.index("HashSet<string> restored;") < method.index("lock (verificationGate)") < method.index("restored = RestoredManualFallbackKeys();") < method.index("await Respond(")
print("PASS: v0.9.1.15.1 restored-variable scope regression check")
