using System;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VRFlatsCore.Runtime;

// Compile-and-behaviour check for the vrflatscore package, run by
// tools/verify/run.sh in a throwaway Unity project. It answers three questions
// that reading the source cannot:
//
//   1. Do the package's declared identities actually resolve? Assembly names,
//      namespaces, the renderer type string consumers use, the package id.
//      The 2026-09-08 rename was documented for a day as unverified because
//      nothing here had ever been built.
//   2. Does the URP feature implement RecordRenderGraph? Unity 6 URP runs
//      RenderGraph by default and silently draws nothing without it, and an
//      asmdef version define that fails to parse leaves the code uncompiled
//      with only a line in the editor log to say so.
//   3. Are runtime splat buffers checked before they reach the GPU? These are
//      handed over as raw buffers with no bounds information; a wrong length
//      is an out-of-bounds read in a shipped player, not a visual defect.
public static class PackageVerify
{
    static int s_Fail;

    static void Check(string what, bool ok, string detail)
    {
        if (!ok) s_Fail++;
        Debug.Log($"[verify] {(ok ? "PASS" : "FAIL")} {what}: {detail}");
    }

    public static void Run()
    {
        const string kRendererTypeString = "VRFlatsCore.Runtime.GaussianSplatRenderer, VRFlatsCore";
        var t = Type.GetType(kRendererTypeString);
        Check("renderer type string resolves", t != null, kRendererTypeString + " -> " + (t == null ? "null" : t.AssemblyQualifiedName));

        var runtime = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "VRFlatsCore");
        Check("runtime assembly named VRFlatsCore", runtime != null, runtime?.GetName().Name ?? "absent");

