using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lays a quad showing the RenderTexture over the phone's screen, instead of projecting the
/// texture onto the model's own submesh.
///
/// Why this exists: the Z Flip 3's screen submesh has 76 triangles and its UVs are not coplanar
/// (ScreenSubmeshAnalyzer measures roughly 25px of deviation from the plane). Each triangle maps
/// the texture with its own affine transform, so the image kinks at the seams, squeezed on one
/// side and stretched on the other. Tiling/Offset cannot fix that, because it just multiplies
/// UVs that are already skewed — the surface being projected onto has to change.
///
/// The quad built here is perfectly flat with UVs at exactly 0..1, so there is nothing left to
/// distort. And because we control its size, its aspect always matches the RenderTexture.
///
/// The size is computed from the real world size of the submesh, so the phone's non-uniform
/// scale of (50, 40, 50) is already accounted for — there is no scale to correct by hand.
/// </summary>
public static class PhoneScreenQuadBuilder
{
    public  const string QuadName     = "ScreenQuad";
    private const string FolderPath   = "Assets/Prefab/LightPole/Phone";
    private const string MaterialPath = FolderPath + "/PhoneScreenUnlit.mat";

    /// <summary>The RenderTexture PhoneCamera renders into and the quad displays —
    /// PhoneScreenMigrator uses this too.</summary>
    public const string RenderTexPath = "Assets/Texture/Phone/PhoneRenderTexture.renderTexture";

    // How far in front of the screen the quad sits, in metres — enough to avoid z-fighting with
    // the original surface
    private const float SurfaceOffset = 0.0005f;

    // ─────────────────────────── rounded corners ───────────────────────────
    // Phone screens have rounded corners while the quad is a plain rectangle, so all four corners
    // poke out past the body. The fix is to round the mesh's corners to match — the UVs still span
    // 0..1 over the full frame, so the image does not move, the corners are simply shaved off, the
    // same way a real screen has its corner pixels covered by the body.
    //
    // The radius lives in EditorPrefs rather than a const because it has to be nudged a step at a
    // time and judged by eye in the Scene view. Every model rounds its corners differently, and it
    // cannot be measured off the mesh (the rounding belongs to the body, not to the screen).

    private const string RadiusKey = "ROH_PhoneScreenCornerPercent";
    private const string FlipKey   = "ROH_PhoneScreenFlipped";

    private const float DefaultCornerPercent = 10f;   // % of the screen width
    private const int   CornerSegments       = 12;    // arc resolution per corner

    /// <summary>Corner radius, as a percentage of the screen width.</summary>
    private static float CornerPercent
    {
        get => EditorPrefs.GetFloat(RadiusKey, DefaultCornerPercent);
        set => EditorPrefs.SetFloat(RadiusKey, Mathf.Clamp(value, 0f, 50f));
    }

    [MenuItem("Rule of Horror/Phone/Build Screen Quad (fixes UV distortion) %#q", false, 61)]
    private static void Build() => BuildFor(Selection.activeGameObject);

    [MenuItem("Rule of Horror/Phone/Screen Quad — Rebuild Facing The Other Way (if invisible)", false, 65)]
    private static void BuildFlipped() => BuildFor(Selection.activeGameObject, true);

    [MenuItem("Rule of Horror/Phone/Screen Quad — More Corner Rounding %#=", false, 66)]
    private static void MoreRound() => NudgeCorner(+1f);

    [MenuItem("Rule of Horror/Phone/Screen Quad — Less Corner Rounding %#-", false, 67)]
    private static void LessRound() => NudgeCorner(-1f);

    /// <summary>
    /// Nudges the corner radius one step and rebuilds immediately — keep the quad or the phone
    /// selected and hold the shortcut down.
    ///
    /// Reuses the facing direction the quad was last built with, otherwise a phone that needs the
    /// flipped build would disappear every time the corners are adjusted.
    /// </summary>
    private static void NudgeCorner(float step)
    {
        var sel = Selection.activeGameObject;
        if (sel == null) { Debug.LogError("[ScreenQuad] Select the ScreenQuad or the phone first"); return; }

        var root = sel.name == QuadName && sel.transform.parent != null
                 ? sel.transform.parent.gameObject
                 : sel;

        CornerPercent += step;
        if (BuildFor(root, EditorPrefs.GetBool(FlipKey, false)) != null)
            Debug.Log($"[ScreenQuad] Corner radius = {CornerPercent:0.#}% of the width");
    }

