# Efficient job workflow

## Inspect → resolve plan → build batch → repair affected → verify

Start a fresh chat for a new job when practical. Read the shared modeling rules
and only that job's notes. Preserve sources once, record hashes, then extract
geometry once into an ID-keyed measurement record. Do not assume source list order
matches root IDs. Review manufacturing pieces, native profile families, dimensions,
corner arcs versus holes, configurations and source axes before building affected
parts. Resolve conflicts using standing authorizations; ask only about unresolved
engineering choices. Continue independent ready work.

Keep the checked component plan immutable. Include full part identity, properties,
configuration, source placement and reviewed evidence. Check the plan with
`Invoke-Plan.ps1` before creating production copies. Unknown metadata stays flagged.

Compile typed recipes offline first. Run supported parts through `PartBatch.Run`
in one native call. Keep large diagnostic output in files. Read a compact report:

```powershell
.\sps-workflow\Invoke-Efficiency.ps1 summary .\JOB\batch-checkpoint.json
.\sps-workflow\Invoke-Efficiency.ps1 delta .\JOB\component-plan-v1.json .\JOB\component-plan-v2.json --out .\JOB\delta-v2.json
```

The delta tool is read-only, and checks complete part records and actual file,
source, library and receipt hashes. `retain_candidate` means prior native evidence
may be retained after reconciliation; it does not pass a new plan, rewrite a
receipt, skip a measured Max Modeled Length refresh required by final verification,
or certify completion. A changed placement alone can retain the component file,
but requires fresh connection/assembly review. Changed part records or evidence
require inspection; rebuild only when the diagnosis establishes it is needed.

For a changed plan, retain old receipts and reconcile unchanged parts explicitly
with their original native timestamp, exact complete identity, file hash and
archived prior receipt. Use the established native/completion protocol. No automatic
receipt promotion is provided. Rerun affected components and dependent connections.
Final native assembly and independent dimensions, connection rules, visual and
applicable BOM reviews remain mandatory. Missing reviews remain incomplete.

After a failure, read the exception and inspect the affected feature/reference.
Make a hypothesis and targeted repair before retrying. Preserve failed copies and
checkpoints. After two attempts with the same failure, stop unchanged retries and
diagnose the underlying API/geometry issue; continue independent work. If the
application exits or cannot attach, check process/session status once; do not run
repeated restart loops. Preserve unrelated documents and unsaved work.

## Reusable coverage

| Work | Reuse path | Required limits/checks |
|---|---|---|
| Plain/coped/grooved pipe | `PartRecipes.Pipe` | Actual blank/configuration, supported axis, actual mid-wall guide, stock versus finished length |
| Trimmed native elbow | `PartRecipes.Elbow` | Measured native family, torus geometry, actual source frame |
| Flange/disc and dimension-based bracket edits | `PartRecipes.Fitting` | PDM flange search, reviewed old/target dimensions, protected dimensions and existing tree |
| Bolt pattern coordinates | `geometry.bolt_centers` | Explicit count/PCD/clocking, check actual native hole axes and corner arcs separately |
| Two broad tangent-arc reducer | `geometry.two_arc_reducer_radius` and `reducer_cylinder_guide` | Confirm exact profile family; normal wall offset; explicit root bracket and reviewed parent radius |
| Offset source-axis datum | `geometry.source_axis_radius_point` | Planar radial elbow datum only; preserve actual axis offset, verify torus/weld interface |
| Special bracket topology/reducer sketch replacement | Reviewed job-specific native builder | Preserve native sketches/features; inspect selected edges and rebuilds; do not generalize template-specific sketch names |

Geometry utilities use consistent caller-supplied units; plans/native APIs remain
meters/radians. They calculate reviewed inputs and do not create or certify a native
model. The tested native pipe/elbow/fitting routines remain the production builders.
Recent native special-case examples are in `tmp/1016670-147883/Build1016670.cs`;
that job is stopped. Do not rerun it to exercise the utilities. Promote more native
topology families only with reviewed dimensions, guards and native test evidence.

## Small handoff and measured effort

Copy `examples/JOB-STATE-template.md` into each active job as `JOB-STATE.md`.
Update it at phase changes and before stopping. Include exact plan, checkpoint,
ready/failed parts, next action, unresolved decisions and current app state.
Keep original detailed reports on disk; communicate counts, exceptions and paths.
Do not paste complete feature trees, measurements or successful repeated checks.

Record measured effort per phase when available (do not invent values):

```powershell
.\sps-workflow\Invoke-Efficiency.ps1 event .\JOB inspect --seconds 120 --calls 3 --retries 0 --note "Measured source review"
.\sps-workflow\Invoke-Efficiency.ps1 effort .\JOB
```

`effort-events.jsonl` records elapsed seconds, tool calls and retries, not tokens
or billed credits. Compare similar jobs before claiming savings. Account usage
totals cannot establish an individual phase's exact cost. Keep exposed tools and
sources relevant to the job; change shared connector/settings only on user request.
