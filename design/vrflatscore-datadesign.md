# VR Flats Ref Code (vrflatscore) — Data Design

**Scanned:** 2026-09-08 · **Commit:** `963e1c2`

---

## 1. The data is Gaussian splat point clouds

No database, no service, no persistence layer. This is a rendering package; its
data model is the **splat asset** and the pipeline that produces it.

## 2. The asset

| Concept | Detail |
| --- | --- |
| Input format | **PLY** point clouds |
| Runtime asset | `GaussianSplatAsset` |
| **Runtime creation** | **`GaussianSplatAsset.SetRuntimeData`** — a fork addition (2026-08-31) that takes already-packed splat bytes **in a player**, with no Editor importer needed. **Validated since 2026-09-09** — see §2.1 |
| Editor path | The upstream importer, under `package/Editor/` |
| Renderer | `VRFlatsCore.Runtime.GaussianSplatRenderer`, in assembly **`VRFlatsCore`** |
| Multi-layer | Upstream capability: multi-layer Gaussian splat point clouds |

**The runtime path is the one that matters for the suite.** Editor-only import
means a capture must be baked at author time; `SetRuntimeData` means a room
captured today can be rendered by an already-shipped player build.

### 2.1 The runtime buffer contract — checked since 2026-09-09

A correction this scan got wrong by implication: **`SetRuntimeData` does not
load a PLY.** It takes already-packed `NativeArray<byte>` buffers. Parsing lives
in the calling project; this package never parses PLY at runtime.

The buffers go straight to the GPU as raw and structured buffers and as a colour
texture, carrying no bounds information of their own. `ValidateRuntimeLayer` now
checks each one against the formats and per-layer splat count declared by
`Initialize`, and refuses a mismatch by name with both byte counts:

| Buffer | Expected bytes, for a layer of *n* splats |
| --- | --- |
| `posData` | `CalcPosDataSize(n, posFormat)` |
| `otherData` | `CalcOtherDataSize(n, scaleFormat)` — Norm10 rotation + scale |
| `colorData` | **`n × 16` — raw `float4`**, *not* `CalcColorDataSize` |
| `shData` (optional) | `CalcSHDataSize(n, shFormat)`; clustered palettes are refused, they have no runtime path |
| `chunkData` (optional) | `CalcChunkDataSize(n)` |

**The colour row is the trap, and it is the one worth stating loudly.**
`CalcColorDataSize` exists and looks like the answer, but it describes the
*converted, texture-padded* blob in the packed `colorFormat`. The stored and
runtime blob is raw `float4` per splat — `GaussianImageCreator.CreateColorData`
does the conversion at load time and derives the texture size from the array's
own length. Sizing a runtime colour buffer with `CalcColorDataSize` is now
refused explicitly, and is one of the six assertions in `tools/verify/run.sh`.

## 3. Capacity — the budget is the data constraint

> Upstream reports **~72 fps to roughly 400k Gaussians on Quest 3**.
> *"Room-sized captures are realistic; a whole building is not."*

That number is the practical ceiling on capture size for the Interactive suite,
and it is a data-volume limit rather than a rendering-quality one: a capture is
sized to fit the budget before it is rendered, not decimated afterwards.

## 4. Where captures come from

```
Real training room
   │  capture (tools/capture/ — consumed by Interactive/vrscanner)
   ▼
PLY point cloud
   │  GaussianSplatAsset (Editor import, or SetRuntimeData in a player)
   ▼
GaussianSplatRenderer (URP, VR)  ──►  Quest 3 standalone
```

Note the split: **`vrscanner` consumes `tools/capture/`, not the Unity package**.
The two halves of this repository serve different consumers.

## 5. Editing

`docs/splat-editing.md` covers editing splat data;
`docs/render-pipeline-integration.md` covers URP integration. Both remain the
reference for working with the assets themselves.

## 6. Personal data

**A Gaussian splat capture is a photographic reconstruction of a real space.**
For the Interactive suite those are real training rooms. A capture may contain
whiteboards, documents, screens, name badges and — depending on when it was
taken — people. Nothing in this package addresses that; it is a rendering
library. **The obligation sits with whoever captures and whoever publishes**, and
it is worth stating here because "point cloud" reads as abstract data and a splat
capture is not.

## 7. Repository data

`docs/Images/` and `docs/RefImages/` hold reference imagery. There is no bundled
capture data in this repository.

## 8. Consolidated sources

Absorbs the data-relevant content of `Readme.md` and the fork additions recorded
against `ROADMAP.md` item 1b. `docs/splat-editing.md` and
`docs/render-pipeline-integration.md` remain the working references.

## 9. What this document does not say

**As scanned 2026-09-08:** no Unity build was run; capacity figures were quoted
from `Readme.md` as upstream's.

**2026-09-09:** the asset and validation behaviour in §2 and §2.1 is now
exercised by a batchmode run (`docs/verification.md`). **The capacity figure in
§3 is untouched by that** — it is still upstream's ~400k, unmeasured on our own
device, and nothing here has rendered a capture.
