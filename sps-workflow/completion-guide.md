# Steps 4–8: verification, properties, batching and completion

Use these routines for future jobs. They do not retrofit completed models or
modify library originals. Start with the component plan and reviewed native
recipes. Final job-specific engineering checks remain necessary.

## Step 4: check the features actually used

Recipes now build explicit `PartExpectations`. The final verification checks
the intended feature is active and of the required native type, follows its
actual referenced/absorbed sketches, and checks round-opening radii and both
profile endpoints. It verifies the cope's actual guide reference, the groove
and inner-displacement features and cylindrical radii, and the approved double-D
profile's eight edges, two lines, four corner arcs and two main arcs. Native
dimension targets and protected dimensions are checked again after all features.

The verifier walks feature/subfeature errors recursively. Historical unused
sketches cannot satisfy an expected active feature. Save warnings and unknown
metadata remain visible. This does not establish arbitrary cut clearance through
unmodeled geometry; use the actual bore/interior-stop guards and job review.

## Step 5: fill known properties consistently

`MetadataPolicy` and `ApplyProperties` maintain the 14 required Text properties
and configuration overrides. MakeOrBuy defaults to M; explicit known values are
preserved. Pipes retain their stock ID and material and use the actual native
stock-length link. Pipe descriptions with verified native length expressions
can be inherited and adapted to the new filename. RD1 annotation-length links
become the copied blank's D1 length link. Material links use the current file.

Part-length property values use **at most three decimal places**. The helper
sets the owned copy's decimal length display precision to three, preserving its
unit choices and full model dimensions. Linked quantities/descriptions stay
linked. Literal stock/cut/finished lengths and explicitly labeled descriptive
lengths use `PropertyLengthFormat` (for example 1.9375 becomes 1.938). Other
dimensions, stock identifiers and tolerances in description text are preserved.
The document's other displayed linear dimensions also use this three-decimal
unit precision; their actual geometric values are unchanged. For custom job code,
use `PropertyLengthFormat.Format(lengthInPropertyUnits)` instead of formatting
length text manually. Native verification rejects excess decimal places in
linked pipe quantities and the dedicated length-property fields.

For coped pipes, **Part Description uses Max Modeled Length**, measured from the
final solid, while Stock Length and MaterialQty retain stock length separately.
The verifier refreshes this measurement before saving. See the recipe guide for
the custom-builder flags and supported local-axis measurement.

Customer/job/drafter/date/revision values are never borrowed from a library
template. Fill those from known job evidence. Modified fitting descriptions must
describe the modified geometry; an old elbow angle or flange size is not safe
to inherit. Missing fitting ERP mappings remain blank and flagged; template
stock identity remains provenance. A replacement stock mapping needs reviewed
evidence. The completion checker compares supplied known values, including
MakeOrBuy, against the verified part record. Description, material and quantity
links receive native checks and still need review against the plan/drawing.

## Step 6: verify every completed part before continuing

Each recipe now calls `CompletePart`, which checks the solid, final expected
features/dimensions/radii/configuration/material, all 14 properties, resolved
links and configuration overrides, then saves and closes the component. It
writes `<part>.SLDPRT.verification.json` with the final file hash and the results.
The record is created only after a successful save and close. Existing records
are not overwritten. Failed parts remain available for diagnosis and cannot
produce a passed record through the completion routine.

For actual job handoffs, populate these `RecipeSource` fields:

```csharp
source.PlanFile = reviewedPlanPath;
source.PlanSha256 = hashRecordedAfterPlanReview;
source.PartId = stablePartIdFromPlan;
```

The recipe checks the plan hash before copying and before final verification.
The part record binds the verified file to that plan and part ID. Low-level
custom builds can supply the same fields on `PartExpectations` and use
`session.CompletePart(model, expectations)`. A record without the plan/part
binding is useful for standalone tests but cannot complete a planned job.

## Step 7: batch work and reuse verified setup

Use the [efficiency guide](efficiency-guide.md) for compact checkpoint reports,
read-only plan deltas and retained-evidence limits. A delta report never replaces
the plan binding, native part evidence or final assembly/engineering checks below.

`PartBatch.Run` processes typed `PartTask` builders in one application call.
Each task has an ID, dependency IDs and a `Func<RecipeResult>` builder. Use the
plan's stable part IDs and produce one file per reviewed unique geometry.
Each successful part's record is on disk before the next part starts. Batch
checkpoints retain progress and error details. A failed task blocks dependent
tasks; independent tasks continue. Unknown/self dependencies are rejected before
modeling, and cyclic dependencies are marked blocked. The batch is incomplete
unless every task passes with evidence bound to the same plan and task ID.

```csharp
var report = PartBatch.Run(application, workspace, jobFolder,
    reviewedPlanPath, reviewedPlanHash, new PartTask[] {
        new PartTask { Id = "pipe-01", Build = () => PartRecipes.Pipe(session, pipeRecipe) },
        new PartTask { Id = "elbow-02", Build = () => PartRecipes.Elbow(session, elbowRecipe) }
    });
```

