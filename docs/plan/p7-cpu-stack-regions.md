# P7 CPU stack-region attribution

The original CPU report retains whole-capture samples. Compilation, warmup, transformation and the
independent output oracle can all occur while collection is active. These samples must not all be
attributed to timed transformation work.

`eng/StreamingCpuReport` now classifies each complete stack before truncating its displayed engine
path: output oracle, transformation entry, fork worker, or unattributed. It preserves every original
sample and the aggregate report, and adds per-region inclusive counts. Unresolved samples are retained.
These are **stack regions, not exact measured-phase windows**; the transform regions may include warmup.
No sample share establishes removable elapsed time. No runtime, corpus, oracle or gate changed.

## Replay validation

Both original traces were copied before analysis. Replaying the new tool preserves every legacy
process/thread/leaf/engine sample count, event count, engine-frame total, lost-event count and global
inclusive row. Regional and site totals equal the original CPU sample count. Both captures report
zero lost events.

| Capture | Transform entry | Fork workers | Output oracle | Unattributed | Total |
|---|---:|---:|---:|---:|---:|
| Fork, ordinary ReadyToRun policy | 21,639 | 37,052 | 991 | 11,427 | 71,109 |
| Identity, framework ReadyToRun disabled | 25,783 | 0 | 0 | 560 | 26,343 |

The fork capture is [42129815135](https://github.com/skuirrels/Xsl3/actions/runs/42129815135),
corrected-discard source `a8d322a`. Of its 1,366 SHA-compression leaf samples, **634** occur in the
independent output oracle. Those are outside the timed transform and do not justify engine changes.
The identity capture is [42124527290](https://github.com/skuirrels/Xsl3/actions/runs/42124527290):
all 3,858 compression leaf samples are in the transform-entry region; this distinction must not be
assumed from one workload for another. Its disabled framework ReadyToRun policy remains a supplemental
symbol diagnostic and is not ordinary performance evidence.

This refinement does not establish a throughput improvement or change the rejected controls' decisions.
The fork trace still contains substantial unresolved and scheduling samples. Use the complete-stack
categories when selecting a new runtime hypothesis, then verify it with unchanged paired consumers.

Evidence: `/private/tmp/xsl3-p7-cpu-regions-20261106/` retains the tool source/binary, copied traces,
new reports, conservation audits and hashes. Original artifacts and reports remain unchanged.