    /// <summary>
    /// Builds or updates the screen quad for the given phone — returns null if the screen could
    /// not be measured.
    ///
    /// Split out from the menu item so PhoneScreenMigrator can call it while migrating, rather
    /// than asking the user to run two menu items back to back.
    /// </summary>
    public static Transform BuildFor(GameObject root, bool faceOtherWay = false)
    {
        if (!ScreenSubmeshAnalyzer.TryMeasureScreen(root, out var face)) return null;

        // The outward direction cannot always be found automatically on a solid slab of a screen,
        // so this menu flips it. We rebuild the whole thing rather than rotating the existing quad
        // 180°, because rotating also mirrors the image left to right.
        if (faceOtherWay) face.localNormal = -face.localNormal;
        EditorPrefs.SetBool(FlipKey, faceOtherWay);

        var parent = face.renderer.transform;
        var quad   = FindQuad(parent);

        if (quad == null)
        {
            var go = new GameObject(QuadName, typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Build Screen Quad");
            go.transform.SetParent(parent, false);
            quad = go.transform;
        }
        else
        {
            Undo.RecordObject(quad.gameObject, "Update Screen Quad");
            Undo.RecordObject(quad, "Update Screen Quad");
        }

        quad.gameObject.layer = face.renderer.gameObject.layer;   // keep it off the UI layer PhoneCamera renders

        // The quad's up axis is the screen's long axis; its forward is the direction the screen faces
        Vector3 up = Vector3.zero;
        up[face.heightAxis] = 1f;

        quad.localRotation = Quaternion.LookRotation(face.localNormal, up);
        quad.localScale    = new Vector3(face.localSize[face.widthAxis], face.localSize[face.heightAxis], 1f);

        // The push-out distance is in real metres, then divided by the scale to get back to local units
        float scaleOnNormal = Mathf.Abs(face.renderer.transform.lossyScale[face.normalAxis]);
        float pushLocal     = scaleOnNormal > 0.0001f ? SurfaceOffset / scaleOnNormal : SurfaceOffset;
        quad.localPosition  = face.localCenter + face.localNormal * pushLocal;

        var filter = quad.GetComponent<MeshFilter>();
        var render = quad.GetComponent<MeshRenderer>();

        // Work the radius out in metres first, then convert it back into mesh units per axis — the
        // mesh is a 1×1 square stretched by two different scales, so a single radius on both axes
        // would make the corners elliptical, flattened by the screen's aspect ratio
        float worldW = face.worldSize[face.widthAxis];
        float worldH = face.worldSize[face.heightAxis];
        float radius = worldW * CornerPercent * 0.01f;

        float rx = worldW > 0.0001f ? radius / worldW : 0f;
        float ry = worldH > 0.0001f ? radius / worldH : 0f;

        filter.sharedMesh      = LoadOrCreateMesh(face.renderer.name, rx, ry);
        render.sharedMaterial  = LoadOrCreateMaterial();
        render.shadowCastingMode = ShadowCastingMode.Off;
        render.receiveShadows    = false;
        render.lightProbeUsage   = LightProbeUsage.Off;

        EditorUtility.SetDirty(quad.gameObject);
        Selection.activeGameObject = quad.gameObject;

        int rtW = face.SuggestedRtWidth;
        Debug.Log($"[ScreenQuad] Quad placed — real size {face.worldSize[face.widthAxis]:0.####} × " +
                  $"{face.worldSize[face.heightAxis]:0.####} m (aspect {face.Aspect:0.0000})\n" +
                  $"  • Set PhoneRenderTexture to {rtW} × {ScreenSubmeshAnalyzer.ReferenceHeight} to match\n" +
                  $"  • The RenderTexture can come off Screen.mat now (set Base Map to None, black), " +
                  $"otherwise a second image sits behind the quad\n" +
                  $"  • Corner radius is {CornerPercent:0.#}% of the width — adjust with Ctrl+Shift+= / Ctrl+Shift+-\n" +
                  $"  • If the image is sideways or mirrored, use the Rotate 90° / Flip menu items", quad.gameObject);

        return quad;
    }

    [MenuItem("Rule of Horror/Phone/Screen Quad — Push In Front Of The Glass", false, 62)]
    private static void PushInFrontOfGlass()
    {
        var root = Selection.activeGameObject;
        if (root != null && root.name == QuadName && root.transform.parent != null)
            root = root.transform.parent.gameObject;

        if (!ScreenSubmeshAnalyzer.TryMeasureScreen(root, out var face)) return;

        var quad = FindQuad(face.renderer.transform);
        if (quad == null)
        {
            Debug.LogError("[ScreenQuad] No ScreenQuad yet — build one with the Build Screen Quad menu first", root);
            return;
        }

        if (face.submesh < 0)
        {
            Debug.Log("[ScreenQuad] This phone's screen is its own GameObject rather than a submesh of the " +
                      "body, so this menu item does not apply. If glass is covering it, move the quad out " +
                      "by hand in the Inspector (the quad's Z axis is the direction the screen faces)",
                      face.renderer);
            return;
        }

        var filter = face.renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;

        Mesh mesh  = filter.sharedMesh;
        var  mats  = face.renderer.sharedMaterials;
        var  verts = mesh.vertices;

        int   nAx  = face.normalAxis;
        float sign = face.localNormal[nAx];                    // +1 or -1
        float half = 0.5f;

        // The screen's extent in plane, used to keep only the vertices genuinely above the screen.
        // Otherwise the body edge or a protruding camera bump would drag the quad much further out
        // than it needs to go.
        float wMin = face.localCenter[face.widthAxis]  - face.localSize[face.widthAxis]  * half;
        float wMax = face.localCenter[face.widthAxis]  + face.localSize[face.widthAxis]  * half;
        float hMin = face.localCenter[face.heightAxis] - face.localSize[face.heightAxis] * half;
        float hMax = face.localCenter[face.heightAxis] + face.localSize[face.heightAxis] * half;

        float best        = face.localCenter[nAx] * sign;
        int   blockerMesh = -1;

        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (sub == face.submesh) continue;

            foreach (int v in mesh.GetTriangles(sub))
            {
                Vector3 p = verts[v];
                if (p[face.widthAxis]  < wMin || p[face.widthAxis]  > wMax) continue;
                if (p[face.heightAxis] < hMin || p[face.heightAxis] > hMax) continue;

                float depth = p[nAx] * sign;
                if (depth <= best) continue;

                best        = depth;
                blockerMesh = sub;
            }
        }

        if (blockerMesh < 0)
        {
            Debug.Log("[ScreenQuad] No submesh covers the screen — whatever is washing out the text does not " +
                      "come from the phone model. Look at post-processing (Bloom / Tonemapping) or another " +
                      "object in the scene instead", quad);
            return;
        }

        float scaleOnNormal = Mathf.Abs(face.renderer.transform.lossyScale[nAx]);
        float clearance     = scaleOnNormal > 0.0001f ? SurfaceOffset / scaleOnNormal : SurfaceOffset;

        Undo.RecordObject(quad, "Push Screen Quad");

        Vector3 pos = quad.localPosition;
        float   old = pos[nAx];
        pos[nAx]    = (best + clearance) * sign;
        quad.localPosition = pos;
        EditorUtility.SetDirty(quad.gameObject);

        string blockerName = blockerMesh < mats.Length && mats[blockerMesh] != null
                           ? mats[blockerMesh].name : $"submesh {blockerMesh}";
        float  movedMm     = Mathf.Abs(pos[nAx] - old) * scaleOnNormal * 1000f;

        Debug.Log($"[ScreenQuad] '{blockerName}' was covering the screen — moved the quad {movedMm:0.##} mm " +
                  "out, in front of it\n" +
                  "If there is still a reflection it comes from somewhere other than the phone model", quad);
    }