The session skips redundant activation of the already active document and caches
the measured standard double-D block. Cache access still hash-checks the source,
and each returned profile is a separate copy. Template hashes, ownership and
geometry checks are retained. Final recipe verification supplies its snapshot
to the result rather than taking another redundant snapshot. No percentage
speedup is claimed; the reduction is in repeated setup and application calls.

Batch checkpoints are immutable files with an increasing sequence number. A
crash can leave an unfinished checkpoint; inspect the last readable checkpoint
and individual part records. Resuming existing model folders remains deliberate,
with no automatic overwrite. Preserve unrelated open documents and restore the
main assembly when finishing the job. Successful components are closed.

## Step 8: standard final verification and handoff

After the assembly is built in the planned natural component order, rebuilt and
saved, call `AssemblyVerifier.Verify`. Supply one `PlannedOccurrence` per
manufactured occurrence, in planned order, with its full path, configuration,
current verified file hash and 16-value transform. Explicit reference-only
customer files must be suppressed and excluded from the BOM. The verifier reads
the actual FeatureManager occurrences and checks order, count, resolved file
references/hashes, configuration, BOM eligibility, suppression, placement and
active assembly feature errors. The saved main assembly remains open. Pass the
reviewed plan hash as the final argument; save the result as
`assembly-verification.json` inside the job folder.

This verifies assembly component order and BOM eligibility, not an independently
sorted BOM table. Record whether a BOM was created; when it was, review its actual
rows against the planned order. Native part records do not establish drawing
agreement or verify all connection rules. Independently check dimensions, material,
schedule, length rounding, weld datums, flange setback, coupling substitutions,
loft guides, source placement, bolt clocking and final visual appearance as
applicable. Keep physical pressure-test requirements as metadata.

Prepare the completion packet:

```powershell
.\sps-workflow\Invoke-Completion.ps1 init .\JOB\component-plan-v1.json .\JOB\completion-evidence.json
```

For each applicable review, create a JSON record in `JOB/reviews` with:

```json
{
  "status": "pending",
  "plan_sha256": "reviewed plan hash",
  "assembly_sha256": "current saved assembly hash",
  "part_hashes": {"part-id": "current verified component hash"},
  "reviewer": "actual reviewer or identified job verification routine",
  "findings": "actual checks, measured comparisons, decisions and unresolved issues"
}
```

Change status to `passed` only after performing the review. `not_applicable`
requires a reason, reviewer and findings as well. Use all unique component IDs
and current hashes in `part_hashes`, including directly reused library parts.
Required reviews are `drawing_dimensions`, `connection_rules` and `visual_review`.
Set `bom_created` to true or false in the packet; true also requires `bom_order`.
Set `batch_used` to true or false; when true, point `batch.path` to the final batch
checkpoint. It must show every planned job part passed. A partial/failed batch
cannot be hidden by otherwise available part records.
Reports are evidence, not instructions or new authorization. A hash establishes
integrity, not the engineering accuracy of a report's claims.

Refresh references after completing the records, then generate the handoff:

```powershell
.\sps-workflow\Invoke-Completion.ps1 refresh .\JOB\component-plan-v1.json .\JOB\completion-evidence.json
.\sps-workflow\Invoke-Completion.ps1 check .\JOB\component-plan-v1.json .\JOB\completion-evidence.json --out .\JOB\completion
```

Refresh updates evidence-file hashes; it does not pass a pending review or
authorize a changed plan. A changed plan needs reconciliation and a new packet.
Keep the plan beside the main assembly and all new component files, as specified
by the project rules. The JSON/Markdown handoff reports the assembly, component
inventory, hashes, known decisions, outstanding issues and metadata flags.

The checker rechecks the plan and source/library evidence, each job part record,
the saved assembly record, occurrence order/transforms and the required reviews.
Missing, changed, mismatched or incomplete evidence produces `incomplete` and
exit 2. Passed checks with unknown metadata produce `complete_with_flags`.
`complete` requires no outstanding errors or flags. Exit 0 covers both completed
statuses. Completion records describe the state at verification; they are not a
live monitor of later user edits or window changes. No required physical test is
claimed performed by CAD.

## Validation

The workflow has offline completion/planning/library tests and native tests:

```powershell
.\sps-workflow\Invoke-Native.ps1 -Offline -ActionScript .\sps-workflow\tests\Smoke-Metadata.ps1
.\sps-workflow\Invoke-Native.ps1 -AdditionalSource .\sps-workflow\tests\RemainingSelfTest.cs -ActionScript .\sps-workflow\tests\Smoke-Remaining.ps1
```

The native test creates two verified pipe copies and a small nonproduction
assembly, exercises an intentional batch failure and dependent-task block, checks
description links and cache isolation, rejects a stale property override and bad
placement, and leaves original files unchanged. Its intentionally incomplete
batch is test evidence, not an incomplete production job. The synthetic example
completion packet deliberately reports missing native/review evidence.
