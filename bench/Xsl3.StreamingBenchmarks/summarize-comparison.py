#!/usr/bin/env python3
"""Write a source-backed before/after streaming performance report."""
import argparse
import json
from pathlib import Path
import random
import statistics

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('results',type=Path)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args(); d=json.loads(a.results.read_text())
expected=len(d['method']['sizesMB'])*len(d['method']['workloads'])*4
assert len(d['correctness'])==expected and len(d['summary'])==expected
assert len(d['runs'])==expected*(d['method']['timingRuns']+d['method']['memoryRuns'])
rows={(r['sizeMB'],r['workload'],r['variant'],r['mode']):r for r in d['summary']}
cases=[(s,w) for s in d['method']['sizesMB'] for w in d['method']['workloads']]
control_path=a.results.parent.parent/'sum-control-recheck/results.json'
control=json.loads(control_path.read_text()) if control_path.exists() else None
def winner(size,w):
    if size==1 and w=='sum' and control:
        cr={(r['variant'],r['mode']):r for r in control['summary']}
        if abs(cr['after','stream']['elapsedMs']/cr['after','tree']['elapsedMs']-1)<=.05:
            return 'No meaningful winner (recheck)'
    pairs={r['repetition']:r for r in d['runs'] if r['kind']=='timing' and r['variant']=='after' and r['mode']=='tree' and r['sizeMB']==size and r['workload']==w}
    ratios=[r['elapsedMs']/pairs[r['repetition']]['elapsedMs'] for r in d['runs'] if r['kind']=='timing' and r['variant']=='after' and r['mode']=='stream' and r['sizeMB']==size and r['workload']==w]
    rng=random.Random(20261023)
    boot=sorted(statistics.median(rng.choices(ratios,k=len(ratios))) for _ in range(10000))
    ratio=rows[size,w,'after','stream']['elapsedMs']/rows[size,w,'after','tree']['elapsedMs']
    return 'Streaming' if ratio<.95 and boot[9749]<1 else 'Tree' if ratio>1.05 and boot[249]>1 else 'No meaningful winner'
def filelink(path):return str(path.resolve())
text=['# Streaming performance investigation — 23 October 2026','',
      'The streaming conversion/filtering overhead came largely from transient allocation and repeated simple-content atomization.',
      'Three targeted changes are implemented in the local working copy. Original and optimized binaries were frozen',
      'and interleaved with identical workloads, inputs, warmup and output sinks. This report records the new measurements;',
      'the earlier benchmark report remains the historical baseline.','',
      '## Cause and implementation','',
      '1. Streamed `xsl:value-of` and value-template inputs went through `fn:data`, wrapped every non-text',
      '   item in an immutable XDM array, and later flattened/atomized the wrapper again for simple content.',
      '   The original conversion profile contains `X31SimpleContent → Sequences.Atomize → Sequences.Flatten`',
      '   and immutable-list enumeration. Allocation samples include the arrays, immutable-list nodes and enumerators.',
      '   Normal tree compilation does not introduce that streamed pre-atomization/wrapping round trip.',
      '2. The forward reader allocated temporary attribute and namespace lists for every element, including',
      '   elements with no attributes/namespaces, then copied their contents into arrays.',
      '3. Text coalescing and element string-value reads allocated string builders even when a single existing',
      '   string already represented the complete value. Some text paths also called `ToString()` twice.','',
      'The optimized compiler emits a direct per-item simple-content call. It returns a single atom directly,',
      'consumes untyped node values while current, and retains the existing wrapped canonical path for',
      'zero/multiple typed atoms. Text nodes keep their adjacency; empty typed values/arrays remain boundaries.',
      'The forward reader fills the final attribute array directly, creates namespace lists only when needed,',
      'and uses a string builder only when another text segment must be appended. No whole-document cache,',
      'node recycling or extra read-ahead is introduced. The normal tree compiler keeps its existing path.','',
      'The matching optimized conversion profile no longer contains the immutable-array flattening path',
      'among its captured hot stacks. Allocation-type samples are approximate and CPU sampling on this runtime',
      'often stops in GC polling; the speed/allocation claims below come from separate unprofiled measurements.','',
      '## Execution time','',
      'Medians in milliseconds; Release .NET 10.0.11, macOS ARM64. Lower is better. “Saved” is the reduction',
      'in streaming elapsed time. Winner compares optimized streaming with optimized tree.','',
      '| Input | Workload | Tree before, ms | Tree after, ms | Streaming before, ms | Streaming after, ms | Streaming time saved | Winner now |',
      '|---|---|---:|---:|---:|---:|---:|---|']
