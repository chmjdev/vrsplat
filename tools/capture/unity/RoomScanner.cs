// SPDX-License-Identifier: MIT
//
// On-headset room scanner: captures posed passthrough frames on a Quest 3/3S
// and writes them in the layout tools/capture/remote-train.sh consumes.
//
// This fills step 1 of the capture pipeline. Until now README.md's step 1 was
// "walk the room slowly" with no tool behind it -- the reconstruction half was
// automated and the shooting half was not.
//
// WHY WebCamTexture AND NOT PassthroughCameraAccess
// Meta's current PCA component (PassthroughCameraAccess) ships in MRUK v81+,
// which is Meta XR SDK. This repository is an MIT Unity package on pure Unity
// OpenXR, and tools/capture/README.md is explicit that GPL/vendor code stays
// out of the compiler. The component Meta replaced was a wrapper around Unity's
// stock WebCamTexture, so the SDK-free path is to use WebCamTexture directly.
// What that costs: no camera intrinsics or extrinsics from the API. It does not
// block the pipeline, because remote-train.sh runs `colmap
// automatic_reconstructor`, which solves intrinsics from the images themselves.
// Head poses are still written alongside, for metric/gravity alignment later.
//
// UNITY MODULE DEPENDENCY, and it is not the one you would guess:
// WebCamTexture is forwarded to UnityEngine.AudioModule, so a project without
// com.unity.modules.audio fails to compile this file with CS1069 naming the
// Audio module. Add com.unity.modules.audio (video is a sensible companion).
//
// Prerequisites (Meta, verified 2026-09-10): Horizon OS v74+, Quest 3 or 3S,
// the Passthrough feature enabled, and horizonos.permission.HEADSET_CAMERA.
// PCA does not work in XR Simulator -- this is a device-only tool.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class RoomScanner : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] Camera m_HeadCamera;

    [Header("Capture")]
    [Tooltip("Frames written per second. COLMAP wants overlap, not volume.")]
    [SerializeField] float m_Fps = 3f;
    [SerializeField] Vector2Int m_Resolution = new Vector2Int(1280, 960);
    [Tooltip("JPEG quality, 1-100.")]
    [SerializeField] int m_JpegQuality = 92;
    [Tooltip("Stop automatically after this many frames. 0 = no limit.")]
    [SerializeField] int m_MaxFrames = 900;

    public const string kCameraPermission = "horizonos.permission.HEADSET_CAMERA";

    WebCamTexture m_Cam;
    Texture2D m_Readback;
    string m_ScanDir, m_FramesDir;
    StreamWriter m_Poses;
    int m_FrameIndex;
    float m_NextCapture;
    bool m_Scanning, m_PrevTrigger, m_PermissionAsked;

    public bool IsScanning => m_Scanning;
    public int FrameCount => m_FrameIndex;
    public string ScanDirectory => m_ScanDir;

    void Start()
    {
        if (m_HeadCamera == null) m_HeadCamera = Camera.main;
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(kCameraPermission))
        {
            var cb = new PermissionCallbacks();
            cb.PermissionGranted += id => Debug.Log($"[roomscan] {id} granted");
            cb.PermissionDenied += id => Debug.LogWarning($"[roomscan] {id} DENIED - cannot scan");
            Permission.RequestUserPermission(kCameraPermission, cb);
            m_PermissionAsked = true;
            Debug.Log("[roomscan] requesting " + kCameraPermission);
        }
#endif
        Debug.Log("[roomscan] ready - pull the RIGHT trigger to start/stop a scan");
    }

    void Update()
    {
        PollTrigger();
        if (m_Scanning && Time.time >= m_NextCapture)
        {
            m_NextCapture = Time.time + 1f / Mathf.Max(0.1f, m_Fps);
            CaptureFrame();
        }
    }

    // Right-hand trigger starts and stops. Read through the legacy XR input
    // device API so the build needs no action-map asset.
    void PollTrigger()
    {
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!right.isValid || !right.TryGetFeatureValue(CommonUsages.triggerButton, out bool t))
            return;
        if (t && !m_PrevTrigger)
        {
            if (m_Scanning) StopScan("trigger");
            else StartScan();
        }
        m_PrevTrigger = t;
    }

    public void StartScan()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(kCameraPermission))
        {
            Debug.LogWarning($"[roomscan] cannot start: {kCameraPermission} not granted");
            if (!m_PermissionAsked) { Permission.RequestUserPermission(kCameraPermission); m_PermissionAsked = true; }
            return;
        }
