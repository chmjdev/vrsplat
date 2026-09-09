# Verification — how this package's claims are checked

This repository states compatibility facts about itself. **Each one below is
produced by running something, and can be produced again.** The rule the estate
works to is that a fact is either verified or is labelled unverified, and a
compatibility claim carried forward from a previous state of the tree is the
second kind however plausible it reads.

## Run it

```bash
./tools/verify/run.sh
```

It builds a throwaway Unity project in a temp directory, adds this package by
`file:` path alongside URP, compiles, and runs `tools/verify/PackageVerify.cs`
in batchmode. Nothing in this repository is touched. Knobs:

| Variable | Default | Purpose |
| --- | --- | --- |
| `VRFLATSCORE_UNITY` | the path for `VRFLATSCORE_UNITY_VERSION` | An editor binary to use instead |
| `VRFLATSCORE_UNITY_VERSION` | `6000.3.22f1` | Which installed editor to look for |
| `VRFLATSCORE_URP_VERSION` | `17.3.0` | Which URP to resolve |
| `VRFLATSCORE_VERIFY_DIR` | a `mktemp -d` | Keep the project and log instead of deleting them |

First run downloads URP and takes several minutes; later runs are quicker.

## What it checks, and why each one needs running rather than reading

1. **The declared identities resolve.** The assembly names (`VRFlatsCore`,
   `VRFlatsCoreEditor`), every runtime namespace, the package id
   `com.binteca.vrflatscore`, and the exact type string consumers resolve by —
   `VRFlatsCore.Runtime.GaussianSplatRenderer, VRFlatsCore`. A rename can be
   perfectly consistent in the source and still not produce the assembly name a
   consumer asks for; only loading it settles that.
2. **`GaussianSplatURPFeature` compiles in, and implements `RecordRenderGraph`.**
   Unity 6 URP runs RenderGraph by default and never calls the legacy `Execute`,
   so a pass without it draws nothing while everything else looks healthy. The
   check is by reflection over the compiled type, not by grepping for the
   method name.
3. **The `GS_URP_RENDERGRAPH` version define actually fired.** This one earned
   its place: the first attempt used the expression `[17.0.0,)`, which Unity
   rejects outright (`ExpressionNotValidException`), so the RenderGraph code
   compiled out entirely — the source was correct, the asmdef was not, and
   nothing failed. Unity's syntax has no unbounded-range form; a bare version
   means "that version or newer". The runner greps the log for that exception
   as well as for `error CS`.
4. **Runtime splat buffers are refused when malformed.** Six behaviour
   assertions against `GaussianSplatAsset.SetRuntimeData`: a well-formed layer
   is accepted; a short position buffer, a colour buffer sized to the packed
   format instead of raw `float4`, a call before `Initialize`, an undeclared
   layer, and a clustered SH palette are each refused. These buffers reach the
   GPU with no bounds information of their own, so this is the difference
   between an exception with two numbers in it and a SIGSEGV in `libunity`.

## Read the log, not the exit code

**Measured 2026-09-09 on 6000.3.22f1: a batchmode run whose harness called
`EditorApplication.Exit(1)` still returned 0 to the shell.** So `run.sh` decides
from the log — `error CS` count, `ExpressionNotValidException` count, and the
presence of `[verify] RESULT ALL PASS` — and reports Unity's exit code as
information only. An absent `RESULT` line is a failure too: it means the harness
never ran.

`run.sh` itself exits **0** on a pass, **1** on a failure, and **2** when it
cannot run at all (no editor, no package). One trap for anyone editing it:
**bash lets a failing last command in an `EXIT` trap overwrite the script's exit
status** — measured 2026-09-09, a script ending `exit 0` with a cleanup trap
whose last test was false exited 1, so the runner reported failure on a passing
run. `cleanup` therefore ends with an explicit `return 0`.

## What a pass does and does not mean

**Does:** the package compiles clean and its declared identities and runtime
input handling behave as documented, on the editor and URP version that were
used.

**Does not:**

- **It is not a device test.** Nothing here renders a frame, and no APK is
  built. That the RenderGraph pass is *recorded* is checked; that it *draws
  correctly on a Quest 3* is not, and is a separate measurement.
- **It says nothing about other versions.** A pass on 6000.3.22f1 / URP 17.3.0
  is evidence about 6000.3.22f1 / URP 17.3.0. `package.json` declares a minimum
  of `2022.3`, inherited from upstream, and **that minimum has never been
  verified in this fork** — including the legacy `Execute` path this package
  keeps specifically for it.
- **It does not check the consuming project.** The dependency is a relative
  `file:` path with no version pin; a consumer's own suite is the only thing
  that tests the pair together.

## Recorded results

| Date | Editor / URP | Result |
| --- | --- | --- |
| 2026-09-09 | 6000.3.22f1 / URP 17.3.0 | **ALL PASS**, 0 compile errors. Two warnings remain, both `CS0618` in unmodified upstream code (`TextureCreationFlags.IgnoreMipmapLimit`, `Object.FindObjectOfType`), left alone to stay mergeable |

The 2026-09-09 baseline run — before the fixes in the same change — is worth
keeping: it compiled clean and every identity resolved, **and** it reported
`GSRenderPass declares: Dispose, Execute, OnCameraSetup`, with no
`RecordRenderGraph`. That is what "silently draws nothing on Unity 6" looks like
from a passing build.