    [MenuItem("Rule of Horror/Phone/Screen Quad — Rotate 90°", false, 63)]
    private static void Rotate90()
    {
        var quad = RequireQuad();
        if (quad == null) return;

        Undo.RecordObject(quad, "Rotate Screen Quad");
        quad.localRotation *= Quaternion.Euler(0f, 0f, 90f);

        // Rotating swaps which axis is width and which is height, so the scale has to swap too or
        // the shape comes out wrong
        var s = quad.localScale;
        quad.localScale = new Vector3(s.y, s.x, s.z);
    }

    [MenuItem("Rule of Horror/Phone/Screen Quad — Flip Horizontally", false, 64)]
    private static void FlipHorizontal()
    {
        var quad = RequireQuad();
        if (quad == null) return;

        Undo.RecordObject(quad, "Flip Screen Quad");
        var s = quad.localScale;
        quad.localScale = new Vector3(-s.x, s.y, s.z);
    }

    private static Transform RequireQuad()
    {
        var sel = Selection.activeGameObject;
        if (sel == null) { Debug.LogError("[ScreenQuad] Select the ScreenQuad (or the phone) first"); return null; }

        var quad = sel.name == QuadName ? sel.transform : FindQuad(sel.transform);
        if (quad == null) Debug.LogError("[ScreenQuad] No ScreenQuad found — build one with the Build Screen Quad menu", sel);
        return quad;
    }

