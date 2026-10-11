using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Measures the "screen" when it is only one submesh of a single combined model.
///
/// The problem: the whole phone is one GameObject with 14 materials, and the screen is Element 6
/// (Screen.mat). Renderer.bounds therefore gives the size of the entire phone, not of the screen,
/// so it cannot be used to set the RenderTexture's aspect.
///
/// This menu drills into that submesh's triangles alone and reports the three things you need:
///   1. The screen's real world size (non-uniform lossyScale already applied) → sets the RT size
///   2. The UV range the screen actually occupies → sets the material's Tiling/Offset so the RT
///      fills the screen exactly
///   3. Every value to enter, computed for you, with no arithmetic left to do by hand
/// </summary>
public static class ScreenSubmeshAnalyzer
{
    // The RenderTexture height we anchor on; the width is derived from the real screen aspect
    public const int ReferenceHeight = 2080;

    /// <summary>A measured screen — every value is in the MeshRenderer's local space except worldSize.</summary>
    public struct ScreenFace
    {
        public MeshRenderer renderer;
        public int          submesh;
        public Vector3      localCenter;
        public Vector3      localSize;
        public Vector3      worldSize;
        public Vector3      localNormal;   // the direction the screen faces, away from the body
        public int          widthAxis, heightAxis, normalAxis;

        public float Aspect   => worldSize[heightAxis] > 0f ? worldSize[widthAxis] / worldSize[heightAxis] : 0f;
        public int   SuggestedRtWidth => Mathf.RoundToInt(ReferenceHeight * Aspect / 2f) * 2;
    }

    /// <summary>
    /// Measures the submesh whose material name contains "screen" — shared with
    /// PhoneScreenQuadBuilder. Returns false after logging a self-explanatory error if it cannot
    /// find one.
    /// </summary>
    public static bool TryMeasureScreen(GameObject root, out ScreenFace face)
    {
        face = default;
        if (root == null) { Debug.LogError("[ScreenAnalyzer] No GameObject selected"); return false; }

        if (!TryFindScreen(root, out var renderer, out int index))
        {
            Debug.LogError($"[ScreenAnalyzer] No screen found in '{root.name}' — one of these has to hold:\n" +
                           "  • a child GameObject whose name contains 'screen' (PhoneScreen, say) that has a Mesh, or\n" +
                           "  • a submesh whose material name contains 'screen' (the old single-mesh phone)\n" +
                           "Rename the screen's GameObject or material to contain 'screen' and run this again", root);
            return false;
        }

        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;

        // index < 0 means the whole object is the screen (it is its own GameObject), so there is
        // no submesh to drill into
        int[] tris = index >= 0 ? mesh.GetTriangles(index) : mesh.triangles;
        if (tris.Length == 0) { Debug.LogError("[ScreenAnalyzer] The screen mesh is empty", root); return false; }

        var verts   = mesh.vertices;
        var normals = mesh.normals;
        var used    = new HashSet<int>(tris);

        Vector3 lmin = Vector3.positiveInfinity, lmax = Vector3.negativeInfinity, nSum = Vector3.zero;
        foreach (int v in used)
        {
            lmin = Vector3.Min(lmin, verts[v]);
            lmax = Vector3.Max(lmax, verts[v]);
            if (normals != null && normals.Length == verts.Length) nSum += normals[v];
        }

        Vector3 scale = renderer.transform.lossyScale;
        Vector3 local = lmax - lmin;
        Vector3 world = new Vector3(local.x * Mathf.Abs(scale.x),
                                    local.y * Mathf.Abs(scale.y),
                                    local.z * Mathf.Abs(scale.z));

        int thin = world.x <= world.y && world.x <= world.z ? 0 : world.y <= world.z ? 1 : 2;
        int axA  = thin == 0 ? 1 : 0;
        int axB  = thin == 2 ? 1 : 2;

        face.renderer    = renderer;
        face.submesh     = index;
        face.localCenter = (lmin + lmax) * 0.5f;
        face.localSize   = local;
        face.worldSize   = world;
        face.normalAxis  = thin;
        face.widthAxis   = world[axA] <= world[axB] ? axA : axB;
        face.heightAxis  = face.widthAxis == axA ? axB : axA;

        // ─────────────── which way the screen faces ───────────────
        // Facing the wrong way means the quad is swallowed by backface culling and simply vanishes,
        // while every other check still looks like it passed. That is very hard to spot by eye, so
        // we work it out two ways and prefer the more trustworthy one.
        //
        //   1. Compare against the body — the screen is always on the OUTSIDE of the body.
        //      Only works on phones where the screen is its own object (there is a body to compare to).
        //   2. The average vertex normal — fine for a single mesh where the screen is one sheet
        //      facing one way. But if the screen is a solid slab (front AND back faces), the normals
        //      cancel out to nearly zero and the direction becomes a coin flip, so this is the fallback.
        float outward = 0f;
        if (TryGetBodyCenterLocal(root, renderer, out Vector3 bodyLocal))
            outward = face.localCenter[thin] - bodyLocal[thin];

        if (Mathf.Abs(outward) < 1e-6f) outward = nSum[thin];

        Vector3 axis = Vector3.zero;
        axis[thin] = 1f;
        face.localNormal = outward < 0f ? -axis : axis;

        Debug.Log($"[ScreenAnalyzer] Using the screen on '{renderer.name}'" +
                  (index >= 0 ? $" submesh {index}" : " (whole object)") +
                  $" — {face.worldSize[face.widthAxis]:0.####} × {face.worldSize[face.heightAxis]:0.####} m, " +
                  $"aspect {face.Aspect:0.0000}, facing {face.localNormal}", renderer);
        return true;
    }

