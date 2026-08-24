# bend package schema v0.3

## 1. Purpose

Schema v0.3 gives SheetPartner M-BEND enough geometry to reproduce each bend
without guessing the sheet side, bend-axis orientation, or moving side.

`direction` and `dxfLayer` are derived values. `fixedFace.normal`,
`bends[].axis`, `bends[].movingSidePoint`, and `bends[].signedAngleDeg` are the
authoritative bend-rotation contract.

## 2. Coordinate system

All points and vectors are transformed to the exported flat-pattern coordinate
system before serialization.

```json
{
  "schemaVersion": "0.3",
  "coordinateSystem": {
    "unit": "mm",
    "handedness": "right",
    "flatPlane": "XY",
    "origin": [0.0, 0.0, 0.0],
    "xAxis": [1.0, 0.0, 0.0],
    "yAxis": [0.0, 1.0, 0.0],
    "flatNormal": [0.0, 0.0, 1.0]
  }
}
```

Rules:

- Lengths are millimetres.
- Vectors are normalized within `1e-6`.
- `xAxis × yAxis = flatNormal`.
- A bend axis is oriented from `axis.start` to `axis.end` using the
  deterministic ordering in section 4.

## 3. Fixed face

```json
{
  "fixedFace": {
    "side": "unknown",
    "normal": [0.0, 0.0, 1.0],
    "resolved": false,
    "persistentId": null
  }
}
```

- `normal` is mandatory and is expressed in the exported coordinate system.
- `side` is `outer`, `inner`, or `unknown`.
- `resolved` is true only when `side` was determined from model geometry or an
  explicit model property. It must not mean merely that a normal was obtained.
- `persistentId` is optional and must use a SolidWorks persistent reference,
  not a transient face index.

## 4. Bend geometry

```json
{
  "id": "B1",
  "angleDeg": 90.0,
  "signedAngleDeg": 90.0,
  "direction": "up",
  "innerRadius": 1.0,
  "axis": {
    "start": [25.0, 10.0, 0.0],
    "end": [125.0, 10.0, 0.0],
    "direction": [1.0, 0.0, 0.0]
  },
  "stationaryFaceId": null,
  "movingFaceId": null,
  "movingSidePoint": [75.0, 30.0, 0.0],
  "dxfLine": {
    "start": [25.0, 10.0],
    "end": [125.0, 10.0],
    "layer": "BEND_UP",
    "handle": null
  },
  "dxfLayer": "BEND_UP"
}
```

Axis ordering is deterministic:

1. Transform both endpoints to exported coordinates.
2. Compare X, then Y, then Z with tolerance `1e-6 mm`.
3. The lexicographically smaller endpoint is `start`.
4. `axis.direction = normalize(end - start)`.

Signed-angle convention:

- The coordinate system is right-handed.
- Rotation about `axis.direction` follows the right-hand rule.
- `signedAngleDeg > 0` derives `direction = up` and `dxfLayer = BEND_UP`.
- `signedAngleDeg < 0` derives `direction = down` and
  `dxfLayer = BEND_DOWN`.
- `direction` must never be independently mapped from a SolidWorks enum.

The sign is computed after coordinate transformation from the actual geometry:

```text
sign = sign(dot(axisDirection, cross(flatNormal, bentFaceNormal)))
signedAngleDeg = sign * abs(angleDeg)
```

Degenerate values (`abs(dot(...)) <= tolerance`) are unresolved and cause an
export error; they must not silently default to `up`.

`movingSidePoint` is mandatory, must lie in the flat plane, and its perpendicular
distance from the bend axis must exceed the configured tolerance. It represents
the side that rotates. Persistent face IDs are optional in v0.3.

## 5. Validation before success

The exporter must fail the package (`errors[]` non-empty) when any mandatory
condition fails:

- fixed-face normal missing, zero length, or non-finite;
- bend axis missing or zero length;
- signed angle missing, zero, or non-finite;
- signed angle and derived direction disagree;
- direction and DXF layer disagree;
- moving-side point is missing or lies on the bend axis;
- bend ID and DXF bend line are not one-to-one;
- units, handedness, or coordinate frame are absent;
- DXF and JSON line endpoints differ beyond tolerance.

No unresolved value may be replaced with an assumed `up` direction in schema
v0.3.

## 6. Compatibility

- Schema v0.1 remains readable as legacy metadata, but M-BEND must not use it
  for deterministic 3D bend reconstruction.
- Schema v0.3 is a breaking exporter contract.
- During migration, the exporter may offer an explicit legacy-v0.1 mode; it
  must not label legacy output as v0.3.
