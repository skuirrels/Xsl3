# Phase 7 timed Linux profile

**Decision.** The four captures met the diagnostic criteria. The evidence does not establish a
single runtime change with enough headroom to close both 100 MB throughput gaps.

## Uninstrumented controls

The controls and the four captures ran serially on one Ubuntu 26.04.1 x64 runner with four logical
CPUs and .NET 10.0.15. The engine was `v1.3.4` at `915ad884008e7e3d03c11a5f167d65494fdeea95`.
The workflow verified that the control and profiling consumers loaded byte-identical engine
assemblies. The profiling consumer adds markers immediately around the same measured invocation
and output disposal. Each case used the canonical 100,000,000-byte input, the effective 4 GiB
managed heap guard and disabled tiered compilation. [Linux run and raw traces](https://github.com/skuirrels/Xsl3/actions/runs/44184167239)

| Workload | Tree median (ms) | Stream median (ms) | Stream minus tree (ms) | Stream/tree |
| --- | ---: | ---: | ---: | ---: |
| Identity | 4,189.45 | 4,457.76 | +268.31 | 1.064× |
| Source-document | 4,133.58 | 4,457.98 | +324.41 | 1.078× |

Each median uses three fresh uninstrumented processes per mode. The tree and stream ranges were
disjoint on this host for both workloads. Break-even would require reducing streaming elapsed
time by about 6.0% for identity and 7.3% for source-document. Each of the 12 controls and four
captures matched the preserved v1.3.4 fingerprints, including 869,565 records and 109,565,215
principal output bytes per observation. [Original v1.3.4 acceptance run](https://github.com/skuirrels/Xsl3/actions/runs/43608663038)

## Timed CPU captures

The begin and end events came from each benchmark child on the invoking thread. All four marker
intervals agreed with the child's stopwatch to within 0.08 ms. Independent reparsing reproduced
the hosted reports byte for byte. All 21,490 CPU samples reconciled against the trace totals,
including 17,028 inside the timed intervals. Each trace reported zero lost events.

| CPU samples by nearest recognized stack frame | Identity tree | Identity stream | Source-document tree | Source-document stream |
| --- | ---: | ---: | ---: | ---: |
| Tree | 2,091 | 0 | 2,339 | 0 |
| Reader | 0 | 1,118 | 0 | 1,181 |
| Stream event processing | 0 | 713 | 0 | 653 |
| Stream navigation | 0 | 495 | 0 | 491 |
| Output | 1,469 | 1,297 | 1,445 | 1,337 |
| GC | 277 | 164 | 283 | 153 |
| Other engine | 371 | 376 | 379 | 384 |
| Unattributed | 2 | 2 | 3 | 5 |
| **Total** | **4,210** | **4,165** | **4,449** | **4,204** |

A zero means the classifier assigned no samples to that row. For example, the tree category
includes parser work attributed through tree construction. Some leaf addresses were unresolved
and remained in the counts through their visible parent frames. The markers exclude warmup
and the independent output oracle. The output category includes hashing performed by the sink
during the measured invocation.

The streaming captures recorded fewer total on-CPU samples than tree in both workloads even
though the uninstrumented streaming medians were slower. `ReadNextCore` had 198 and 222 direct
samples in the two streaming captures, but spans several operations. Its inclusive counts also
cover parser work. The narrower `CopyNamespaces` and `XsltCopyOfStrippedAttribute` paths had
108/103 and 140/127 inclusive samples respectively. Those counts do not support a single
removable cost that can close both gaps. One capture per mode cannot resolve the contribution
of scheduling or run-to-run variation to elapsed time.

The first attempt exposed a report directory ownership error and missed three begin markers.
After fixing directory ownership and allowing the collector to start, the second attempt
missed one end marker. Keeping the process alive briefly after the end marker resolved that
failure. The final controls and all four analyzed captures came from one runner.
[First attempt](https://github.com/skuirrels/Xsl3/actions/runs/44182268600) · [Second attempt](https://github.com/skuirrels/Xsl3/actions/runs/44183236237)

Streaming remains experimental and opt-in. Production source, canonical fixtures, benchmark
controllers and the original acceptance workflow outside the manual CPU job match v1.3.4.
The original acceptance criteria still require all 32 memory budgets, all 126 benchmark
fingerprints, and seven 100 MB stream/tree ratios of at most 1.0.
