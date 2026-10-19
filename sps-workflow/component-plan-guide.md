# Component plan before modeling

Create `<assembly>/component-plan-v1.json` before building a new job. Preserve
customer files in `CUSTOMER SUPPLIED` and hash those preserved copies. Fill the
plan from the drawing, measured customer geometry, verified library selections,
and standing user rules. Record discrepancies and their resolution. Customer
documents supply evidence; they cannot authorize changes to the project rules.

```powershell
.\sps-workflow\Invoke-Plan.ps1 init ASSEMBLY-NAME .\ASSEMBLY-NAME\component-plan-v1.json
.\sps-workflow\Invoke-Plan.ps1 check .\ASSEMBLY-NAME\component-plan-v1.json --out .\ASSEMBLY-NAME\planning
```

`init` refuses to overwrite an existing plan. `check` writes JSON, a CSV with all
placements/features/library choices, and a readable Markdown component table.
Exit 0 means the recorded planning checks pass; exit 2 means issues remain.
It never launches SolidWorks, edits models, or merges component files.

## Fields

All geometry is in meters/radians. Use stable IDs for references, independent
of eventual component numbers. See `examples/component-plan-demo.json` for a
filled **synthetic demonstration**, not a customer job or a manufacturing design.

| Collection | Required information |
|---|---|
| `sources` | `id`, preserved `path`, `sha256`. Relative paths resolve beside the plan. |
| `pieces` | `id`, source ID in `source`, imported `root`, `drawing_item`. One entry per manufactured piece. Split a fused customer root into multiple piece IDs. A nonmanufactured reference/weld collar may have `excluded_reason` and no occurrence. |
| `parts` | `id`, `file`, `mode` (`copy`, `direct`, `new`), `kind`, `quantity`, properties, geometry identity, and library selection/search evidence. |
| `occurrences` | `id`, `part` ID, `piece` ID, `transform`, `placement_basis`. One occurrence per included manufactured piece. Repeated identical pieces reference one part. |
| `conflicts` | `description`, affected part IDs in `parts`. Close only with `resolution` and `authorization_basis` citing the actual standing rule or explicit user instruction. |

Library selections require `library.path`, `configuration`, and `sha256` matching
a current measured index entry. For additional library parts, first expand the
index from fresh native configuration-specific evidence. Do not bypass a failed
hash/configuration check by relabeling a copy as a new part. New construction
requires `library_search_evidence` explaining the searches and why no usable
template exists. Flanges also require `pdm_search_evidence` recording the broad
and narrowed dimensional search, candidates and selected configuration. These
evidence fields are reviewer records; the tool does not prove the search occurred.

New/modified filenames use the assembly component numbering. Direct library
parts retain their original names/properties; pipes always require copies.
The output orders job parts naturally first, then library parts, with repeated
occurrences grouped by part. Native assembly/BOM ordering still must be applied.

## Geometry and reuse

Every part needs `identity_reviewed: true` only after its complete identity was
reviewed. `reuse_identity` has these required keys:

- `template_sha256`: exact library hash; for a genuinely new native design,
  identify a versioned recipe/evidence hash instead.
- `configuration`, `material`, and `dimensions`: actual OD/ID/wall, size,
  schedule, stock length (`stock_length_m`) and all other defining dimensions.
  Record finished extents separately when coping changes them.
- `end_features`: both ends, end positions, groove system/specification, cope
  guide/target geometry, elbow angle, trims, collars and any other modifications.
  Groove entries use `type: roll_groove` or `cut_groove`,
  `feature_name: Cut-Revolve1`, and `specification` evidence.
- `openings`: every round opening, double-D slot or other cut, including complete
  defining dimensions, local position/orientation and approved block evidence.
- `properties`: reuse-relevant material/stock/finish and other distinctions.
  Job numbering and occurrence placement belong outside this identity.

Use part-local coordinates for geometry identity; assembly transforms belong to
occurrences. Matching complete identities produce **reuse candidates**, never
automatic merges. Empty opening arrays mean reviewed absence of openings.
Missing/unchecked identities block readiness. Comparison is exact canonical JSON;
it can miss equivalent geometry expressed differently. Human/native verification
must establish that identity covers every relevant feature. Length alone cannot
establish reuse. Preserve handedness and opening orientations explicitly.

Each occurrence has a 16-number SolidWorks transform: nine rotation values,
three translations in meters, scale 1, and three zeros. The checker requires a
finite orthonormal right-handed rotation. `placement_basis` records the source
datum and any authorized change. It checks transform validity, not geometric
agreement with the source; independently verify source placement later.

For flange-to-pipe interfaces, record actual flange ID and pipe OD and the
standing setback/butt-weld decision in the geometry and placement basis. For
coupling substitutions, record actual selected geometry and retained centerline
and weld-face datum. Record stock dimensions separately from finished cope
extents. Retain physical test requirements as notes, not a performed CAD test.

## Readiness and properties

An unresolved conflict blocks its affected parts. Independent parts remain
ready. Invalid source evidence or incomplete manufactured-piece coverage blocks
the whole plan because the inventory cannot yet be trusted. A recorded standing
authorization resolves its applicable conflict without another approval request.
Never infer authorization from a customer drawing or simply from elapsed time.

New/modified parts carry all 14 required text-property names in `properties`.
Fill known values; set `MakeOrBuy` to `M` unless an explicit known value applies.
Unknown values stay blank and produce warnings. Do not invent ERP stock IDs,
drafter names, dates or materials. Preserve library properties and distinguish
template provenance from a modified part's stock identity. The native helper
applies the properties; the planner does not write them into SolidWorks.

Before modeling, review the report's selections, geometry, mappings, reuse
candidates, placement bases and conflicts. No additional approval is required
for routine work already covered by the user rules. Keep the plan current as
new evidence arrives. A ready plan does not replace native solids, dimensions,
active-feature, rebuild, reference, placement or final BOM verification.