#endif
        if (!OpenCamera()) return;

        string sceneId = "scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        m_ScanDir = Path.Combine(Application.persistentDataPath, "scans", sceneId);
        m_FramesDir = Path.Combine(m_ScanDir, "frames");
        Directory.CreateDirectory(m_FramesDir);

        // JSON Lines, one pose per frame: appendable, and a truncated scan
        // (battery, crash) still leaves every frame written so far usable.
        m_Poses = new StreamWriter(Path.Combine(m_ScanDir, "poses.jsonl"), false, Encoding.UTF8);
        m_FrameIndex = 0;
        m_NextCapture = 0f;
        m_Scanning = true;
        Debug.Log($"[roomscan] START {sceneId} -> {m_ScanDir} ({m_Cam.width}x{m_Cam.height} @ {m_Fps}fps)");
    }

    bool OpenCamera()
    {
        if (m_Cam != null && m_Cam.isPlaying) return true;

        var devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogError("[roomscan] no camera devices. On Quest this means the Passthrough " +
                           "feature is off, the permission was refused, or the OS is below v74.");
            return false;
        }
        foreach (var d in devices)
            Debug.Log($"[roomscan] camera device: '{d.name}' frontFacing={d.isFrontFacing}");

        m_Cam = new WebCamTexture(devices[0].name, m_Resolution.x, m_Resolution.y, Mathf.RoundToInt(m_Fps));
        m_Cam.Play();
        if (!m_Cam.isPlaying)
        {
            Debug.LogError("[roomscan] camera did not start playing");
            return false;
        }
        Debug.Log($"[roomscan] opened '{devices[0].name}' at {m_Cam.width}x{m_Cam.height}");
        return true;
    }

    void CaptureFrame()
    {
        if (m_Cam == null || !m_Cam.isPlaying || m_Cam.width < 16) return;   // width<16 = not ready yet

        if (m_Readback == null || m_Readback.width != m_Cam.width || m_Readback.height != m_Cam.height)
            m_Readback = new Texture2D(m_Cam.width, m_Cam.height, TextureFormat.RGB24, false);

        m_Readback.SetPixels32(m_Cam.GetPixels32());
        m_Readback.Apply(false);

        string name = $"frame_{m_FrameIndex:D5}.jpg";
        File.WriteAllBytes(Path.Combine(m_FramesDir, name), m_Readback.EncodeToJPG(m_JpegQuality));

        // Head pose, not camera pose: without MRUK the API gives us no camera
        // extrinsics. Recorded so a future posed pipeline can use it as a
        // PRIOR (metric scale and gravity), never as fixed truth.
        var t = m_HeadCamera != null ? m_HeadCamera.transform : transform;
        var p = t.position; var q = t.rotation;
        m_Poses.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{{\"frame\":\"{0}\",\"t\":{1:F4},\"pos\":[{2:F5},{3:F5},{4:F5}],\"rot\":[{5:F6},{6:F6},{7:F6},{8:F6}]}}",
            name, Time.realtimeSinceStartup, p.x, p.y, p.z, q.x, q.y, q.z, q.w));

        m_FrameIndex++;
        if (m_FrameIndex % 25 == 0) Debug.Log($"[roomscan] {m_FrameIndex} frames");
        if (m_MaxFrames > 0 && m_FrameIndex >= m_MaxFrames) StopScan("frame limit");
    }

    public void StopScan(string why)
    {
        if (!m_Scanning) return;
        m_Scanning = false;

        m_Poses?.Flush();
        m_Poses?.Dispose();
        m_Poses = null;
        if (m_Cam != null && m_Cam.isPlaying) m_Cam.Stop();

        // Manifest last, so its presence means the scan completed cleanly.
        var manifest = new StringBuilder();
        manifest.AppendLine("{");
        manifest.AppendLine($"  \"sceneId\": \"{Path.GetFileName(m_ScanDir)}\",");
        manifest.AppendLine($"  \"frames\": {m_FrameIndex},");
        manifest.AppendLine($"  \"resolution\": [{(m_Readback != null ? m_Readback.width : 0)}, {(m_Readback != null ? m_Readback.height : 0)}],");
        manifest.AppendLine($"  \"fps\": {m_Fps.ToString(CultureInfo.InvariantCulture)},");
        manifest.AppendLine($"  \"device\": \"{SystemInfo.deviceModel}\",");
        manifest.AppendLine($"  \"captured\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",");
        manifest.AppendLine("  \"poses\": \"head poses only - no camera extrinsics without MRUK; treat as priors\",");
        manifest.AppendLine($"  \"stoppedBy\": \"{why}\"");
        manifest.AppendLine("}");
        File.WriteAllText(Path.Combine(m_ScanDir, "scan.json"), manifest.ToString());

        Debug.Log($"[roomscan] STOP ({why}) - {m_FrameIndex} frames in {m_ScanDir}");
    }

    void OnDisable() { if (m_Scanning) StopScan("disabled"); }
}
