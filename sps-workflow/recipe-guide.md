# Step 3: reusable native part recipes

For shared source-axis, bolt-circle and supported two-arc reducer calculations,
see `geometry.py` and the [coverage table](efficiency-guide.md). These pure
calculations supply reviewed recipe inputs; they are not general native topology
builders and do not replace source geometry or final native verification.

`native/SpsRecipes.cs` combines the measured low-level helpers into repeatable
part workflows. Use a checked component plan first. Recipes do not infer geometry
or authorization from customer files. They create copies in a new job/session
folder and never edit a library original. Successful recipes save and close their
components, as requested. The caller restores the main assembly after a batch.

For the step-2 handoff, use `SpsSession.ForPlannedJob(application, workspace,
jobFolder, reviewedPlanPath, reviewedPlanSha256)`. The JSON plan must be directly
inside the job folder and match the hash recorded at review. The folder may
contain planning reports and preserved customer references, but must contain no
native part/assembly/drawing files at its top level. Existing production models
require deliberate inspection/resume. Every target filename still uses
create-new semantics, so a collision cannot overwrite a file. The factory checks
plan identity and folder safety; the caller must run the planner and review its
ready parts, conflicts and evidence before calling it. It does not interpret an
arbitrary JSON file as proof of engineering approval.

| Recipe | Operations, in order |
|---|---|
| `PartRecipes.Pipe` | Copy exact hash/configuration; set total stock length; native mid-wall lofted cope if requested; reviewed OGS roll groove if requested; round mid-wall openings; approved double-D openings; properties; final rebuild/solid/feature checks; save; close. |
| `PartRecipes.Elbow` | Copy suitable native library elbow; reduce its existing revolve angle; check actual inner/outer torus geometry and native stock dimensions; optional openings; properties; verify/save/close. |
| `PartRecipes.Fitting` | Copy suitable library flange/disc/fitting; validate all reviewed existing dimension values and protected dimensions before editing; edit existing sketches/features; rebuild; check requested and protected values, solid and material; properties; verify/save/close. |

Direct unmodified library reuse remains an assembly operation; do not pass those
parts through a copy recipe. The fitting routine preserves the existing native
tree rather than reconstructing an equivalent shape. Flanges still need the PDM
dimensional search and correct configuration, and modified copies need accurate
job numbering and verified stock mappings or a visible missing-mapping flag.

## Use from a job C# entry point

`Invoke-Native.ps1` automatically loads both helper modules. Put typed job code
in a separate C# file and call it from a short PowerShell action script, following
`tests/RecipeSelfTest.cs` and `tests/Smoke-Recipes.ps1`. Compile without touching
SolidWorks first:

```powershell
.\sps-workflow\Invoke-Native.ps1 -AdditionalSource .\your-job-code.cs -CompileOnly
```

This example shows a plain pipe using values resolved from its verified plan;
all distances are meters and angles are radians:

```csharp
var source = new SpsWorkflow.RecipeSource {
    Template = measuredTemplatePath,
    TemplateSha256 = measuredTemplateHash,
    Configuration = measuredConfiguration,
    Filename = assemblyName + "-01.SLDPRT",
    CustomerSourceFile = preservedCustomerSourcePath,
    Properties = knownJobProperties
};
var result = SpsWorkflow.PartRecipes.Pipe(session, new SpsWorkflow.PipeRecipe {
    Source = source, StockLength = plannedStockLength,
    OD = actualLibraryOD, ID = actualLibraryID
});
// result.File, Snapshot, UnknownProperties, Saved and Closed record completion.
```

Use `PipeRecipe.Cope` with the actual branch mid-wall guide, `Groove` with a
job-reviewed `GrooveSpec`, and the `Openings` / `DoubleDOpenings` collections as
needed. Round openings use the attached branch's actual OD and ID. Interior stop
and bounded parent bore are explicit. Double-D orientation and the hash-checked
approved block are explicit. The recipe sets stock length before end features;
the native pipe quantity link retains stock length rather than the coped extent.

For every future coped pipe, the recipe measures the final solid's maximum
axial extent and uses it in **Part Description**, with at most three decimals.
It also writes **Max Modeled Length** with explicit units. The blank's stock
length remains separately linked in **Stock Length** and **MaterialQty**. This
measurement uses actual solid extremes along the verified blank's local X axis,
after all cope/groove/opening features; it is not the nominal stock length or
the maximum guide station. The original description pattern is retained in
`Pipe Description Template`, so final verification can regenerate the finished
length text after geometry changes. Existing job/library files are not retrofitted.

