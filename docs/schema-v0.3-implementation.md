# Schema v0.3 implementation

The C# exporter under `src/GenericCadLink.Macro` is the authoritative schema-v0.3 exporter.
`macro/BendExportMacro.swb` is a thin launcher that synchronously runs the sibling
`macro/BendExportMacro.exe`. The host connects to the active SolidWorks session and invokes
the same exporter implementation without requiring VSTA or COM registration.

## Output contract

For each saved sheet-metal part, the exporter creates:

```text
{exportRoot}/{partNumber}/
  bend.json
  flat.dxf
```

`bend.json` contains the required deterministic geometry:

- right-handed, millimetre, XY `coordinateSystem`
- `fixedFace.normal` (the exported frame is always `[0, 0, 1]`)
- canonical 3D `bends[].axis` in the same coordinate system as the DXF
- geometry-derived `bends[].signedAngleDeg`
- `bends[].movingSidePoint`, derived as the side opposite the fixed-face interior point
- `bends[].dxfLine`, including the matched DXF handle and endpoints

The exporter does not derive `up`/`down` directly from the SolidWorks bend-direction enum.
It computes the sign from the canonical axis, flat normal, and folded moving-face normal.
`direction` and the DXF layer are then derived from that signed angle.

## Strict failure policy

The package contains an error and is not a successful M-BEND input if any required value
cannot be resolved. Validation includes:

- coordinate frame and fixed-face normal
- non-zero bend axis and signed angle
- signed angle, direction, and DXF layer agreement
- moving-side point not on the bend axis
- one JSON bend to exactly one DXF line, with matching endpoints
- at least one fully resolved bend

No missing v0.3 geometry is filled from a default `up`, guessed angle, or guessed axis.

## DXF layers

The post-processor registers and emits:

- `CUT`: outline, holes, and other exported geometry
- `BEND_UP`: bends with `signedAngleDeg > 0`
- `BEND_DOWN`: bends with `signedAngleDeg < 0`

Every matched bend also records its DXF line endpoints and handle in `bend.json`.

## Build and SolidWorks verification

Run from the repository root:

```powershell
.\scripts\build-macro.ps1
```

This creates `macro/BendExportMacro.exe` and copies the two SolidWorks Interop DLLs beside it.
Keep the EXE, Interop DLLs, and `macro/BendExportMacro.swb` in the same folder.
In SolidWorks 2022, open each representative saved sheet-metal part and run the SWB from
**Tools > Macro > Run**. The SWB invokes the schema-v0.3 host and waits for completion.
The export is accepted
only when the result dialog has no `[ERROR]` entries and the generated JSON/DXF satisfy the
checks above. Validate all three representative models before merging to `main`.
