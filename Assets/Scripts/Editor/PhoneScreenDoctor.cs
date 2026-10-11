using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Walks every link in the chain that can leave the phone screen black and reports which one is
/// broken.
///
/// The image passes through four hops on its way from the UI to the screen, and a break at any of
/// them looks identical from the outside:
///   HomeCanvas → PhoneCamera → RenderTexture → the ScreenQuad's material
///
/// This tool fixes nothing; it is read-only, and exists to say where to look first.
/// </summary>
public static class PhoneScreenDoctor
{
    [MenuItem("Rule of Horror/Phone/Diagnose Black Screen", false, 58)]
    private static void Diagnose()
    {
        var sb = new StringBuilder("[PhoneDoctor] Phone screen diagnosis\n");

        // ── RenderTexture ────────────────────────────────────────────────────
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(PhoneScreenQuadBuilder.RenderTexPath);
        if (rt == null)
        {
            Debug.LogError($"[PhoneDoctor] No RenderTexture at {PhoneScreenQuadBuilder.RenderTexPath} — stopping here");
            return;
        }

        float rtAspect = (float)rt.width / rt.height;
        sb.AppendLine($"1) RenderTexture : {rt.width} × {rt.height} (aspect {rtAspect:0.0000})");

        // ── The camera rendering into the RT ─────────────────────────────────
        Camera phoneCam = null;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam.targetTexture != rt) continue;

