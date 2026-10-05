using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// วัด "หน้าจอ" ที่เป็นแค่ submesh หนึ่งของโมเดลก้อนเดียว
///
/// ปัญหา: โทรศัพท์ทั้งเครื่องเป็น GameObject เดียว มี 14 material หน้าจอคือ Element 6 (Screen.mat)
/// เพราะงั้น Renderer.bounds ให้ขนาดของทั้งเครื่อง ไม่ใช่ของหน้าจอ เอาไปตั้งสัดส่วน RenderTexture ไม่ได้
///
/// เมนูนี้เจาะเข้าไปดูเฉพาะ triangle ของ submesh นั้น แล้วบอก 3 อย่างที่ต้องรู้:
///   1. ขนาดจริงของหน้าจอในโลก (คิด lossyScale ที่ไม่เท่ากันทุกแกนให้แล้ว) → เอาไปตั้งขนาด RT
///   2. ช่วง UV ที่หน้าจอใช้จริง → เอาไปตั้ง Tiling/Offset ของ material ให้ RT เต็มหน้าจอพอดี
///   3. ค่าที่ควรตั้งทั้งหมด คำนวณมาให้เลย ไม่ต้องมานั่งคูณเอง
/// </summary>
public static class ScreenSubmeshAnalyzer
{
    // ความสูงของ RenderTexture ที่จะยึดเป็นหลัก แล้วคำนวณความกว้างตามสัดส่วนหน้าจอจริง
    public const int ReferenceHeight = 2080;

    /// <summary>ผลการวัดหน้าจอ — ทุกค่าอยู่ใน local space ของ MeshRenderer ยกเว้น worldSize</summary>
    public struct ScreenFace
    {
        public MeshRenderer renderer;
        public int          submesh;
        public Vector3      localCenter;
        public Vector3      localSize;
        public Vector3      worldSize;
        public Vector3      localNormal;   // ทิศที่หน้าจอหันออกนอกเครื่อง
        public int          widthAxis, heightAxis, normalAxis;

        public float Aspect   => worldSize[heightAxis] > 0f ? worldSize[widthAxis] / worldSize[heightAxis] : 0f;
        public int   SuggestedRtWidth => Mathf.RoundToInt(ReferenceHeight * Aspect / 2f) * 2;
    }

    /// <summary>
    /// วัด submesh ที่ material ชื่อมีคำว่า "screen" — ใช้ร่วมกับ PhoneScreenQuadBuilder
    /// คืน false พร้อม error ที่อธิบายได้เอง ถ้าหาไม่เจอ
    /// </summary>
    public static bool TryMeasureScreen(GameObject root, out ScreenFace face)
    {
        face = default;
        if (root == null) { Debug.LogError("[ScreenAnalyzer] ยังไม่ได้เลือก GameObject"); return false; }

        var filter   = root.GetComponentInChildren<MeshFilter>();
        var renderer = root.GetComponentInChildren<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || renderer == null)
        {
            Debug.LogError($"[ScreenAnalyzer] '{root.name}' ไม่มี MeshFilter/MeshRenderer ที่มี mesh", root);
            return false;
        }

