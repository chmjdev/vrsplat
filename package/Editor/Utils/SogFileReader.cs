// SPDX-License-Identifier: MIT
//
// Estate fork: PlayCanvas SOG ("Spatially Ordered Gaussians") import
// (ROADMAP.md item 2). SOG reports ~15-20x smaller than PLY.
//
// UNVERIFIED: written source-only in a session with no working Unity
// process (see CHANGELOG.md and docs/verification.md) -- never compiled,
// never run against a real .sog file. Treat every claim below as "read from
// the schema", not "checked against real data".
//
// Schema below is transcribed from the meta.json schema published at
// https://developer.playcanvas.com/user-manual/gaussian-splatting/formats/sog/
// (fetched 2026-09-09), then CROSS-CHECKED 2026-09-09 by actually installing
// Node.js v24.21.0 LTS + `npm install @playcanvas/splat-transform` (v3.4.2,
// commit 0cb47cd) in this session's writable workspace and running it for
// real against a synthetic 20,000-splat PLY: `splat-transform test_input.ply
// sog_out/meta.json -w` produced a real meta.json whose top-level shape
// (version:2, count, means{mins,maxs,files}, scales{codebook,files},
// quats{files}, sh0{codebook,files}) matches SogMeta/SogMeans/SogScales/
// SogQuats/SogSh0 below exactly. The quaternion formula that gap-1 below
// used to say was unconfirmed was then read directly out of that npm
// package's own shipped source (node_modules/@playcanvas/splat-transform/
// dist/index.mjs, function unpackQuat, V2/current format) and is now
// implemented in DequantizeQuat with that provenance. What is still NOT
// implemented, and why:
//
//   1. WebP pixel decoding in this C# importer. Unity's ImageConversion.LoadImage
//      supports only JPEG and PNG (https://docs.unity3d.com/ScriptReference/ImageConversion.LoadImage.html);
//      there is no native WebP path, and this machine has no dwebp/cwebp/
//      libwebp/ImageMagick to shell out to (checked). A real decoder DOES
//      exist and DOES run, though: `@playcanvas/splat-transform` ships its
//      own WebPCodec (used internally by its SOG reader) and was actually
//      exercised above via Node.js. The concrete, proven integration path
//      for a future pass is therefore an Editor-only `Process.Start` call
//      into a small Node script using that same package (or a direct
//      libwebp .NET binding) -- not "no known solution", just not wired up
//      here. ISogTextureDecoder is the seam it plugs into;
//      DefaultSogTextureDecoder throws rather than silently returning wrong
//      pixels.
namespace VRFlatsCore.Editor.Utils
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Unity.Collections;
    using UnityEngine;

    /// <summary>meta.json root. Field names match the SOG spec verbatim so
    /// JSONParser.FromJson&lt;SogMeta&gt;() (this repo's embedded TinyJSON,
    /// see TinyJsonParser.cs) can deserialize it directly.</summary>
    [Serializable]
    public class SogMeta
    {
        public int version;
        public int count;
        public bool antialias;
        public SogMeans means;
        public SogScales scales;
        public SogQuats quats;
        public SogSh0 sh0;
        public SogShN shN; // null when the asset has no higher-order SH
    }

    [Serializable]
    public class SogMeans
    {
        public float[] mins;  // [x,y,z], log-domain
        public float[] maxs;  // [x,y,z], log-domain
        public string[] files; // ["means_l.webp", "means_u.webp"]
    }

    [Serializable]
    public class SogScales
    {
        public float[] codebook; // 256 entries, log-domain
        public string[] files;   // ["scales.webp"]
    }

    [Serializable]
    public class SogQuats
    {
        public string[] files; // ["quats.webp"]
        // No codebook for quats -- confirmed correct: V2 quats use a
        // "smallest-three" geometric decode instead. See DequantizeQuat.
    }

    [Serializable]
    public class SogSh0
    {
        public float[] codebook; // 256 entries, gamma-space DC
        public string[] files;   // ["sh0.webp"]
    }

    [Serializable]
    public class SogShN
    {
        public int count; // 1..65536
        public int bands; // 1..3
        public float[] codebook; // 256 entries, gamma-space AC
        public string[] files;   // ["shN_centroids.webp", "shN_labels.webp"]
    }

    /// <summary>Decoded RGBA8 pixels for one SOG image, width/height as
    /// declared by the image itself (the meta.json schema fetched for this
    /// implementation does not carry explicit width/height fields separate
    /// from the image files, so callers must read them off the decoded
    /// texture).</summary>
    public struct SogImage
    {
        public int width;
        public int height;
        /// <summary>RGBA8, width*height*4 bytes, row-major from the top-left
        /// (matching Unity's Texture2D.LoadImage output convention).</summary>
        public NativeArray<byte> pixels;
    }

    /// <summary>
    /// The seam a real WebP decoder plugs into. Editor-only by construction:
    /// SOG import, like the existing PLY import, runs in the Editor
    /// (PLYFileReader.cs), never in a shipped player.
    /// </summary>
    public interface ISogTextureDecoder
    {
        SogImage Decode(string filePath);
    }

    /// <summary>Throws with a specific, actionable message rather than
    /// returning wrong or blank pixels. See the file-level note above for
    /// what plugging in a real decoder requires.</summary>
    public sealed class DefaultSogTextureDecoder : ISogTextureDecoder
    {
        public SogImage Decode(string filePath)
        {
            throw new NotImplementedException(
                $"SOG import: no WebP decoder is configured to read '{filePath}'. " +
                "Unity's ImageConversion.LoadImage supports only JPEG/PNG " +
                "(no native WebP), and this environment has no dwebp/cwebp/libwebp/ImageMagick " +
                "available to shell out to at import time. Provide an ISogTextureDecoder " +
                "(e.g. a libwebp .NET binding, or an Editor-only process call to a decoder " +
                "installed on the machine actually running this importer) via " +
                "SogFileReader.ReadBundle(..., decoder).");
        }
    }

    public static class SogFileReader
    {
        /// <summary>
        /// Reads and validates a SOG bundle's meta.json from a directory
        /// layout (the multi-file form; a zipped bundle is documented as an
        /// equivalent but is not handled here -- unzip first). Does not
        /// decode any image; call DecodeMeans/DecodeScales/DecodeSh0 (or a
        /// caller-supplied decoder path) separately once a real
        /// ISogTextureDecoder is available.
        /// </summary>
        public static SogMeta ReadMeta(string bundleDir)
        {
            string metaPath = Path.Combine(bundleDir, "meta.json");
            if (!File.Exists(metaPath))
                throw new FileNotFoundException($"SOG import: no meta.json in '{bundleDir}'.", metaPath);

            string json = File.ReadAllText(metaPath);
            var meta = json.FromJson<SogMeta>();
            if (meta == null)
                throw new IOException($"SOG import: '{metaPath}' did not parse as the documented SOG meta.json schema.");

            Validate(meta, bundleDir);
            return meta;
        }

        static void Validate(SogMeta meta, string bundleDir)
        {
            if (meta.count <= 0)
                throw new IOException($"SOG import: meta.json declares count={meta.count}, expected > 0.");
            if (meta.means?.files == null || meta.means.files.Length != 2)
                throw new IOException("SOG import: meta.json 'means' must declare exactly 2 files (means_l, means_u).");
            if (meta.means.mins == null || meta.means.mins.Length != 3 || meta.means.maxs == null || meta.means.maxs.Length != 3)
                throw new IOException("SOG import: meta.json 'means.mins'/'means.maxs' must each have 3 components (x,y,z).");
            if (meta.scales?.codebook == null || meta.scales.codebook.Length != 256)
                throw new IOException($"SOG import: meta.json 'scales.codebook' must have 256 entries, got {meta.scales?.codebook?.Length ?? 0}.");
            if (meta.sh0?.codebook == null || meta.sh0.codebook.Length != 256)
                throw new IOException($"SOG import: meta.json 'sh0.codebook' must have 256 entries, got {meta.sh0?.codebook?.Length ?? 0}.");
            if (meta.quats?.files == null || meta.quats.files.Length != 1)
                throw new IOException("SOG import: meta.json 'quats' must declare exactly 1 file.");

            foreach (var files in new[] { meta.means.files, meta.scales.files, meta.quats.files, meta.sh0.files })
            foreach (var f in files)
            {
                string p = Path.Combine(bundleDir, f);
                if (!File.Exists(p))
                    throw new FileNotFoundException($"SOG import: meta.json references '{f}' which is missing from '{bundleDir}'.", p);
            }
        }

        /// <summary>unlog(n) = sign(n) * (exp(|n|) - 1). Verified against the
        /// published spec, not against a real decoded sample (no decoder is
        /// available in this environment -- see the file-level note).</summary>
        public static float Unlog(float n)
        {
            float s = Mathf.Sign(n);
            return s * (Mathf.Exp(Mathf.Abs(n)) - 1f);
        }

        /// <summary>Per-axis log-domain value from two 8-bit channels (upper,
        /// lower) linearly interpolated between mins/maxs, then Unlog'd to a
        /// scene-space coordinate. axisIndex in [0,2].</summary>
        public static float DequantizePositionAxis(byte lower8, byte upper8, float min, float max, int axisIndex)
        {
            int quantized = (upper8 << 8) | lower8; // 16-bit value per the spec
            float t = quantized / 65535f;
            float logDomain = Mathf.Lerp(min, max, t);
            return Unlog(logDomain);
        }

        /// <summary>scale = exp(codebook[pixelValue]).</summary>
        public static float DequantizeScale(byte pixelValue, float[] codebook) => Mathf.Exp(codebook[pixelValue]);

        /// <summary>Base colour DC term: 0.5 + codebook[pixelValue] * 0.28209479177387814
        /// (the SH0 normalization constant, 1/(2*sqrt(pi))); opacity is alpha/255,
        /// both verified against the published spec.</summary>
        public static void DequantizeSh0(byte r, byte g, byte b, byte a, float[] codebook,
            out float colorR, out float colorG, out float colorB, out float opacity)
        {
            const float kSh0Scale = 0.28209479177387814f;
            colorR = 0.5f + codebook[r] * kSh0Scale;
            colorG = 0.5f + codebook[g] * kSh0Scale;
            colorB = 0.5f + codebook[b] * kSh0Scale;
            opacity = a / 255f;
        }

        static readonly int[][] kQuatIdx =
        {
            new[] {1, 2, 3},
            new[] {0, 2, 3},
            new[] {0, 1, 3},
            new[] {0, 1, 2},
        };

        /// <summary>
        /// "Smallest-three" quaternion decode, V2 (current) SOG format.
        /// r,g,b are the three stored components (the component NOT equal to
        /// maxComp, in ascending index order); a is the tag byte, where
        /// maxComp = tag - 252 identifies which of the four quaternion
        /// components was omitted (it is reconstructed to make the
        /// quaternion unit-length with a positive sign on that component).
        ///
        /// Verified 2026-09-09 against @playcanvas/splat-transform v3.4.2
        /// (commit 0cb47cd)'s own shipped implementation --
        /// node_modules/@playcanvas/splat-transform/dist/index.mjs,
        /// function unpackQuat (V2 path; a distinct unpackQuat$1 exists for
        /// legacy V1 files and is not implemented here) -- not invented.
        /// That source returns components in (w,x,y,z) order; this method
        /// does the same.
        /// </summary>
        public static Quaternion DequantizeQuat(byte r, byte g, byte b, byte tag)
        {
            int maxComp = tag - 252;
            if (maxComp < 0 || maxComp > 3)
                throw new ArgumentOutOfRangeException(nameof(tag),
                    $"SOG import: quats.webp alpha byte {tag} does not decode to a valid maxComp " +
                    $"(expected tag-252 in [0,3], got {maxComp}). Malformed or non-V2 quats.webp.");

            const float sqrt2 = 1.4142135623730951f;
            float a = (r / 255f * 2f - 1f) / sqrt2;
            float b2 = (g / 255f * 2f - 1f) / sqrt2;
            float c = (b / 255f * 2f - 1f) / sqrt2;

            float[] comps = { 0f, 0f, 0f, 0f };
            var idx = kQuatIdx[maxComp];
            comps[idx[0]] = a;
            comps[idx[1]] = b2;
            comps[idx[2]] = c;

            float t = 1f - (comps[0] * comps[0] + comps[1] * comps[1] + comps[2] * comps[2] + comps[3] * comps[3]);
            comps[maxComp] = Mathf.Sqrt(Mathf.Max(0f, t));

            // comps is (w,x,y,z); Unity's Quaternion constructor is (x,y,z,w).
            return new Quaternion(comps[1], comps[2], comps[3], comps[0]);
        }
    }
}
