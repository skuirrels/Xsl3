"""Summarises the failures in a W3C run report and lists regressions against its baseline.

    py eng/w3c-failures.py qt3-run|xslt30-run [top-N] [set-prefix]
"""
import collections, pathlib, re, sys

root = pathlib.Path(__file__).resolve().parent.parent
name = sys.argv[1] if len(sys.argv) > 1 else "qt3-run"
top = int(sys.argv[2]) if len(sys.argv) > 2 else 30
prefix = sys.argv[3] if len(sys.argv) > 3 else ""
base_file = root / f"tests/Xsl3.W3C/baselines/{name}.txt"
base = {l.strip() for l in open(base_file, encoding="utf-8") if not l.startswith("#")} if base_file.exists() else set()
report = root / f"tests/Xsl3.W3C/bin/Debug/net10.0/w3c-reports/{name}.md"
lines = [l for l in open(report, encoding="utf-8") if l.startswith("- `") and l[3:].startswith(prefix)]
print(open(report, encoding="utf-8").readline().strip())
if base:
    regressions = [l for l in lines if re.match(r"- `([^`]+)`", l).group(1) not in base]
    print(f"regressions: {len(regressions)}")
    for l in regressions[:30]:
        print("  " + l.strip()[:260])
groups, example = collections.Counter(), {}
for l in lines:
    tid, reason = re.match(r"- `([^`]+)`: (.*)", l).groups()
    m = re.search(r"Unknown function Q\{[^}]*\}([\w.-]+#\d+)", reason)
    key = "unknown function " + m.group(1) if m else re.sub(r"\d+", "N", re.sub(r"\[[^\]]*\]", "[…]", re.sub(r"'[^']*'", "'…'", reason)))[:110]
    groups[key] += 1
    example.setdefault(key, tid)
for k, v in groups.most_common(top):
    print(f"{v:5} {k}  e.g. {example[k]}")