for s,w in cases:
 bt,at,bs,ns=[rows[s,w,v,m] for v,m in [('before','tree'),('after','tree'),('before','stream'),('after','stream')]]
 text.append(f'| {s} MB | {w} | {bt["elapsedMs"]:.2f} | {at["elapsedMs"]:.2f} | {bs["elapsedMs"]:.2f} | {ns["elapsedMs"]:.2f} | {100*(1-ns["elapsedMs"]/bs["elapsedMs"]):.1f}% | {winner(s,w)} |')
text+=['','Winners require a median difference over 5% and a 95% paired-bootstrap interval excluding equal',
       'performance (10,000 resamples of the nine after-stream/after-tree ratios; deterministic seed).',
       'This describes the local sample, not all workloads or hardware.','',
       '## Peak process memory and first output','',
       'RSS is the median sampled peak of three separate transform-phase observations. It includes the loaded',
       'engine/runtime, code and committed GC pages. It is not a managed retained-heap bound or allocation total.',
       'First output is the first nonempty serialized byte write, with the usual serializer buffering.','',
       '| Input | Workload | Tree after peak RSS, MB | Streaming before peak RSS, MB | Streaming after peak RSS, MB | Streaming first output before, ms | Streaming first output after, ms |',
       '|---|---|---:|---:|---:|---:|---:|']
for s,w in cases:
 t,b,n=rows[s,w,'after','tree'],rows[s,w,'before','stream'],rows[s,w,'after','stream']
 text.append(f'| {s} MB | {w} | {t["peakRssMB"]:.2f} | {b["peakRssMB"]:.2f} | {n["peakRssMB"]:.2f} | {b["firstOutputMs"]:.3f} | {n["firstOutputMs"]:.3f} |')
text+=['','Conversion first output still arrives after reading 12,288 input bytes; filtering after 49,152 bytes,',
       'for both original and optimized streaming at every size. Sum needs the full input to produce its final result.','',
       '## Allocation and timing variation','',
       'Allocations are cumulative bytes allocated during one transformation, not simultaneously live memory.','',
       '| Input | Workload | Streaming allocation before, MB | Streaming allocation after, MB | Reduction | Original streaming time range, ms | Optimized streaming time range, ms |',
       '|---|---|---:|---:|---:|---:|---:|']
for s,w in cases:
 b,n=rows[s,w,'before','stream'],rows[s,w,'after','stream']
 text.append(f'| {s} MB | {w} | {b["allocatedMB"]:.2f} | {n["allocatedMB"]:.2f} | {100*(1-n["allocatedMB"]/b["allocatedMB"]):.1f}% | {b["minElapsedMs"]:.2f}–{b["maxElapsedMs"]:.2f} | {n["minElapsedMs"]:.2f}–{n["maxElapsedMs"]:.2f} |')