    private static Transform FindQuad(Transform parent)
    {
        foreach (Transform c in parent) if (c.name == QuadName) return c;
        return null;
    }

    /// <summary>
    /// A hand-built quad rather than Unity's primitive, because the primitive's facing direction
    /// has changed between versions. This one has normal +Z, full 0..1 UVs, and winding that
    /// follows Unity's rule (a front face is clockwise when seen from the front).
    ///
    /// The asset is per-screen, because the shape depends on that phone's aspect ratio — sharing
    /// one file would mean building a quad for one phone skews the corners on the other.
    /// </summary>
    private static Mesh LoadOrCreateMesh(string id, float rx, float ry)
    {
        string path = $"{FolderPath}/PhoneScreenQuad_{Sanitize(id)}.mesh";

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh();

        mesh.name = $"PhoneScreenQuad_{id}";
        FillRoundedRect(mesh, rx, ry);

        EnsureFolder();
        if (isNew) AssetDatabase.CreateAsset(mesh, path);
        else       EditorUtility.SetDirty(mesh);

        AssetDatabase.SaveAssets();
        return mesh;
    }

    /// <summary>
    /// A -0.5..0.5 rectangle with rounded corners, triangulated as a fan from its centre (the
    /// shape is convex, so a centre fan is enough).
    /// UV = position + 0.5, so the image still spans the whole frame no matter how much of the
    /// corners is cut away.
    /// </summary>
    private static void FillRoundedRect(Mesh mesh, float rx, float ry)
    {
        rx = Mathf.Clamp(rx, 0f, 0.5f);
        ry = Mathf.Clamp(ry, 0f, 0.5f);

        // Walk counter-clockwise starting at the bottom-right corner, so the fan triangles come
        // out clockwise when viewed from the front
        var outline = new List<Vector3>(CornerSegments * 4 + 8);
        AddArc(outline,  0.5f - rx, -0.5f + ry, rx, ry, -90f,   0f);
        AddArc(outline,  0.5f - rx,  0.5f - ry, rx, ry,   0f,  90f);
        AddArc(outline, -0.5f + rx,  0.5f - ry, rx, ry,  90f, 180f);
        AddArc(outline, -0.5f + rx, -0.5f + ry, rx, ry, 180f, 270f);

        int n = outline.Count;

        var verts   = new Vector3[n + 1];
        var uvs     = new Vector2[n + 1];
        var normals = new Vector3[n + 1];
        var tris    = new int[n * 3];

        verts[0]   = Vector3.zero;                  // the centre is vertex 0
        uvs[0]     = new Vector2(0.5f, 0.5f);
        normals[0] = Vector3.forward;

        for (int i = 0; i < n; i++)
        {
            verts[i + 1]   = outline[i];
            uvs[i + 1]     = new Vector2(outline[i].x + 0.5f, outline[i].y + 0.5f);
            normals[i + 1] = Vector3.forward;

            int next = (i + 1) % n;
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = next + 1;
            tris[i * 3 + 2] = i + 1;
        }

        mesh.Clear();
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.normals   = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
    }

    private static void AddArc(List<Vector3> into, float cx, float cy, float rx, float ry,
                               float fromDeg, float toDeg)
    {
        // Radius 0 means a square corner — one point is enough, no need for 13 duplicates
        if (rx <= 0.0001f && ry <= 0.0001f) { into.Add(new Vector3(cx, cy, 0f)); return; }

        for (int i = 0; i <= CornerSegments; i++)
        {
            float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)CornerSegments);
            into.Add(new Vector3(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, 0f));
        }
    }

    private static string Sanitize(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');

        return sb.Length > 0 ? sb.ToString() : "Screen";
    }

    private static Material LoadOrCreateMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;

        // Unlit, because a phone screen emits its own light and should not dim with the scene at night
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[ScreenQuad] URP/Unlit not found, falling back to Unlit/Texture");
            shader = Shader.Find("Unlit/Texture");
        }

        var mat = new Material(shader) { name = "PhoneScreenUnlit" };
        var rt  = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexPath);
        if (rt == null)
            Debug.LogWarning($"[ScreenQuad] No RenderTexture at {RenderTexPath} — assign it to the material by hand");
        else
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", rt);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", rt);
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

        EnsureFolder();
        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    private static void EnsureFolder()
    {
        if (Directory.Exists(FolderPath)) return;
        Directory.CreateDirectory(FolderPath);
        AssetDatabase.Refresh();
    }
}