Custom low-level cope builders should pass `copedPipe: true` to `ApplyProperties`
and set `PartExpectations.CopedPipe = true`. `RefreshCopedPipeDescription` updates
the measured text before verification/save; its marker also enables subsequent
refreshes through the shared verifier. The finished-length value is a measured
property refreshed by this workflow, not a claim that an associative RD1
annotation was created. Direct manual saves outside the workflow require
reverification before completion evidence is current.

For `ElbowRecipe`, supply measured `OriginalAngle`, requested `TargetAngle`,
actual `BendRadius`, `OD` and `ID`. The supported pattern is a native
`D1@Revolve1` with the measured stock dimensions `D1/D2/D3@Sketch1` used by the
verified SPS elbow family. The routine checks the actual solid's outer and inner
torus radii before and after. It supports reducing an existing bend up to 180
degrees; adding sweep beyond the original angle is rejected. Complex horns,
split pieces, special trims and weld collars remain explicit job operations.

For `FittingRecipe`, each `DimensionEdit` names the existing dimension with its
reviewed `Expected` old value and `Target` value. `PreservedDimensions` names
dimensions that must stay unchanged. `DimensionEvidence` points to the actual
measurement/search record. All references and old values are checked before
any dimension is changed. The caller must also verify resulting faces, bolt
patterns, tolerances and placements against the job; valid dimensions and a
valid solid alone do not prove the entire requested shape.

## Properties, failures and supported geometry

For Schedule 10 grooved pipes, also consider the user's existing templates in
`C:\SPS\Libraries\SolidWorks Fittings\PIPE MODELS\VIC - ROLLED`; see the
[local reference guide](references/victaulic-guide.md). Copy and inspect the
selected native template, preserve its existing groove features, and verify
both end stations after length changes. Do not pass an already grooved template
through `PipeRecipe` without checking its supported source structure or add a
duplicate groove. The current blank-based recipe is not automatically compatible
with these templates. Use a deliberate job-specific copy/edit workflow when
needed, or build native grooves from a verified blank. Double-end templates
do not imply that the existing single-end recipe supports two grooves.

All successful recipes apply the 14 required Text properties, fill supplied
known values, default MakeOrBuy to M, set current file material/quantity links,
and report unknown values. Pipe material and stock IDs remain inherited from
the verified blank. Modified fittings clear the template stock identity unless
a verified replacement is supplied with its evidence. Template provenance stays
separate. A known nonempty fitting ERP mapping without evidence is rejected
before copying. The evidence file's existence is a guard; its applicability must
be reviewed against the numbering workbook and the library.

Successful recipes close their components. A failure raises an exception and
may leave the new owned copy open for diagnosis; it does not silently save or
close a failed part. Do not retry into the same filename or overwrite it. Inspect
the failure and resume deliberately. Existing customer/library documents are
outside session ownership and cannot be edited by these helpers.

Current pipe recipes assume the verified blank's axis is X, its plain start
is X=0, the optional groove is at that end, and the optional cope is at the far
end. The cope cannot overlap the grooved end's straight section. The guide must
come from actual material geometry; perpendicular cylindrical guides are provided,
while curved, angled and multiple-parent guides remain job calculations. OGS roll
grooves use `Cut-Revolve1` plus native inner-wall displacement. Cut grooves,
two-ended groove recipes, other groove systems, and unspecified coupling blocks
remain explicit extensions requiring applicable dimensional evidence and tests.
No automatic fallback substitutes a different operation.

The plan JSON is a review/identity record, not executable code. Map its reviewed
values into typed recipes in the job code; the toolkit does not blindly execute
arbitrary feature descriptions from the plan or customer documents. Native
assembly transforms, flange setback decisions, component order/BOM and final
visual review remain part of job verification.

## Native verification

```powershell
.\sps-workflow\Invoke-Native.ps1 `
  -AdditionalSource .\sps-workflow\tests\RecipeSelfTest.cs `
  -ActionScript .\sps-workflow\tests\Smoke-Recipes.ps1
```

The test uses clearly labeled nonproduction copies: a pipe combining a cope,
groove, round opening and approved double-D; a 40-degree native elbow; and a
library flange with its existing bore dimension changed. It reloads saved
files, checks real geometry as well as native dimensions, exercises rejection
guards, checks protected originals and closes completed fixtures. Reports and
native review images are saved under `tests/recipe-runs`. The flange bore edit
is synthetic and is not a production selection or PDM-search approval.

`tests/Smoke-RecipeShape.ps1`, loaded with `tests/RecipeShapeAudit.cs`, adds an
independent check of the saved elbow's actual solid volume against a 40-degree
torus sector with the specified bend radius, OD and ID. This checks the resulting
bend geometry beyond reading its angle dimension. The existing `Smoke-Native.ps1`
suite continues to cover the low-level helper and stale-sketch regressions.