        var editor = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "VRFlatsCoreEditor");
        Check("editor assembly named VRFlatsCoreEditor", editor != null, editor?.GetName().Name ?? "absent");

        if (runtime != null)
        {
            var ns = runtime.GetTypes().Select(x => x.Namespace).Where(x => x != null).Distinct().OrderBy(x => x).ToArray();
            Check("runtime namespaces are VRFlatsCore.*", ns.All(n => n.StartsWith("VRFlatsCore")), string.Join(", ", ns));

            var urp = runtime.GetTypes().FirstOrDefault(x => x.Name == "GaussianSplatURPFeature");
            Check("URP feature compiled in (GS_ENABLE_URP)", urp != null, urp?.FullName ?? "absent - URP versionDefine did not fire");

            var pass = runtime.GetTypes().FirstOrDefault(x => x.Name == "GSRenderPass");
            if (pass != null)
            {
                var methods = pass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                  .Select(m => m.Name).Distinct().OrderBy(x => x).ToArray();
                Debug.Log($"[verify] INFO GSRenderPass declares: {string.Join(", ", methods)}");
                Check("GSRenderPass implements RecordRenderGraph", methods.Contains("RecordRenderGraph"),
                      methods.Contains("RecordRenderGraph") ? "present - draws under URP RenderGraph" : "ABSENT - draws nothing under URP RenderGraph");
                Check("GS_URP_RENDERGRAPH fired (PassData nested type)",
                      pass.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Any(x => x.Name == "PassData"),
                      string.Join(", ", pass.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Select(x => x.Name).DefaultIfEmpty("none")));
                Check("legacy Execute override kept (URP 14 / Compatibility Mode)", methods.Contains("Execute"), string.Join(", ", methods));
            }

            Check("QuestBudget present", runtime.GetTypes().Any(x => x.Name == "QuestBudget"), "QuestBudget");
        }

        var pkg = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(p => p.name == "com.binteca.vrflatscore");
        Check("package id com.binteca.vrflatscore registered", pkg != null,
              pkg == null ? "absent" : $"{pkg.name}@{pkg.version} source={pkg.source} path={pkg.resolvedPath}");

        RuntimeDataValidation();

        Debug.Log($"[verify] RESULT {(s_Fail == 0 ? "ALL PASS" : s_Fail + " FAILURE(S)")}");
        EditorApplication.Exit(s_Fail == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------------
    // SetRuntimeData is the package's only path that parses data in a
    // shipped player. These buffers are handed to the GPU with no further
    // bounds information, so a wrong length is an out-of-bounds read rather
    // than a visual defect. Assert it is refused.
    // ------------------------------------------------------------------
    const int kSplats = 1000;

    static GaussianSplatAsset NewInitialisedAsset()
    {
        var a = ScriptableObject.CreateInstance<GaussianSplatAsset>();
        a.name = "verify-asset";
        a.Initialize(kSplats,
            GaussianSplatAsset.VectorFormat.Float32,   // pos
            GaussianSplatAsset.VectorFormat.Float32,   // scale
            GaussianSplatAsset.ColorFormat.Float16x4,
            GaussianSplatAsset.SHFormat.Float16,
            Vector3.zero, Vector3.one, Array.Empty<GaussianSplatAsset.CameraInfo>(),
            new[] { new int2(0, kSplats) });
        return a;
    }

    static NativeArray<byte> Buf(long n) => new NativeArray<byte>((int)n, Allocator.Persistent);

    static void RuntimeDataValidation()
    {
        long posN   = GaussianSplatAsset.CalcPosDataSize(kSplats, GaussianSplatAsset.VectorFormat.Float32);
        long otherN = GaussianSplatAsset.CalcOtherDataSize(kSplats, GaussianSplatAsset.VectorFormat.Float32);
        long colN   = (long)kSplats * GaussianSplatAsset.kRuntimeColorStride;
        long shN    = GaussianSplatAsset.CalcSHDataSize(kSplats, GaussianSplatAsset.SHFormat.Float16);
        long chunkN = GaussianSplatAsset.CalcChunkDataSize(kSplats);
        Debug.Log($"[verify] INFO expected runtime sizes for {kSplats} splats: pos={posN} other={otherN} colour={colN} sh={shN} chunk={chunkN}");

        // 1. Correct buffers are accepted.
        var good = NewInitialisedAsset();
        try
        {
            good.SetRuntimeData(0, Buf(chunkN), Buf(posN), Buf(otherN), Buf(colN), Buf(shN));
            Check("well-formed runtime layer accepted", good.HasRuntimeData, $"HasRuntimeData={good.HasRuntimeData}, posDataSize={good.posDataSize}");
        }
        catch (Exception e)
        {
            Check("well-formed runtime layer accepted", false, e.GetType().Name + ": " + e.Message);
        }
        finally { good.DisposeRuntimeData(); UnityEngine.Object.DestroyImmediate(good); }

        // 2. A short position buffer is refused, by name and with both numbers.
        CheckThrows("short posData refused", () =>
        {
            var a = NewInitialisedAsset();
            try { a.SetRuntimeData(0, default, Buf(posN - 12), Buf(otherN), Buf(colN), Buf(shN)); }
            finally { UnityEngine.Object.DestroyImmediate(a); }
        });

        // 3. Colour sized to the packed format rather than raw float4 is refused.
        //    This is the mistake CalcColorDataSize invites, so it is worth pinning.
        CheckThrows("packed-size colorData refused", () =>
        {
            var a = NewInitialisedAsset();
            try { a.SetRuntimeData(0, default, Buf(posN), Buf(otherN), Buf(GaussianSplatAsset.CalcColorDataSize(kSplats, GaussianSplatAsset.ColorFormat.Float16x4)), Buf(shN)); }
            finally { UnityEngine.Object.DestroyImmediate(a); }
        });

        // 4. SetRuntimeData before Initialize is refused rather than silently unchecked.
        CheckThrows("SetRuntimeData before Initialize refused", () =>
        {
            var a = ScriptableObject.CreateInstance<GaussianSplatAsset>();
            a.name = "uninitialised";
            try { a.SetRuntimeData(0, default, Buf(posN), Buf(otherN), Buf(colN), Buf(shN)); }
            finally { UnityEngine.Object.DestroyImmediate(a); }
        });

        // 5. An undeclared layer is refused.
        CheckThrows("undeclared layer refused", () =>
        {
            var a = NewInitialisedAsset();
            try { a.SetRuntimeData(3, default, Buf(posN), Buf(otherN), Buf(colN), Buf(shN)); }
            finally { UnityEngine.Object.DestroyImmediate(a); }
        });

        // 6. A clustered SH palette has no runtime path and is refused explicitly.
        CheckThrows("clustered SH at runtime refused", () =>
        {
            var a = ScriptableObject.CreateInstance<GaussianSplatAsset>();
            a.name = "clustered";
            a.Initialize(kSplats, GaussianSplatAsset.VectorFormat.Float32, GaussianSplatAsset.VectorFormat.Float32,
                GaussianSplatAsset.ColorFormat.Float16x4, GaussianSplatAsset.SHFormat.Cluster16k,
                Vector3.zero, Vector3.one, Array.Empty<GaussianSplatAsset.CameraInfo>(), new[] { new int2(0, kSplats) });
            try { a.SetRuntimeData(0, default, Buf(posN), Buf(otherN), Buf(colN), Buf(shN)); }
            finally { UnityEngine.Object.DestroyImmediate(a); }
        });
    }

    static void CheckThrows(string what, Action action)
    {
        try
        {
            action();
            Check(what, false, "no exception thrown - the malformed buffer would have reached the GPU");
        }
        catch (Exception e)
        {
            bool ok = e is ArgumentException || e is InvalidOperationException;
            Check(what, ok, e.GetType().Name + ": " + e.Message);
        }
    }
}
