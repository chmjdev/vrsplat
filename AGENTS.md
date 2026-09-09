# vrflatscore — agent briefing

Context for an AI agent picking up work in this repository. Read it before
changing anything here.

Facts below carry the date they were established. Treat any of them as dated
evidence and re-check before relying on one — this file included.

## What this is

A **Unity package for rendering 3D Gaussian splats in VR**. A library that ships
inside consuming Unity builds — not an application, not a service.

- Package `com.binteca.vrflatscore`, currently **0.11.0**. Assemblies
  `VRFlatsCore` / `VRFlatsCoreEditor`, namespaces `VRFlatsCore.Runtime` /
  `.Editor`. Renamed from `vrsplat` on 2026-09-08.
- An **MIT fork**: aras-p/UnityGaussianSplatting → ninjamode/Unity-VR-Gaussian-Splatting
  (adds VR rendering and multi-layer clouds; originates in an IEEE TVCG paper) →
  this fork. Original copyright and licence preserved unchanged; **this fork
  adds no licence terms**.
- It exists to render **real captured rooms** on **Quest 3 standalone**. Both
  upstreams describe themselves as experimental and the canonical one states it
  is untested on mobile, so this fork is maintained rather than depended on.
- **This repository is public.** It carries no credentials, no capture data and
  no customer material, and it must stay that way.

## Layout

| Path | What |
| --- | --- |
| `package/` | The Unity package — `Runtime/`, `Editor/`, `Shaders/`, `Materials/` |
| `VR-URP/` | Upstream's URP demo project — upgraded 2026-09-09 to Unity 6000.3.22f1 + URP 17.3.0 — with baked splat assets in three quality tiers |
| `tools/capture/` | Remote GPU reconstruction pipeline. Consumed on its own, by a different caller than the Unity package — the two halves of this repository serve different consumers |
| `tools/verify/` | Compile and behaviour harness |
| `docs/` | `render-pipeline-integration.md`, `splat-editing.md`, `upstream.md`, `verification.md` |
| `design/` | Dated architecture / data-design / security scans |
| `ROADMAP.md`, `CHANGELOG.md`, `SECURITY.md` | Work items, release record, posture |

## How the data works

PLY point cloud → `GaussianSplatAsset`, by two routes: the **Editor importer**
(bakes compressed blobs) and **`SetRuntimeData`** (a fork addition — build an
asset from packed bytes in a *player*, so a capture made after the build is
still renderable). Rendering is a compute sort plus a `DrawProcedural` into an
offscreen target, composited over the camera colour. The composite reads
`_GaussianSplatRT` as a **global** texture, not as the blit source.

Capacity is the binding constraint: **~72 fps to roughly 400k Gaussians on
Quest 3**. Room-sized captures are realistic; a whole building is not.

## Rules that bind work here

1. **Stay mergeable.** Prefer additive changes and keep upstream's file layout.
   The class vocabulary (`GaussianSplatRenderer`, …), `GaussianSplatting.hlsl`,
   the `_GaussianSplatRT` property and the `nesnausk.*` EditorPrefs keys
   deliberately keep upstream's names.
2. **Nothing is inferred.** Every statement is either verified or is labelled
   unverified; there is no third category. Configuration states intent, never
   reality. Counts are counted. Before reporting an absence, say how the check
   would have looked if the thing had been there.
3. **MIT preserved.** Original copyright and attribution stay untouched.
4. **Measured, not assumed.** Performance claims carry the device and the
   method, or they are labelled as upstream's numbers.
5. **Compatibility claims are re-runnable**, never merely asserted.

## Verifying

Run `./tools/verify/run.sh`. It builds a throwaway Unity project, adds this
package by `file:` path alongside URP, compiles, and checks **by reflection over
the loaded assemblies**: that the declared identities resolve, that
`GaussianSplatURPFeature` implements `RecordRenderGraph`, that the
`GS_URP_RENDERGRAPH` version define actually fired, and that runtime splat
buffers are refused when malformed (six assertions). `docs/verification.md` says
what a pass does and does not mean.

**Read the log, not the exit code** — a batchmode run whose harness called
`EditorApplication.Exit(1)` still returned 0 to the shell.

Last result (2026-09-10): zero compile errors on Unity 6000.3.22f1 with URP
17.3.0, with 0.11.0's LOD fields and `SogFileReader` compiled in and
`DequantizeQuat` returning unit quaternions across all four `maxComp` branches.
That run also **found a non-terminating loop in the harness itself** — see
Traps.

## Upstream