    /// <summary>
    /// Finds the screen inside a phone model — handles both layouts this project has:
    ///
    ///   Old phone (Z Flip 3) : one mesh, 14 materials; the screen is the submesh whose material
    ///                          name contains "screen"
    ///   New phone            : the screen is its own GameObject (PhoneBody / PhoneScreen / …)
    ///
    /// A returned submesh of -1 means the whole object is the screen, with nothing to drill into.
    ///
    /// GameObject names are checked before material names on purpose — while migrating, the old
    /// phone is usually kept parented under the new one for a while, and searching by material
    /// first would measure the old phone's screen instead.
    /// </summary>
    private static bool TryFindScreen(GameObject root, out MeshRenderer renderer, out int submesh)
    {
        renderer = null;
        submesh  = -1;

        MeshRenderer byName = null, byMat = null;
        int nameDepth = int.MaxValue, matDepth = int.MaxValue, matSub = -1;

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            // Skip the quad we placed ourselves — its name contains "Screen" too, and without this
            // a second run would measure the quad instead of the screen, compounding the error
            if (mr.name == PhoneScreenQuadBuilder.QuadName) continue;

            var filter = mr.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            int depth = DepthUnder(root.transform, mr.transform);

            if (IsScreenName(mr.name) && depth < nameDepth) { byName = mr; nameDepth = depth; }

            if (depth >= matDepth) continue;

            int sub = FindScreenSubmesh(mr, filter.sharedMesh);
            if (sub >= 0) { byMat = mr; matDepth = depth; matSub = sub; }
        }

        if (byName != null)
        {
            renderer = byName;
            // An object named like a screen may still have several submeshes (glass + panel), so
            // prefer one whose material name matches if there is one
            submesh  = FindScreenSubmesh(byName, byName.GetComponent<MeshFilter>().sharedMesh);
            return true;
        }

        if (byMat != null)
        {
            renderer = byMat;
            submesh  = matSub;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The centre of the body (everything under root that is neither the screen nor our own quad),
    /// converted into the screen's local space. Returns false when there is nothing to compare
    /// against, as on a single-mesh phone.
    /// </summary>
    private static bool TryGetBodyCenterLocal(GameObject root, MeshRenderer screen, out Vector3 localCenter)
    {
        localCenter = Vector3.zero;

        // Only count pieces actually adjacent to the screen — while migrating, the old phone is
        // usually parked under the new one, and counting it as "body" drags the centre towards it
        // and yields the wrong direction
        Bounds near = screen.bounds;
        near.Expand(near.size);

        Bounds body  = default;
        bool   found = false;

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == screen || mr.name == PhoneScreenQuadBuilder.QuadName) continue;
            if (!near.Intersects(mr.bounds)) continue;

            if (!found) { body = mr.bounds; found = true; }
            else         body.Encapsulate(mr.bounds);
        }

