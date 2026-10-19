# Component plan: PLANNING-DEMO

Synthetic workflow demonstration only; no model is built.

Status: ready

Planning checks only. Native geometry, properties, rebuild and placement verification remain required. Reuse candidates require review; no files are merged.

| Component | Qty | Drawing items | Action | Status |
|---|---:|---|---|---|
| PLANNING-DEMO-01.SLDPRT | 2 | 1, 2 | copy | ready |
| PLANNING-DEMO-02.SLDPRT | 1 | 3 | copy | ready |

## Component details

### PLANNING-DEMO-01.SLDPRT

Stock length (m): 0.254

Library: C:\SPS\Libraries\SolidWorks Fittings\PIPE MODELS\PIPE BLANKS\PIPE 3 S10.SLDPRT; configuration: Default

End features: [{"end": "start", "type": "plain"}, {"end": "finish", "type": "plain"}]

Openings: []

- occ1: piece1; Synthetic example datum, illustrative only. Translation (m): [0, 0.2, 0].
- occ2: piece2; Synthetic example datum, illustrative only. Translation (m): [0, 0.4, 0].

### PLANNING-DEMO-02.SLDPRT

Stock length (m): 0.254

Library: C:\SPS\Libraries\SolidWorks Fittings\PIPE MODELS\PIPE BLANKS\PIPE 3 S10.SLDPRT; configuration: Default

End features: [{"end": "start", "type": "plain"}, {"end": "finish", "type": "plain"}]

Openings: [{"depth_basis": "through near wall; verify interior stop", "diameter_m": 0.02, "local_axis": [0, 1, 0], "local_center_m": [0.127, 0, 0], "type": "round"}]

- occ3: piece3; Synthetic example datum, illustrative only. Translation (m): [0, 0.6000000000000001, 0].


## Issues

- warning: plain / unknown_property: Customer: fill known value; flag unknown
- warning: plain / unknown_property: Job Number: fill known value; flag unknown
- warning: plain / unknown_property: Item Number: fill known value; flag unknown
- warning: plain / unknown_property: DrawnBy: fill known value; flag unknown
- warning: plain / unknown_property: Drawn By: fill known value; flag unknown
- warning: plain / unknown_property: Drawn Date: fill known value; flag unknown
- warning: plain / unknown_property: Revision: fill known value; flag unknown
- warning: opened / unknown_property: Customer: fill known value; flag unknown
- warning: opened / unknown_property: Job Number: fill known value; flag unknown
- warning: opened / unknown_property: Item Number: fill known value; flag unknown
- warning: opened / unknown_property: DrawnBy: fill known value; flag unknown
- warning: opened / unknown_property: Drawn By: fill known value; flag unknown
- warning: opened / unknown_property: Drawn Date: fill known value; flag unknown
- warning: opened / unknown_property: Revision: fill known value; flag unknown

## Possible shared files

None identified.
