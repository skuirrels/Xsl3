"""Lists QT3 tests that fail now but are not in the baseline, and summarises failure causes.

    py eng/qt3-regressions.py [top-N]
"""
import collections, pathlib, re, sys

root = pathlib.Path(__file__).resolve().parent.parent
base = {l.strip() for l in open(root / "tests/Xsl3.W3C/baselines/qt3-run.txt", encoding="utf-8") if not l.startswith("#")}
report = root / "tests/Xsl3.W3C/bin/Debug/net10.0/w3c-reports/qt3-run.md"
lines = [l for l in open(report, encoding="utf-8") if l.startswith("- `")]
regressions = [l for l in lines if re.match(r"- `([^`]+)`", l).group(1) not in base]
print(open(report, encoding="utf-8").readline().strip())
print(f"regressions: {len(regressions)}")
for l in regressions[:30]:
    print("  " + l.strip()[:260])
groups, example = collections.Counter(), {}
for l in lines:
    tid, reason = re.match(r"- `([^`]+)`: (.*)", l).groups()
    m = re.search(r"Unknown function Q\{[^}]*\}([\w.-]+#\d+)", reason)
    key = "unknown function " + m.group(1) if m else re.sub(r"\d+", "N", re.sub(r"\[[^\]]*\]", "[…]", re.sub(r"'[^']*'", "'…'", reason)))[:100]
    groups[key] += 1
    example.setdefault(key, tid)
for k, v in groups.most_common(int(sys.argv[1]) if len(sys.argv) > 1 else 30):
    print(f"{v:5} {k}  e.g. {example[k]}")
