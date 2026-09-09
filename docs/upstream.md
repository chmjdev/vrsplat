# Upstream — remotes, relationship, and how to pull a fix

**Verified 2026-09-09.** Every count and relationship below was measured with
`git` against freshly fetched remotes on that date, not carried from prose.

## The remotes

```bash
git remote add upstream       https://github.com/ninjamode/Unity-VR-Gaussian-Splatting.git
git remote add upstream-aras  https://github.com/aras-p/UnityGaussianSplatting.git
git fetch --no-tags upstream upstream-aras
```

Until 2026-09-09 this repository had **`origin` only**, so nothing upstream —
including a security fix — would ever have surfaced here. Adding the remotes was
the cheapest possible mitigation and it is done.

Remotes are local git configuration and are **not** carried by a clone. A fresh
checkout has to run the two `git remote add` lines above; that is what this file
is for.

## The two upstreams are not the same kind of relationship

| Remote | Repository | Relationship to this fork |
| --- | --- | --- |
| `upstream` | `ninjamode/Unity-VR-Gaussian-Splatting` | **A true git ancestor.** `upstream/main` is an ancestor of our `HEAD`, and we are **0 behind** — nothing upstream is unmerged. A merge from it would be a real merge. (Ahead by this fork's own history; counted 19 on 2026-09-10, and it moves with every commit here, so re-count rather than quoting this) |
| `upstream-aras` | `aras-p/UnityGaussianSplatting` | **No shared history at all.** `git merge-base HEAD upstream-aras/main` exits 1 — there is no common ancestor. ninjamode copied the code rather than forking the repository |

**That second row is the important one.** "Pull the fix from aras-p" cannot be a
merge or a cherry-pick: git has no path between the two histories. It is a
manual port — read their file, write ours.

ninjamode's own README says as much from the other direction: *"This is an
unmaintained demo based on a research publication. VR support has been
upstreamed to aras-p/UnityGaussianSplatting."* So the repository we descend from
is finished, and the repository where fixes actually land is the one we cannot
merge from. Both facts have to be held at once.

## The rename map

Since 2026-09-08 the package identity is this fork's own. Any port from either
upstream has to be translated:

| Upstream | Here |
| --- | --- |
| package id `org.nesnausk.gaussian-splatting` | `com.binteca.vrflatscore` |
| assembly `GaussianSplatting` | `VRFlatsCore` |
| assembly `GaussianSplattingEditor` | `VRFlatsCoreEditor` |
| namespace `GaussianSplatting.Runtime` | `VRFlatsCore.Runtime` |
| namespace `GaussianSplatting.Editor` | `VRFlatsCore.Editor` |
| env vars `VRSPLAT_*` | `VRFLATSCORE_*` |

**Deliberately NOT renamed**, so upstream diffs stay readable: the class
vocabulary (`GaussianSplatRenderer`, `GaussianSplatAsset`, …), the shader files
and their names (`GaussianSplatting.hlsl`, `Hidden/Gaussian Splatting/Composite`),
the `_GaussianSplatRT` shader property, and the `nesnausk.*` EditorPrefs keys.
The file layout is upstream's throughout — that is the point of "stay
mergeable" in `ROADMAP.md`.

## Porting a fix

1. `git fetch --no-tags upstream-aras`
2. Read the upstream file: `git show upstream-aras/main:package/Runtime/<File>.cs`
3. Diff it against ours by eye — `git diff` will not help across unrelated
   histories.
4. Apply the change **through the rename map**, keeping our fork's own additions
   (below) intact.
5. Compile-verify before claiming it works — see `docs/verification.md`.
6. Record it in `CHANGELOG.md`, naming the upstream commit.

## What this fork carries that upstream does not

Anything ported has to survive these, because a careless overwrite silently
removes them:

- **Runtime-created splat data** — `GaussianSplatAsset.SetRuntimeData` /
  `ValidateRuntimeLayer` / `DisposeRuntimeData`, and the `HasRuntimeData`
  branches in `GaussianSplatRenderer.UpdateRessources`. Upstream has no runtime
  loading path at all.
- **`#if UNITY_EDITOR` guards** on the Runtime assembly's `using UnityEditor;`
  lines. Without them every player build of a consuming project fails.
- **`QuestBudget`** and the budget warnings in the creator window and the
  renderer inspector.
- **The XR per-eye matrix handoff** — `ResolveXRMatrices` in
  `GaussianSplatURPFeature`, and the `xrViewOverride` / `xrRawProjOverride`
  parameters threaded through `SortAndRenderSplats` into `CalcViewData`.
  Upstream's `RecordRenderGraph` calls `SortAndRenderSplats(camera, cmd)` with
  no overrides; ours must keep them or stereo parallax goes to zero.
- **Both URP recording paths.** Upstream `#error`s on anything below Unity 6 and
  ships RenderGraph only. We keep the legacy `Execute`/`OnCameraSetup` overrides
  as well, behind `GS_URP_RENDERGRAPH`, so a Compatibility Mode project and a
  URP 14 project still render.

## Already ported

- **`RecordRenderGraph` (ROADMAP 1c), 2026-09-09.** Ported from
  `upstream-aras/main:package/Runtime/GaussianSplatURPFeature.cs`. Structure,
  the unsafe-pass shape, `CreateRenderGraphTexture`, `AllowPassCulling(false)`,
  the `SetGlobalTexture` of `_GaussianSplatRT`, the `CoreUtils.SetRenderTarget`
  with the camera depth, and the read-only depth declaration all follow
  upstream. What differs is ours: the XR matrix overrides, the retained legacy
  path, and the `GS_URP_RENDERGRAPH` version define that selects between them.
