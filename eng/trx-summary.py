"""Groups failed tests in a .trx file by the first line of their error message.

    py eng/trx-summary.py <file.trx> [max-groups]
"""
import collections
import re
import sys
import xml.etree.ElementTree as ET

ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(sys.argv[1]).getroot()
limit = int(sys.argv[2]) if len(sys.argv) > 2 else 30
groups = collections.defaultdict(list)
for r in root.iterfind(".//t:UnitTestResult", ns):
    if r.get("outcome") != "Failed":
        continue
    msg = r.findtext(".//t:Message", default="", namespaces=ns).strip().splitlines()
    first = re.sub(r"'[^']*'|\"[^\"]*\"", "'…'", msg[0] if msg else "")[:160]
    groups[first].append(r.get("testName"))
for msg, tests in sorted(groups.items(), key=lambda kv: -len(kv[1]))[:limit]:
    print(f"{len(tests):5}  {msg}\n       e.g. {tests[0][:150]}")