text+=['','## Correctness and compatibility','',
       '- All 36 complete before/after tree/stream output files match independently generated expected output byte for byte;',
       '  thus all four variants agree for each of the nine size/workload cases. SHA-256 values are retained in raw data.',
       '- Full Release unit suites: **1,817 passed on .NET 10; 1,798 on .NET 8**.',
       '- Added regression coverage for empty-atom/text boundaries, singleton and nested arrays, multi-valued and empty',
       '  schema list values, canonical atomization errors, consuming sibling nodes, and saved stylesheet assemblies.',
       '- Upstream CI filter: **1,654 passed** on .NET 10; differential suite: **394 passed**.',
       '  An initial unfiltered upstream run had four known platform-specific failures; the existing CI filter',
       '  (`category!=failing`) excludes those. No baseline or test policy was changed.',
       '- Selected strict-forward W3C sets with saved-assembly sampling: **264 passed, 15 existing failures, zero not run**',
       '  before and after; the complete reports are byte-identical. Three expected errors have the same existing code mismatches.',
       '- NativeAOT saved-stylesheet smoke test passed with dynamic code disabled, correct mixed-text/attribute',
       '  output, early XML-writer delivery before EOF, and borrowed input kept open; publication emitted no warnings.',
       '- New runtime entry point recorded in `PublicAPI.Unshipped.txt`; existing saved-assembly entry points remain intact.','',
       '## Method, scope and reproduction','',
       '- 9 timing observations and 3 independent memory observations for each of four variants and nine cases:',
       f'  **{len(d["runs"])} measured processes**, plus **{len(d["correctness"])}** full-output correctness processes.',
       '- Each child compiles its stylesheet and warms five transformations of the same 65,536-byte corpus before a full GC.',
       '  Timing includes reader creation, parsing, transformation and serialization, excluding compilation/warmup/startup.',
       '- Variants run serially in rotating/reversing order. Timing runs have no sampler or profiler attached.',
       '- Memory runs use an external macOS RSS sampler targeting 1 ms; brief peaks between samples can be missed.',
       '- Exact decimal 1/5/10 MB synthetic files; cache-warm local input and counting/discarding output sink.',
       '- Tiered compilation disabled in every process. Fresh-process/small-input warmup does not model a long-running',
       '  service heap already sized by repeated full-size inputs. Network/output-disk delays are not simulated.',
       '- Unrelated global-binding/emitter changes landed in the shared checkout during this comparison. The whole',
       '  working-tree source fingerprint therefore changed. Both measured DLLs remained frozen; all three optimized',
       '  source files still match their measured-library PDB checksums. The new unrelated changes are preserved',
       '  and are outside this measured/tested build. Binary hashes identify the exact build evaluated here.',
       '- These results preserve the measured memory/latency benefits at 1–10 MB; they do not close P7’s full',
       '  conformance, 2 GB memory or release-readiness gates. Existing unrelated working-copy changes are preserved.','',
       f'- Original library SHA-256: `{d["environment"]["binaries"]["before"]["librarySha256"]}`.',
       f'- Optimized library SHA-256: `{d["environment"]["binaries"]["after"]["librarySha256"]}`.',
       f'- Working-tree source fingerprint at measurement start: `{d["environment"]["candidateSourceSha256"]}`.',
       f'- [Raw measurements]({filelink(a.results)}).',
       f'- [Before profile]({filelink(a.results.parent.parent/"stream-convert-before-profile.json")}), [after profile]({filelink(a.results.parent.parent/"stream-convert-after-profile.json")}).',
       f'- [Validation details]({filelink(a.results.parent.parent/"validation.json")}).',
       f'- [Focused performance patch]({filelink(a.results.parent.parent/"performance-increment.patch")}).',
       f'- [Harness instructions]({filelink(Path(__file__).parent/"README.md")}).','',
       '```sh',
       'python3 bench/Xsl3.StreamingBenchmarks/compare.py \\',
       '  --before-dll artifacts/streaming-investigation-2026-10-23/baseline-bin/Xsl3.StreamingBenchmarks.dll \\',
       '  --after-dll artifacts/streaming-investigation-2026-10-23/optimized-bin/Xsl3.StreamingBenchmarks.dll \\',
       '  --sizes 1,5,10 --timing-runs 9 --memory-runs 3 \\',
       '  --output artifacts/streaming-investigation-2026-10-23/comparison',
       'python3 bench/Xsl3.StreamingBenchmarks/summarize-comparison.py artifacts/streaming-investigation-2026-10-23/comparison/results.json --output docs/streaming-performance-investigation-2026-10-23.md',
       '```','']
for s,w in cases:
 for v in ['before','after']:
  r=rows[s,w,v,'stream']
  expected_bytes=12288 if w=='convert' else 49152 if w=='filter' else s*1000000
  assert r['inputBytesAtFirstOutput']==expected_bytes
for s,w in cases:
 assert len({r['sha256'] for r in d['correctness'] if r['sizeMB']==s and r['workload']==w})==1
a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text('\n'.join(text));print(a.output)
if control:
    cr={(r['variant'],r['mode']):r for r in control['summary']}
    note=('\n## Small-sum control recheck\n\n'
          'The initial 1 MB sum tree control varied enough to affect its apparent winner. A separate\n'
          'follow-up of 27 timing observations per variant repeated just this case with the\n'
          'same binaries/corpus/settings. Original tree: '
          f'{cr["before","tree"]["elapsedMs"]:.3f} ms; optimized tree: {cr["after","tree"]["elapsedMs"]:.3f} ms. '
          'Original streaming: '
          f'{cr["before","stream"]["elapsedMs"]:.3f} ms; optimized streaming: {cr["after","stream"]["elapsedMs"]:.3f} ms.\n'
          'The tree-control difference did not persist; the after-stream/after-tree difference is below\n'
          'the 5% practical threshold, so this case has no meaningful winner. The primary table keeps\n'
          'the original matrix medians and explicitly uses the recheck for its winner label.\n\n'
          'The harness DLL hashes differ after recompiling against the changed reference assembly.\n'
          'All 48 method bodies, method signatures, local signatures and PDB source document checksums\n'
          'were independently compared and are identical; the differences are build/debug identity metadata.\n\n'
          f'[Raw control recheck]({filelink(control_path)}).\n')
    with a.output.open('a') as output:output.write(note)
