# SPS SolidWorks workflow toolkit — version 1

Start with the [efficiency guide](efficiency-guide.md) and the preserved
[standing modeling rules](modeling-rules.md). Use a fresh chat and small
`JOB-STATE.md` per job; [historical notes](job-history-index.md) are loaded only
for the relevant job. `Invoke-Efficiency.ps1` provides read-only delta/compact
reports and optional measured effort events. It does not modify CAD or pass reviews.

The full process now includes the [steps 4–8 completion guide](completion-guide.md):
active-feature checks, consistent properties, hash-bound per-part verification,
typed batches with dependency handling, native assembly audits and a standard
JSON/Markdown completion handoff. Use this with the component-plan and recipe
guides for future jobs.

This toolkit supports future jobs with a verified library shortlist, reusable
native modeling routines, and checks that follow the features actually used
by the model. Existing production models and original library files are not
retrofitted. `AGENTS.md` remains the authority for the modeling rules.

## Library shortlist

Use the [local Victaulic reference and rolled-pipe template guide](references/victaulic-guide.md)
for groove work. The user-supplied PDF is preserved locally so routine online
lookup is unnecessary. Schedule 10 single- and double-end rolled templates may
be copied for jobs after configuration-specific verification; native groove
construction remains an option. These candidates are not automatically measured
entries in the verified shortlist.

For step 2, use the [component planning guide](component-plan-guide.md) to map
manufactured pieces, drawing items, library configurations, stock lengths,
end features and placements **before modeling**. `Invoke-Plan.ps1` creates and
checks these plans and produces JSON/CSV/Markdown reports. The filled example
is explicitly synthetic; it makes no changes to existing customer jobs.

`index/library-index.json` and its CSV companion contain measured configurations
from completed native audits. Consult the index for its current inventory. Each entry records its exact
file hash, configuration, material, relevant dimensions, aliases, stock metadata,
and the measurement/audit evidence. All values use meters/radians except fields
explicitly marked otherwise. CLI dimensional arguments use inches.

Only the measured Default configurations are verified in this initial index.
Other discovered configurations are listed as **unmeasured**, not as interchangeable
alternatives. In particular, F4SQ534X3 Default is 0.500 inch thick; the separate
Finished flange configuration must not be selected from that measurement.

From the project directory:

```powershell
.\sps-workflow\Invoke-Index.ps1 check-index
.\sps-workflow\Invoke-Index.ps1 search --kind pipe_blank --od-in 6.625 --brief
.\sps-workflow\Invoke-Index.ps1 search --kind flange --square-in 5.75 --bore-in 3 --thickness-in .5 --brief
.\sps-workflow\Invoke-Index.ps1 search "3/4 NPT" --kind half_coupling --brief
```

Searches hash-check cached files and exclude changed, unreadable or uncached
entries. A zero-result shortlist does not establish library absence. Continue
to use the PDM flange dimensional search card, starting broad, and verify the
configuration selected. This index is not a replacement for the vault catalog
or the material numbering workbook.

`build-index` reconstructs this seed from the archived native measurement log
only if its library hashes still match the completed audit. It intentionally
refuses to refresh changed files using old measurements. To expand the index,
collect a new native configuration-specific measurement and audit, bind it to
the current file hash, and then add an importer for that evidence.

## Native helpers

Step 3 adds [reusable part recipes](recipe-guide.md): complete pipe, trimmed
elbow and existing-dimension fitting workflows. These compose the helpers,
apply metadata, verify and save, and close completed component documents.
The launcher loads `native/SpsRecipes.cs` along with the low-level module.

`native/SpsSolidWorks.cs` exposes `SpsWorkflow.SpsSession`. The launcher loads
the installed SolidWorks interop through Windows PowerShell/.NET Framework.
It attaches to an exact application ROT moniker and rejects ambiguous sessions;
pass `-SolidWorksPid` when more than one is available. It does not select an
arbitrary document moniker or log unrelated open applications.

```powershell
.\sps-workflow\Invoke-Native.ps1 -CompileOnly
```

For a job, put its typed C# entry point in a separate source file and invoke
it from a short PowerShell action script. See `tests/NativeSelfTest.cs` and
`tests/Smoke-Native.ps1` for a working example. Add the job source using
`-AdditionalSource`; `-ActionScript` calls that entry point with
`$SpsApplication`, `$SpsWorkspace`, and `$SpsWorkflowRoot`.

Supported operations:

