# vrsplat — maintenance roadmap

Why this fork exists, and what we intend to carry in it. See `Readme.md`
for the attribution and licence position (MIT, unchanged).

The driving constraint is **Quest 3 standalone**: the Interactive suite
renders real captured training rooms in a headset with no PC attached.
Upstream reports ~72fps up to roughly **400k Gaussians** on that hardware.
That number is upstream's, not ours — it is the working assumption until we
measure a real capture on our own device, at which point this file gets the
measured figure instead.

## Already upstream (verified present — do not rebuild)

Reviewing https://github.com/MrNeRF/awesome-3D-gaussian-splatting against
this package, most of the "compression" techniques listed there are already
here, in `GaussianSplatAssetCreator` / `GaussianSplatAsset`:

- **Quality presets** with measured trade-offs, e.g. `VeryLow` ≈ 18.6×
  smaller at 32.3 PSNR, `Medium` ≈ 5.1× smaller at 47.5 PSNR.
- **Vector quantisation**: `Norm11` (4 bytes), `Norm6` (2 bytes).
- **Palette clustering**: `Cluster64k`, `Cluster32k`.

So the first job is not to add compression — it is to pick and document the
right preset for the Quest budget.

## Wanted, in priority order

1. **A documented Quest preset, and a guard.**
   The importer offers quality levels but nothing tells an author that a
   capture will miss frame rate on device. Add a preset tuned to the ~400k
   budget, and warn at import when a capture exceeds it. This is the change
   that most protects the suite: the failure it prevents is silent —
   everything imports fine and only the headset tells you, late.

2. **SOG / compressed-format import.**
   PlayCanvas' SOG format reports 15–20× smaller than PLY. It is
   web-oriented, so this is real importer work rather than a flag, but the
   size win matters: captures ship inside the APK's StreamingAssets, and
   load time and download size are both real UAT costs.

3. **An LOD ladder.**
   AURA-style progressive levels, so a room can hold detail near the
   trainee and shed it at distance instead of being decimated uniformly.
   This is what would let a capture be larger than the flat budget allows.

4. **Author-facing decimation guidance.**
   `SplatTransform` (converts formats, emits LOD) is the practical tool for
   getting a raw capture down to budget. Document the exact invocation we
   use rather than telling authors to "decimate".

## Ground rules for this fork

- **Stay mergeable.** Prefer additive changes; keep upstream's file layout
  so `upstream` can still be pulled.
- **MIT preserved.** Original copyright and attribution stay untouched.
- **Measured, not assumed.** Performance claims in this repo carry the
  device and the method, or they are labelled as upstream's numbers.
