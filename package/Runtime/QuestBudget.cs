// SPDX-License-Identifier: MIT
// Estate fork (chmjdev/vrsplat): ROADMAP item 1 — a documented Quest budget,
// and a guard that speaks up at import time instead of letting the headset
// deliver the news late.

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// The splat budget for the target device, stated once with its
    /// provenance attached.
    ///
    /// The number is upstream's, not ours: ninjamode/Unity-VR-Gaussian-Splatting
    /// reports ~72fps up to roughly 400k Gaussians on a Quest 3, standalone.
    /// It stands as the working assumption until a capture is measured on our
    /// own device — at which point <see cref="MeasuredOnOurDevice"/> flips and
    /// the figures here become ours. Nothing else may flip it.
    /// </summary>
    public static class QuestBudget
    {
        public const string Device = "Meta Quest 3, standalone";
        public const int TargetFps = 72;
        public const int MaxSplats = 400_000;
        public const bool MeasuredOnOurDevice = false;
        public const string Provenance =
            "upstream (ninjamode/Unity-VR-Gaussian-Splatting), not yet measured on our own device";

        public enum Verdict
        {
            /// <summary>At or under budget — convert and ship.</summary>
            Within,
            /// <summary>Up to 1.5x — trim before converting; it will miss frame rate.</summary>
            Over,
            /// <summary>Past 1.5x — this is a building, not a room. Crop first, then trim.</summary>
            FarOver,
        }

        public static Verdict Assess(int splatCount)
        {
            if (splatCount <= MaxSplats) return Verdict.Within;
            if (splatCount <= MaxSplats * 3L / 2L) return Verdict.Over;
            return Verdict.FarOver;
        }

        /// <summary>
        /// The message a HelpBox shows for an over-budget capture. Written
        /// once here so the importer and the renderer inspector cannot drift
        /// apart, and so the provenance line travels with every warning.
        /// </summary>
        public static string Describe(int splatCount)
        {
            var verdict = Assess(splatCount);
            string headline = verdict switch
            {
                Verdict.Within => $"{splatCount:N0} splats — within the ~{MaxSplats:N0} budget for {Device}.",
                Verdict.Over =>
                    $"{splatCount:N0} splats exceeds the ~{MaxSplats:N0} budget for {Device} " +
                    $"({TargetFps}fps). Trim before shipping: crop to the room with cutouts, delete " +
                    "floaters with the splat edit tools, then Export modified PLY and re-import.",
                _ =>
                    $"{splatCount:N0} splats is {(double)splatCount / MaxSplats:F1}x the ~{MaxSplats:N0} " +
                    $"budget for {Device}. A room fits; a building does not — crop to the one room " +
                    "the lesson uses before trimming anything else.",
            };
            return headline + $"\nBudget figure: {Provenance}.";
        }
    }
}