| Helper | Behavior/checks |
|---|---|
| `Copy` | Requires the verified template hash, creates a new file, checks the copied bytes, verifies the requested configuration, records provenance. |
| `SetPipeLength` | Reads the extrusion's actual profile, requires two full circles, preserves stock diameters/material/ID, checks total modeled length including reverse extrusion. Blank local axis is X. |
| `SetElbowAngle` | Changes the existing native bend angle while preserving measured bend radius, OD, wall and actual inner/outer torus geometry. |
| `EditExistingDimensions` | Validates all expected old dimension values and protected dimensions before editing; checks targets, preserved values, solid and material afterward. |
| `ApplyProperties` | Creates the 14 required Text fields in order; defaults MakeOrBuy=M while retaining explicit known values; sets actual new material and pipe-quantity links; updates existing configuration overrides; reports unknowns. Source filename comes from the actual supplied path. |
| `AddRollGroove` | Requires a job-reviewed OGS roll specification, matching OD, sufficient wall/length, and available feature names. Builds Cut-Revolve1 plus inner-wall displacement, then checks both diameters. |
| `AddMidWallCope` | Builds a native guided, closed lofted cut with four sections; checks closed guide, actual branch mid-wall/wall, stock length and valid solid. |
| `PerpendicularCylinderCopeGuide` | Generates a guide for the supported perpendicular cylinder case using actual parent OD. Curved, angled and multi-piece parent intersections remain explicit job geometry calculations. |
| `AddBranchOpening` | Uses actual branch OD minus wall. Checks its stop disk against a bounded parent bore before creating the cut, then verifies the cut's active circle profiles and endpoints. |
| `ReadStandard34Block` / `AddDoubleD` | Reads the actual approved standard 3/4 block at scale 1, checks eight edges, large/corner radii and flats; makes a native cut in the explicitly supplied orientation with a verified interior stop. Other blocks are not silently substituted. |
| `ActiveCircleProfiles` | Reads parents/absorbed subfeatures of the specified active cut, not sketches found by historical names. Handles SolidWorks' ICE feature type through its underlying native type. |
| `SaveChecked` / `CloseSaved` | Checks solid, feature errors, property types and evaluated links before saving. Refuses to close an unsaved document. |

Every mutating helper requires a document created by its session. The output
folder must be inside the project; files are never overwritten. The default
constructor requires a new folder. `ForPlannedJob` supports the step-2 handoff
from an existing planning folder with a hash-checked reviewed plan and no native
models at its top level; see the recipe guide. Existing/incomplete model jobs
require explicit inspection and a deliberate resume procedure.

Modified fittings receive a blank ERP Material ID unless an explicit new
mapping is supplied with `verifiedStockMappingEvidence` pointing to the actual
reference file. The caller must verify that mapping against the numbering
workbook and library evidence; an existing template identifier is provenance,
not evidence of a new stock mapping. Known fitting quantities are retained;
otherwise they default to one piece. Pipe stock identifiers remain inherited
from their verified blanks.

`CylinderBore` and `TorusBore` require actual inside dimensions and finite
axial/angular bounds. Use `UnionBore` for a connection crossing adjacent parent
pieces. The job must supply the correct parent region, branch centerline,
interior stop, and double-D orientation. These helpers do not infer those
engineering choices from filenames or approximate bounding boxes. End-disk
sampling checks 64 boundary points; it is a guard, not a general proof of
clearance through arbitrary intervening geometry. Verify the actual connection.

The cached OGS rows in `index/groove-specs.json` were checked against
[Victaulic 25.01 Rev T, March 2026](https://assets.victaulic.com/assets/uploads/literature/25.01.pdf).
Review the applicable system, material, rolls and wall for each job. The native
helper currently supports roll grooves; it rejects an unreviewed specification
and does not invent a Schedule40 cut-groove implementation. The 0.030-inch
profile blend is an existing workflow modeling convention, separately labeled
from the manufacturer's tabulated A/B/C values and manufacturing tolerances.

## Checks and native demonstration

Offline regression tests cover changed/missing cache files, configuration
thickness differences, the flange ID/pipe OD boundary, and inappropriate part
reuse where pipe lengths match but openings differ.

```powershell
python -m unittest discover -s .\sps-workflow\tests -v
```

The launcher for the index finds the bundled Codex Python when available;
the command above can also use its full runtime path if Python is not on PATH.

Run the native demonstration only when SolidWorks is available:

```powershell
.\sps-workflow\Invoke-Native.ps1 `
  -AdditionalSource .\sps-workflow\tests\NativeSelfTest.cs `
  -ActionScript .\sps-workflow\tests\Smoke-Native.ps1
```

It creates clearly labeled **nonproduction copies** in a new `tests/runs`
folder. It exercises pipe sizing, an opening, a roll groove, the approved
double-D block, and a lofted cope. It also copies the prior job's elbow to
reproduce the stale-sketch regression: the active cut depth must be
185.260495 mm, despite old unused sketches describing 260 mm. Protected original
hashes and the existing active document's save state must remain unchanged.
Test documents are saved/closed, and the original active document is restored.
The JSON report and two native review images remain beside the fixtures.

This is the first reusable layer, not a complete one-click assembly builder.
Source reconciliation, PDM searches, curved-parent guide calculations, assembly
placement/BOM audits, and final visual review still use the established job
workflow. Grow coverage with measured configurations and successful job evidence.