        Mesh mesh = filter.sharedMesh;
        var  mats = renderer.sharedMaterials;
        int  index = -1;
        for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++)
            if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("screen")) { index = i; break; }

        if (index < 0)
        {
            Debug.LogError($"[ScreenAnalyzer] '{root.name}' ไม่มี submesh ไหนที่ material ชื่อมีคำว่า 'screen'", root);
            return false;
        }

        int[] tris = mesh.GetTriangles(index);
        if (tris.Length == 0) { Debug.LogError("[ScreenAnalyzer] submesh หน้าจอว่าง", root); return false; }

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

        // ทิศที่หันออก: เอาจาก normal เฉลี่ยของ vertex จริง ไม่ใช่เดาเอา
        Vector3 axis = Vector3.zero;
        axis[thin] = 1f;
        face.localNormal = nSum[thin] < 0f ? -axis : axis;
        return true;
    }

    [MenuItem("Rule of Horror/Phone/วัดขนาด+UV ของ submesh หน้าจอ (เลือก object โทรศัพท์ก่อน)", false, 60)]
    private static void Analyze()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("[ScreenAnalyzer] ยังไม่ได้เลือกอะไร — เลือก GameObject ของโทรศัพท์ใน Hierarchy ก่อน");
            return;
        }

        var filter   = go.GetComponentInChildren<MeshFilter>();
        var renderer = go.GetComponentInChildren<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || renderer == null)
        {
            Debug.LogError($"[ScreenAnalyzer] '{go.name}' ไม่มี MeshFilter/MeshRenderer ที่มี mesh อยู่", go);
            return;
        }

        Mesh mesh  = filter.sharedMesh;
        var  mats  = renderer.sharedMaterials;
        var  t     = renderer.transform;
        var  scale = t.lossyScale;

        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenAnalyzer] {go.name} — mesh '{mesh.name}', {mesh.subMeshCount} submesh, lossyScale = {Fmt(scale)}");
        if (!Approximately(scale.x, scale.y) || !Approximately(scale.x, scale.z))
            sb.AppendLine("  ⚠ scale ไม่เท่ากันทุกแกน — รูปทรงถูกบีบอยู่ ค่าด้านล่างคิดผลของมันให้แล้ว");

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            string matName = i < mats.Length && mats[i] != null ? mats[i].name : "(ไม่มี material)";
            bool   isScreen = matName.ToLowerInvariant().Contains("screen");

            sb.AppendLine();
            sb.AppendLine($"── submesh {i} : {matName}{(isScreen ? "   ◄◄ หน้าจอ" : "")}");
            AppendSubmeshReport(sb, mesh, i, scale, isScreen, i < mats.Length ? mats[i] : null);
        }

        Debug.Log(sb.ToString(), go);
    }

    private static void AppendSubmeshReport(StringBuilder sb, Mesh mesh, int index, Vector3 scale,
                                            bool verbose, Material mat)
    {
        int[] tris = mesh.GetTriangles(index);
        if (tris.Length == 0) { sb.AppendLine("   (ว่าง)"); return; }

        var verts = mesh.vertices;
        var uvs   = mesh.uv;

        // หา vertex ที่ submesh นี้ใช้จริงเท่านั้น — ใช้ mesh.bounds ไม่ได้เพราะมันคือทั้งก้อน
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

        sb.AppendLine($"   vertex {used.Count} ตัว / triangle {tris.Length / 3} อัน");
        sb.AppendLine($"   ขนาดในโลก : X {world.x:0.####}   Y {world.y:0.####}   Z {world.z:0.####}");

        if (!verbose) return;

        // หน้าจอเป็นแผ่นแบน → แกนที่บางที่สุดคือแกนความหนา อีกสองแกนคือกว้าง×สูง
        int thin = world.x <= world.y && world.x <= world.z ? 0 : world.y <= world.z ? 1 : 2;
        int axA  = thin == 0 ? 1 : 0;
        int axB  = thin == 2 ? 1 : 2;
        int wAx  = world[axA] <= world[axB] ? axA : axB;      // แกนความกว้าง
        int hAx  = wAx == axA ? axB : axA;                    // แกนความสูง
        float faceW = world[wAx];
        float faceH = world[hAx];
        float aspect = faceH > 0f ? faceW / faceH : 0f;

        sb.AppendLine($"   หน้าจอจริง : กว้าง {faceW:0.####}  สูง {faceH:0.####}  → อัตราส่วน {aspect:0.0000}");

        if (!hasUv) { sb.AppendLine("   ⚠ mesh ไม่มี UV — ต้องไปแก้ที่โมเดล หรือใช้ Quad ลูกแทน"); return; }

        Vector2 uvSize = umax - umin;
        sb.AppendLine($"   ช่วง UV    : u {umin.x:0.####}..{umax.x:0.####}   v {umin.y:0.####}..{umax.y:0.####}" +
                      $"   (กว้าง {uvSize.x:0.####} × สูง {uvSize.y:0.####})");

        if (uvSize.x <= 0f || uvSize.y <= 0f)
        {
            sb.AppendLine("   ⚠ UV แบน — หน้าจอไม่ได้กาง UV ไว้ ใช้ RenderTexture กับ submesh นี้ตรงๆ ไม่ได้");
            return;
        }

        // ให้ RT เต็มพื้นที่ UV ของหน้าจอพอดี:  uv*tiling + offset  ต้องส่ง umin→0 และ umax→1
        Vector2 tiling = new Vector2(1f / uvSize.x, 1f / uvSize.y);
        Vector2 offset = new Vector2(-umin.x / uvSize.x, -umin.y / uvSize.y);

        int rtW = Mathf.RoundToInt(ReferenceHeight * aspect / 2f) * 2;   // ให้เป็นเลขคู่

        AppendLinearityReport(sb, verts, uvs, used, wAx, hAx, uvSize, rtW);

        sb.AppendLine();
        sb.AppendLine("   ─── ค่าที่ควรตั้ง ───");
        sb.AppendLine($"   Screen.mat  Tiling = ({tiling.x:0.####}, {tiling.y:0.####})   Offset = ({offset.x:0.####}, {offset.y:0.####})");
        sb.AppendLine($"   RenderTexture ขนาด = {rtW} × {ReferenceHeight}");
        sb.AppendLine($"   Canvas Scaler Reference Resolution = {rtW} × {ReferenceHeight}  (ต้องเท่ากับ RT เป๊ะๆ)");

        if (mat == null) return;

        Vector2 curT = mat.HasProperty("_BaseMap") ? mat.GetTextureScale("_BaseMap") : Vector2.one;
        Vector2 curO = mat.HasProperty("_BaseMap") ? mat.GetTextureOffset("_BaseMap") : Vector2.zero;
        sb.AppendLine($"   ตอนนี้ตั้งอยู่   Tiling = ({curT.x:0.####}, {curT.y:0.####})   Offset = ({curO.x:0.####}, {curO.y:0.####})" +
                      (Approximately(curT.x, tiling.x) && Approximately(curT.y, tiling.y) &&
                       Approximately(curO.x, offset.x) && Approximately(curO.y, offset.y)
                           ? "   ✓ ตรงแล้ว"
                           : "   ✗ ไม่ตรง"));
    }

    /// <summary>
    /// เช็กว่า UV ของหน้าจอ "ตรง" หรือ "เบี้ยว"
    ///
    /// หน้าจอแบนที่กาง UV ถูกต้อง การ map จากตำแหน่งบนหน้าจอไปเป็น UV ต้องเป็นสมการเชิงเส้น
    /// อันเดียวใช้ได้ทั้งแผ่น:  u = a·x + b·y + c
    ///
    /// ถ้าไม่ใช่ — เช่น UV เป็นสี่เหลี่ยมคางหมู หรือแต่ละสามเหลี่ยมกาง UV คนละแบบ — texture จะ
    /// หักมุมตรงรอยต่อสามเหลี่ยม ฝั่งหนึ่งถูกบีบอีกฝั่งถูกยืด (อาการแบบเกม PS1) ซึ่งแก้ด้วย
    /// Tiling/Offset ไม่ได้เลย ต้องไปแก้ที่โมเดลหรือใช้ Quad แปะทับแทน
    ///
    /// วิธีเช็ก: fit สมการเชิงเส้นด้วย least squares จาก vertex ทุกตัว แล้ววัดว่าตัวที่หลุด
    /// ออกจากสมการมากที่สุด หลุดไปกี่พิกเซลบนจอจริง
    /// </summary>
    private static void AppendLinearityReport(StringBuilder sb, Vector3[] verts, Vector2[] uvs,
                                              HashSet<int> used, int wAx, int hAx, Vector2 uvSize, int rtW)
    {
        if (used.Count < 4) return;

        // normal equations ของ [p q 1] สำหรับทั้ง u และ v พร้อมกัน
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
            sb.AppendLine("   (เช็กความตรงของ UV ไม่ได้ — vertex เรียงกันเป็นเส้นตรง)");
            return;
        }

        double worstU = 0, worstV = 0;
        foreach (int i in used)
        {
            double p = verts[i][wAx], q = verts[i][hAx];
            worstU = System.Math.Max(worstU, System.Math.Abs(uvs[i].x - (cu[0] * p + cu[1] * q + cu[2])));
            worstV = System.Math.Max(worstV, System.Math.Abs(uvs[i].y - (cv[0] * p + cv[1] * q + cv[2])));
        }

        // แปลงค่าที่หลุดเป็นพิกเซลบน RenderTexture จะได้รู้ว่าตาเห็นไหม
        double pxU = uvSize.x > 0f ? worstU / uvSize.x * rtW : 0;
        double pxV = uvSize.y > 0f ? worstV / uvSize.y * ReferenceHeight : 0;
        double px  = System.Math.Max(pxU, pxV);

        sb.AppendLine();
        sb.AppendLine($"   ความตรงของ UV : เบี้ยวจากระนาบสูงสุด {px:0.0} พิกเซล  " +
                      (px < 1.0 ? "✓ ตรงสนิท ภาพไม่บิดจาก UV แน่นอน"
                     : px < 8.0 ? "~ เบี้ยวนิดหน่อย ตาแทบไม่เห็น"
                                : "✗ เบี้ยวจริง — Tiling/Offset แก้ไม่ได้ ต้องใช้ Quad แปะทับ"));
    }

    /// <summary>แก้สมการ 3×3 ด้วย Gaussian elimination + partial pivot</summary>
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
