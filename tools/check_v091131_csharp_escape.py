"""Regression for CS1009 at ManualFallbackV912.cs lines 146, 147, 151.
This is a focused source test, not a substitute for compiling with .NET 8.
"""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[1]
cs = (root / 'ManualFallbackV912.cs').read_text(encoding='utf-8')
# These three C# literals must contain TWO source backslashes, yielding one
# literal backslash in the actual artist name after C# compilation.
valid = '"Axwell /' + '\\' * 2 + ' Ingrosso"'
invalid = '"Axwell /' + '\\' + ' Ingrosso"'
assert cs.count(valid) == 3, 'All three artist-name test literals must use \\\\.'
assert invalid not in cs, 'CS1009 invalid escape sequence reintroduced.'
page = (root/'AppUI/index.html').read_text(encoding='utf-8')
ui = (root/'AppUI/flow918.js').read_text(encoding='utf-8')
match = re.search(r"const BUILD_VERSION='(v[0-9.]+)'", page)
assert match, 'Missing application build version'
assert match.group(1) in ui, 'App and review overlay versions disagree'
print('PASS: all three C# artist-name escapes repaired; UI version synchronized')