            phoneCam = cam;
            bool live = cam.isActiveAndEnabled;
            sb.AppendLine($"2) Camera rendering into the RT : '{cam.name}' {(live ? "active" : "⚠ disabled (the screen will be pure black)")}");
            sb.AppendLine($"   Culling Mask = {MaskText(cam.cullingMask)}");
            sb.AppendLine($"   Clear Flags = {cam.clearFlags}, background = {cam.backgroundColor}");
            break;
        }

        if (phoneCam == null)
        {
            sb.AppendLine("2) ⚠ No camera in the scene has its Target Texture set to this RT — the screen is certainly black");
            Debug.LogWarning(sb.ToString());
            return;
        }

        // ── Canvases that render into that camera ────────────────────────────
        int canvasCount = 0;
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.worldCamera != phoneCam) continue;
            canvasCount++;

            sb.AppendLine($"3) Canvas '{canvas.name}' : {(canvas.gameObject.activeInHierarchy ? "active" : "⚠ inactive")}" +
                          $", Render Mode = {canvas.renderMode}");

            // A camera that does not render the canvas's layer renders nothing but empty space
            if ((phoneCam.cullingMask & (1 << canvas.gameObject.layer)) == 0)
                sb.AppendLine($"   ⚠ The canvas is on layer '{LayerMask.LayerToName(canvas.gameObject.layer)}', " +
                              $"which is not in '{phoneCam.name}'s Culling Mask — the camera cannot see it");

            if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                float d = canvas.planeDistance;
                if (d < phoneCam.nearClipPlane || d > phoneCam.farClipPlane)
                    sb.AppendLine($"   ⚠ Plane Distance {d} is outside the camera's Near {phoneCam.nearClipPlane} – " +
                                  $"Far {phoneCam.farClipPlane} range — everything is clipped away");
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                bool matches = scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                               Mathf.Approximately(scaler.referenceResolution.x, rt.width) &&
                               Mathf.Approximately(scaler.referenceResolution.y, rt.height);

                sb.AppendLine($"   CanvasScaler {scaler.uiScaleMode} ref = " +
                              $"{scaler.referenceResolution.x:0} × {scaler.referenceResolution.y:0} " +
                              (matches ? "(matches the RT)" : "⚠ does not match the RT → the UI gets scaled a second time"));
            }

            // Which children are genuinely on — every panel being off looks black too, and that is
            // not the image chain's fault.
            //
            // We count enabled Graphics as well as active objects: a panel that is on but whose
            // contents are all off, or that is just an empty grouping box, gives the same black
            // screen for an entirely different reason.
            int active = 0, total = 0, drawn = 0;
            foreach (Transform child in canvas.transform)
            {
                total++;
                bool on = child.gameObject.activeSelf;
                if (on) active++;

                int graphics = 0;
                foreach (var g in child.GetComponentsInChildren<Graphic>(false))
                    if (g.enabled && g.color.a > 0.01f) graphics++;

                drawn += on ? graphics : 0;
                sb.AppendLine($"     {(on ? "on " : "off")} {child.name}  ({graphics} thing(s) actually drawn)");
            }

            sb.AppendLine($"   Total: {active} of {total} active, {drawn} thing(s) drawn" +
                          (drawn == 0 ? "  ⚠ nothing is drawn at all, so the screen is just the camera's background color" : ""));
        }

        if (canvasCount == 0)
            sb.AppendLine($"3) ⚠ No Canvas has '{phoneCam.name}' in its Render Camera field — the camera renders nothing");

        // ── ScreenQuad ───────────────────────────────────────────────────────
        var quad = FindQuad();
        if (quad == null)
        {
            sb.AppendLine("4) ⚠ No ScreenQuad in the scene — run 'Migrate Screen To New Phone' first");
            Debug.LogWarning(sb.ToString());
            return;
        }

        var qr = quad.GetComponent<MeshRenderer>();
        sb.AppendLine($"4) ScreenQuad : under '{(quad.parent != null ? quad.parent.name : "-")}' " +
                      $"{(quad.gameObject.activeInHierarchy ? "active" : "⚠ inactive")}" +
                      $"{(qr != null && qr.enabled ? "" : " ⚠ the MeshRenderer is disabled")}");

        Vector3 s = quad.lossyScale;   // the mesh is 1×1, so the real size equals the scale
        float quadAspect = Mathf.Abs(s.y) > 0.0001f ? Mathf.Abs(s.x / s.y) : 0f;
        sb.AppendLine($"   Real size {Mathf.Abs(s.x):0.####} × {Mathf.Abs(s.y):0.####} m " +
                      $"(aspect {quadAspect:0.0000})" +
                      (Mathf.Abs(quadAspect - rtAspect) > 0.02f ? "  ⚠ does not match the RT → the image is stretched" : ""));

        int quadLayer = quad.gameObject.layer;
        sb.AppendLine($"   Layer = '{LayerMask.LayerToName(quadLayer)}'");

        // Facing the wrong way means backface culling eats it: every other check passes and yet
        // nothing is visible. Compare against the body: the quad's front must point away from the
        // centre of the phone, not into it.
        if (TryGetBodyCenter(quad, out Vector3 bodyCenter))
        {
            Vector3 outward = quad.position - bodyCenter;
            if (Vector3.Dot(quad.forward, outward) < 0f)
                sb.AppendLine("   ⚠⚠ The quad faces into the phone — backface culling hides it entirely. " +
                              "Run 'Screen Quad — Rebuild Facing The Other Way'");
            else
                sb.AppendLine("   Facing outward correctly");
        }

        // The most common cause of a black screen: the quad sits on a layer PhoneCamera renders,
        // so the camera films the screen it is feeding — a feedback loop that comes out black or
        // as noise
        if ((phoneCam.cullingMask & (1 << quadLayer)) != 0)
            sb.AppendLine($"   ⚠⚠ The quad is inside '{phoneCam.name}'s Culling Mask — the camera is filming its own " +
                          "screen (feedback loop). Move the quad and the phone to a layer this camera does not render");

        // And can the main camera see the quad at all?
        bool seenBySomeone = false;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam == phoneCam || cam.targetTexture != null || !cam.isActiveAndEnabled) continue;
            if ((cam.cullingMask & (1 << quadLayer)) != 0) { seenBySomeone = true; break; }
        }
        if (!seenBySomeone)
            sb.AppendLine("   ⚠ No main camera renders this layer — the quad will never appear on screen");

        // ── The quad's material ──────────────────────────────────────────────
        var mat = qr != null ? qr.sharedMaterial : null;
        if (mat == null)
        {
            sb.AppendLine("5) ⚠ The ScreenQuad has no material");
        }
        else
        {
            Texture tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
            if (tex == null && mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");

            sb.AppendLine($"5) material '{mat.name}' (shader {mat.shader.name})");
            sb.AppendLine(tex == rt
                ? "   Base Map = the phone's RenderTexture, correct"
                : $"   ⚠ Base Map = {(tex == null ? "empty" : tex.name)}, not the RenderTexture — assign it by hand");

            if (mat.HasProperty("_BaseColor"))
            {
                var c = mat.GetColor("_BaseColor");
                if (c.maxColorComponent < 0.2f)
                    sb.AppendLine($"   ⚠ Base Color is very dark ({c}) — it multiplies the image down to black, set it to white");
            }
        }

        sb.Append("\nIf every check passes and it is still black, press Play and open the phone — outside Play mode " +
                  "the app panels are off, so an empty screen is expected");

        Debug.Log(sb.ToString(), quad);
    }

    /// <summary>The centre of the pieces around the quad (the phone body) — the quad itself excluded.</summary>
    private static bool TryGetBodyCenter(Transform quad, out Vector3 center)
    {
        center = Vector3.zero;

        var scope = quad.parent != null ? quad.parent : quad;
        Bounds body  = default;
        bool   found = false;

        foreach (var mr in scope.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.transform == quad) continue;

            if (!found) { body = mr.bounds; found = true; }
            else         body.Encapsulate(mr.bounds);
        }

        if (!found) return false;

        center = body.center;
        return true;
    }

    private static Transform FindQuad()
    {
        var sel = Selection.activeGameObject;
        if (sel != null)
        {
            if (sel.name == PhoneScreenQuadBuilder.QuadName) return sel.transform;

            foreach (var mr in sel.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.name == PhoneScreenQuadBuilder.QuadName) return mr.transform;
        }

        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None))
            if (mr.name == PhoneScreenQuadBuilder.QuadName) return mr.transform;

        return null;
    }

    private static string MaskText(int mask)
    {
        if (mask == ~0) return "Everything";

        var sb = new StringBuilder();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) == 0) continue;

            string name = LayerMask.LayerToName(i);
            if (string.IsNullOrEmpty(name)) continue;

            if (sb.Length > 0) sb.Append(", ");
            sb.Append(name);
        }

        return sb.Length > 0 ? sb.ToString() : "Nothing";
    }
}