        if (!found) return false;

        localCenter = screen.transform.InverseTransformPoint(body.center);
        return true;
    }

    private static int FindScreenSubmesh(MeshRenderer mr, Mesh mesh)
    {
        var mats = mr.sharedMaterials;
        for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++)
            if (mats[i] != null && IsScreenName(mats[i].name)) return i;

        return -1;
    }

    private static bool IsScreenName(string name)
        => !string.IsNullOrEmpty(name) && name.ToLowerInvariant().Contains("screen");

    private static int DepthUnder(Transform root, Transform child)
    {
        int depth = 0;
        for (var t = child; t != null && t != root; t = t.parent) depth++;
        return depth;
    }

    [MenuItem("Rule of Horror/Phone/Measure Screen Submesh Size + UVs (select the phone first)", false, 60)]
    private static void Analyze()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("[ScreenAnalyzer] Nothing selected — select the phone's GameObject in the Hierarchy first");
            return;
        }

        // Use the same finder TryMeasureScreen uses, otherwise a phone with the screen split into
        // its own child reports on PhoneBody and the numbers do not match what the migrate menu says
        if (!TryFindScreen(go, out var renderer, out int screenSub))
        {
            Debug.LogError($"[ScreenAnalyzer] No screen found in '{go.name}' — there has to be a child " +
                           "GameObject whose name contains 'screen', or a submesh whose material name does", go);
            return;
        }

        Mesh mesh  = renderer.GetComponent<MeshFilter>().sharedMesh;
        var  mats  = renderer.sharedMaterials;
        var  t     = renderer.transform;
        var  scale = t.lossyScale;

        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenAnalyzer] {go.name}'s screen is on '{renderer.name}' — " +
                      $"mesh '{mesh.name}', {mesh.subMeshCount} submesh(es), lossyScale = {Fmt(scale)}");
        if (screenSub < 0) sb.AppendLine("  (the whole object is the screen, not split into submeshes)");
        if (!Approximately(scale.x, scale.y) || !Approximately(scale.x, scale.z))
            sb.AppendLine("  ⚠ non-uniform scale — the shape is squashed; the values below already account for it");

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            string matName = i < mats.Length && mats[i] != null ? mats[i].name : "(no material)";
            bool   isScreen = screenSub < 0 || i == screenSub;

            sb.AppendLine();
            sb.AppendLine($"── submesh {i} : {matName}{(isScreen ? "   ◄◄ the screen" : "")}");
            AppendSubmeshReport(sb, mesh, i, scale, isScreen, i < mats.Length ? mats[i] : null);
        }

        Debug.Log(sb.ToString(), go);
    }

    private static void AppendSubmeshReport(StringBuilder sb, Mesh mesh, int index, Vector3 scale,
                                            bool verbose, Material mat)
    {
        int[] tris = mesh.GetTriangles(index);
        if (tris.Length == 0) { sb.AppendLine("   (empty)"); return; }

        var verts = mesh.vertices;
        var uvs   = mesh.uv;

        // Only the vertices this submesh actually uses — mesh.bounds is no good, it covers everything
        var used = new HashSet<int>(tris);
        Vector3 lmin = Vector3.positiveInfinity, lmax = Vector3.negativeInfinity;
        Vector2 umin = Vector2.positiveInfinity, umax = Vector2.negativeInfinity;
        bool hasUv = uvs != null && uvs.Length == verts.Length;

        foreach (int v in used)
        {
            lmin = Vector3.Min(lmin, verts[v]);
            lmax = Vector3.Max(lmax, verts[v]);
            if (!hasUv) continue;
            umin = Vector2.Min(umin, uvs[v]);
            umax = Vector2.Max(umax, uvs[v]);
        }

        Vector3 local = lmax - lmin;
        Vector3 world = new Vector3(local.x * Mathf.Abs(scale.x),
                                    local.y * Mathf.Abs(scale.y),
                                    local.z * Mathf.Abs(scale.z));

        sb.AppendLine($"   {used.Count} vertices / {tris.Length / 3} triangles");
        sb.AppendLine($"   World size : X {world.x:0.####}   Y {world.y:0.####}   Z {world.z:0.####}");

        if (!verbose) return;

        // The screen is a flat sheet, so the thinnest axis is its thickness and the other two are
        // width × height
        int thin = world.x <= world.y && world.x <= world.z ? 0 : world.y <= world.z ? 1 : 2;
        int axA  = thin == 0 ? 1 : 0;
        int axB  = thin == 2 ? 1 : 2;
        int wAx  = world[axA] <= world[axB] ? axA : axB;      // width axis
        int hAx  = wAx == axA ? axB : axA;                    // height axis
        float faceW = world[wAx];
        float faceH = world[hAx];
        float aspect = faceH > 0f ? faceW / faceH : 0f;

        sb.AppendLine($"   Real screen: width {faceW:0.####}  height {faceH:0.####}  → aspect {aspect:0.0000}");

        if (!hasUv) { sb.AppendLine("   ⚠ the mesh has no UVs — fix the model, or use a child quad instead"); return; }

        Vector2 uvSize = umax - umin;
        sb.AppendLine($"   UV range   : u {umin.x:0.####}..{umax.x:0.####}   v {umin.y:0.####}..{umax.y:0.####}" +
                      $"   ({uvSize.x:0.####} wide × {uvSize.y:0.####} tall)");

        if (uvSize.x <= 0f || uvSize.y <= 0f)
        {
            sb.AppendLine("   ⚠ the UVs are degenerate — the screen has no UV layout, so a RenderTexture " +
                          "cannot be applied to this submesh directly");
            return;
        }

        // To make the RT fill the screen's UV area exactly: uv*tiling + offset must send umin→0 and umax→1
        Vector2 tiling = new Vector2(1f / uvSize.x, 1f / uvSize.y);
        Vector2 offset = new Vector2(-umin.x / uvSize.x, -umin.y / uvSize.y);

        int rtW = Mathf.RoundToInt(ReferenceHeight * aspect / 2f) * 2;   // keep it even

        AppendLinearityReport(sb, verts, uvs, used, wAx, hAx, uvSize, rtW);

        sb.AppendLine();
        sb.AppendLine("   ─── values to enter ───");
        sb.AppendLine($"   Screen.mat  Tiling = ({tiling.x:0.####}, {tiling.y:0.####})   Offset = ({offset.x:0.####}, {offset.y:0.####})");
        sb.AppendLine($"   RenderTexture size = {rtW} × {ReferenceHeight}");
        sb.AppendLine($"   Canvas Scaler Reference Resolution = {rtW} × {ReferenceHeight}  (must match the RT exactly)");

        if (mat == null) return;

        Vector2 curT = mat.HasProperty("_BaseMap") ? mat.GetTextureScale("_BaseMap") : Vector2.one;
        Vector2 curO = mat.HasProperty("_BaseMap") ? mat.GetTextureOffset("_BaseMap") : Vector2.zero;
        sb.AppendLine($"   Currently set  Tiling = ({curT.x:0.####}, {curT.y:0.####})   Offset = ({curO.x:0.####}, {curO.y:0.####})" +
                      (Approximately(curT.x, tiling.x) && Approximately(curT.y, tiling.y) &&
                       Approximately(curO.x, offset.x) && Approximately(curO.y, offset.y)
                           ? "   ✓ matches"
                           : "   ✗ does not match"));
    }

    /// <summary>
    /// Checks whether the screen's UVs are "straight" or skewed.
    ///
    /// On a flat screen with a correct UV layout, the mapping from position on the screen to UV is
    /// a single linear equation that holds across the whole sheet:  u = a·x + b·y + c
    ///
    /// When it is not — a trapezoidal UV layout, or each triangle laid out its own way — the texture
    /// kinks at the triangle seams, squeezed on one side and stretched on the other (the PS1 look).
    /// No Tiling/Offset can fix that; the model has to change, or a quad has to be laid over it.
    ///
    /// Method: least-squares fit the linear equation across every vertex, then measure how far the
    /// worst outlier falls from it, expressed in pixels on the real screen.
    /// </summary>
    private static void AppendLinearityReport(StringBuilder sb, Vector3[] verts, Vector2[] uvs,
                                              HashSet<int> used, int wAx, int hAx, Vector2 uvSize, int rtW)
    {
        if (used.Count < 4) return;

        // Normal equations for [p q 1], solved for u and v at the same time
        double[,] m = new double[3, 3];
        double[] ru = new double[3], rv = new double[3];
        foreach (int i in used)
        {
            double p = verts[i][wAx], q = verts[i][hAx];
            double[] r = { p, q, 1.0 };
            for (int a = 0; a < 3; a++)
            {
                for (int b = 0; b < 3; b++) m[a, b] += r[a] * r[b];
                ru[a] += r[a] * uvs[i].x;
                rv[a] += r[a] * uvs[i].y;
            }
        }

        if (!Solve3(m, ru, out var cu) || !Solve3(m, rv, out var cv))
        {
            sb.AppendLine("   (cannot check UV straightness — the vertices are collinear)");
            return;
        }

        double worstU = 0, worstV = 0;
        foreach (int i in used)
        {
            double p = verts[i][wAx], q = verts[i][hAx];
            worstU = System.Math.Max(worstU, System.Math.Abs(uvs[i].x - (cu[0] * p + cu[1] * q + cu[2])));
            worstV = System.Math.Max(worstV, System.Math.Abs(uvs[i].y - (cv[0] * p + cv[1] * q + cv[2])));
        }

        // Express the deviation in RenderTexture pixels, so it is clear whether the eye would catch it
        double pxU = uvSize.x > 0f ? worstU / uvSize.x * rtW : 0;
        double pxV = uvSize.y > 0f ? worstV / uvSize.y * ReferenceHeight : 0;
        double px  = System.Math.Max(pxU, pxV);

        sb.AppendLine();
        sb.AppendLine($"   UV straightness: worst deviation from the plane {px:0.0} px  " +
                      (px < 1.0 ? "✓ perfectly flat, no UV distortion possible"
                     : px < 8.0 ? "~ slightly skewed, barely visible"
                                : "✗ genuinely skewed — Tiling/Offset cannot fix this, lay a quad over it"));
    }

    /// <summary>Solves a 3×3 system by Gaussian elimination with partial pivoting.</summary>
    private static bool Solve3(double[,] src, double[] rhs, out double[] x)
    {
        var a = (double[,])src.Clone();
        var b = (double[])rhs.Clone();
        x = new double[3];

        for (int c = 0; c < 3; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < 3; r++)
                if (System.Math.Abs(a[r, c]) > System.Math.Abs(a[pivot, c])) pivot = r;
            if (System.Math.Abs(a[pivot, c]) < 1e-12) return false;

            if (pivot != c)
            {
                for (int k = 0; k < 3; k++) (a[c, k], a[pivot, k]) = (a[pivot, k], a[c, k]);
                (b[c], b[pivot]) = (b[pivot], b[c]);
            }

            for (int r = c + 1; r < 3; r++)
            {
                double f = a[r, c] / a[c, c];
                for (int k = c; k < 3; k++) a[r, k] -= f * a[c, k];
                b[r] -= f * b[c];
            }
        }

        for (int r = 2; r >= 0; r--)
        {
            double s = b[r];
            for (int k = r + 1; k < 3; k++) s -= a[r, k] * x[k];
            x[r] = s / a[r, r];
        }
        return true;
    }

    private static bool   Approximately(float a, float b) => Mathf.Abs(a - b) < 0.0005f;
    private static string Fmt(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
}