- `upstream` (ninjamode) is a **true git ancestor** and we are **0 behind** it —
  nothing upstream is unmerged. Ahead by this fork's own history; that number
  moves with every commit here, so count it rather than quoting one.
- `upstream-aras` (aras-p, where fixes actually land now) shares **no history at
  all**: `git merge-base` exits 1. Porting from it is manual by construction,
  never a merge or a cherry-pick.

Remotes do not travel with a clone. `docs/upstream.md` carries the two
`git remote add` lines, the rename map, and the list of fork additions that a
careless port would erase.

## What this fork carries that upstream does not

Runtime-created splat data (`SetRuntimeData` / `ValidateRuntimeLayer`) · the
`#if UNITY_EDITOR` guards that had been failing every player build of a
consuming project · `QuestBudget` and its import-time guard · the XR per-eye
matrix handoff, without which stereo parallax is zero · `RecordRenderGraph`
**and** a retained legacy `Execute` path behind a version define, so URP 14 and
Compatibility Mode projects still render.

## Open, and to be stated as open

- **The Quest budget is upstream's number.** `QuestBudget.MeasuredOnOurDevice`
  is `false`. Do not quote ~400k as a measurement made here.
- **Nothing has rendered a frame on a headset.** That the RenderGraph pass is
  *recorded* is verified; that it *draws correctly on a Quest 3* is not. A Quest
  APK has been built (ARM64 / IL2CPP / Vulkan / Linear, OpenXR with
  MetaQuestFeature, MultiPass, 245,063 splats in upstream's own demo
  configuration, RenderGraph compatibility mode off) but installing it was
  blocked device-side and the measurement has not been taken.
- **Single Pass Instanced stereo is unmeasured** — see `ROADMAP.md` 1e. Current
  rendering is Multi-pass.
- **SOG import is scaffolding, not an importer** (ROADMAP 2). The schema and the
  dequantization formulas are in and now compile, but **WebP pixel decoding is
  not implemented** — Unity has no native WebP path. `ISogTextureDecoder` is the
  seam; `DefaultSogTextureDecoder` throws rather than returning wrong pixels.
  Nothing is wired into the asset-creator UI.
- **The LOD ladder is compiled but never run** (ROADMAP 3). It only behaves as a
  ladder if the source capture's layers were authored as nested detail levels —
  author responsibility, as the layer format always was.
- **`package.json` declares Unity `2022.3`** and that minimum has never been
  built in this fork, including the legacy path retained for it.
- **The `file:` dependency cannot be version-pinned** — a consumer takes whatever
  is in the checkout beside it. `CHANGELOG.md` and a meaningful `version` are
  the mitigation available from this side.

## Traps — all silent, each cost a run to find

- **`m_LayerActivationState` is populated only by the custom inspector.** A scene
  built by script leaves it empty, which means zero active layers, zero splats,
  and a renderer that reports a valid asset while drawing nothing.
- **The renderer's shader fields have no `Shader.Find` fallback and no
  `Reset()`.** Unassigned, `Initialize` returns early and nothing draws.
  (Both of the above are unfixed; a `Reset`/`OnValidate` would close them.)
- **Unity's asmdef version-define syntax has no unbounded-range form.**
  `[17.0.0,)` is rejected with `ExpressionNotValidException` and the guarded code
  silently compiles out of an otherwise clean build. A bare `17.0.0` means "that
  version or newer".
- **Overriding an `[Obsolete]` member raises CS0672**, not CS0618.
- **The runtime colour buffer is raw `float4` per splat**, not the packed
  `colorFormat`. `CalcColorDataSize` describes the *converted* texture and is the
  wrong thing to size a runtime buffer with; that mistake is now refused by name.
- **A bash `EXIT` trap whose last command fails overwrites the script's exit
  status** — which can make a verifier report failure on a passing run.
- **A `for (byte x = 252; x <= 255; x++)` loop never terminates.** A byte cannot
  exceed 255, so the condition is always true and the counter wraps to 0. This
  was live in `tools/verify/PackageVerify.cs` and measured on 2026-09-10: the
  run spun for 22 minutes, logged 12.5 million failing iterations, grew
  `verify.log` to 11.4 GB at ~5 MB/s and never printed a result. Fixed by
  counting in an `int` and casting at the call.

## Deployment

`jcds.config` declares the package `disabled`, with no ports and no domains.
**There is no deploy step.** Consumers resolve it by relative `file:` path from a
checkout beside them, so publishing to the git remote is the whole of shipping
it. Never report a deploy for this project.
