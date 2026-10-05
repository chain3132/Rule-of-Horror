using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// วาง Quad แสดง RenderTexture ทับหน้าจอโทรศัพท์ แทนการฉายลง submesh ของโมเดล
///
/// ทำไมต้องมี: submesh หน้าจอของ Z Flip 3 มี 76 triangle และ UV ของมันไม่ได้อยู่บนระนาบเดียว
/// (ScreenSubmeshAnalyzer วัดได้ว่าเบี้ยวจากระนาบ ~25px) แต่ละสามเหลี่ยม map texture คนละ affine
/// ภาพเลยหักมุมตรงรอยต่อ ฝั่งหนึ่งถูกบีบอีกฝั่งถูกยืด — Tiling/Offset แก้ไม่ได้เพราะมันไปคูณ
/// UV ที่เบี้ยวอยู่แล้ว ต้องเปลี่ยนพื้นผิวที่ฉายลงไปเลย
///
/// Quad ที่สร้างตรงนี้มี 2 triangle, UV 0..1 เป๊ะ, แบนสนิท → ไม่มีทางบิด
/// และเพราะเราคุมขนาดเองได้ สัดส่วนจึงตรงกับ RenderTexture เสมอ
///
/// ขนาดคำนวณจาก world size ของ submesh จริง เพราะฉะนั้น scale (50, 40, 50) ที่ไม่เท่ากันทุกแกน
/// ของตัวโทรศัพท์ถูกคิดรวมไปแล้ว ไม่ต้องไปแก้ scale
/// </summary>
public static class PhoneScreenQuadBuilder
{
    private const string QuadName     = "ScreenQuad";
    private const string FolderPath   = "Assets/Prefab/LightPole/Phone";
    private const string MeshPath     = FolderPath + "/PhoneScreenQuad.mesh";
    private const string MaterialPath = FolderPath + "/PhoneScreenUnlit.mat";
    private const string RenderTexPath = "Assets/Texture/Phone/PhoneRenderTexture.renderTexture";

    // ดัน Quad ออกมาหน้าหน้าจอกี่เมตร — กันซ้อนกับผิวเดิมจนกระพริบ (z-fighting)
    private const float SurfaceOffset = 0.0005f;

    [MenuItem("Rule of Horror/Phone/สร้าง Quad หน้าจอ (แก้ภาพบิดจาก UV) %#q", false, 61)]
    private static void Build()
    {
        if (!ScreenSubmeshAnalyzer.TryMeasureScreen(Selection.activeGameObject, out var face)) return;

        var parent = face.renderer.transform;
        var quad   = FindQuad(parent);

        if (quad == null)
        {
            var go = new GameObject(QuadName, typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(go, "สร้าง Quad หน้าจอ");
            go.transform.SetParent(parent, false);
            quad = go.transform;
        }
        else
        {
            Undo.RecordObject(quad.gameObject, "อัปเดต Quad หน้าจอ");
            Undo.RecordObject(quad, "อัปเดต Quad หน้าจอ");
        }

        quad.gameObject.layer = face.renderer.gameObject.layer;   // อย่าให้ไปอยู่ layer UI ที่ PhoneCamera ถ่าย

        // แกนขึ้นของ Quad = แกนด้านยาวของหน้าจอ / แกนหน้า = ทิศที่หน้าจอหันออก
        Vector3 up = Vector3.zero;
        up[face.heightAxis] = 1f;

        quad.localRotation = Quaternion.LookRotation(face.localNormal, up);
        quad.localScale    = new Vector3(face.localSize[face.widthAxis], face.localSize[face.heightAxis], 1f);

        // ระยะดันออกคิดเป็นเมตรจริง แล้วค่อยหารด้วย scale กลับมาเป็นหน่วย local
        float scaleOnNormal = Mathf.Abs(face.renderer.transform.lossyScale[face.normalAxis]);
        float pushLocal     = scaleOnNormal > 0.0001f ? SurfaceOffset / scaleOnNormal : SurfaceOffset;
        quad.localPosition  = face.localCenter + face.localNormal * pushLocal;

        var filter = quad.GetComponent<MeshFilter>();
        var render = quad.GetComponent<MeshRenderer>();
        filter.sharedMesh      = LoadOrCreateMesh();
        render.sharedMaterial  = LoadOrCreateMaterial();
        render.shadowCastingMode = ShadowCastingMode.Off;
        render.receiveShadows    = false;
        render.lightProbeUsage   = LightProbeUsage.Off;

        EditorUtility.SetDirty(quad.gameObject);
        Selection.activeGameObject = quad.gameObject;

        int rtW = face.SuggestedRtWidth;
        Debug.Log($"[ScreenQuad] วาง Quad เรียบร้อย — ขนาดจริง {face.worldSize[face.widthAxis]:0.####} × " +
                  $"{face.worldSize[face.heightAxis]:0.####} ม. (อัตราส่วน {face.Aspect:0.0000})\n" +
                  $"  • ตั้ง PhoneRenderTexture เป็น {rtW} × {ScreenSubmeshAnalyzer.ReferenceHeight} ให้ตรงสัดส่วนด้วย\n" +
                  $"  • เอา RenderTexture ออกจาก Screen.mat ได้แล้ว (ตั้ง Base Map เป็น None สีดำ) " +
                  $"ไม่งั้นจะมีภาพซ้อนอยู่ข้างหลัง Quad\n" +
                  $"  • ถ้าภาพตะแคงหรือกลับด้าน ใช้เมนู หมุน 90° / พลิกซ้าย-ขวา ได้เลย", quad.gameObject);
    }

    [MenuItem("Rule of Horror/Phone/Quad หน้าจอ — ดันออกมาหน้ากระจก", false, 62)]
    private static void PushInFrontOfGlass()
    {
        var root = Selection.activeGameObject;
        if (root != null && root.name == QuadName && root.transform.parent != null)
            root = root.transform.parent.gameObject;

        if (!ScreenSubmeshAnalyzer.TryMeasureScreen(root, out var face)) return;

        var quad = FindQuad(face.renderer.transform);
        if (quad == null)
        {
            Debug.LogError("[ScreenQuad] ยังไม่มี ScreenQuad — สร้างก่อนด้วยเมนูสร้าง Quad หน้าจอ", root);
            return;
        }

        var filter = face.renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;

        Mesh mesh  = filter.sharedMesh;
        var  mats  = face.renderer.sharedMaterials;
        var  verts = mesh.vertices;

        int   nAx  = face.normalAxis;
        float sign = face.localNormal[nAx];                    // +1 หรือ -1
        float half = 0.5f;

        // ขอบเขตของหน้าจอในระนาบ ใช้คัดเฉพาะ vertex ที่อยู่ "เหนือจอ" จริงๆ
        // ไม่งั้นขอบเครื่องหรือปุ่มกล้องที่ยื่นออกมาจะลากให้ Quad ลอยออกไปไกลเกินจำเป็น
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
            Debug.Log("[ScreenQuad] ไม่มี submesh ไหนคลุมหน้าจออยู่ — ตัวที่บังตัวอักษรไม่ได้มาจากโมเดลโทรศัพท์ " +
                      "ลองดู post-processing (Bloom / Tonemapping) หรือวัตถุอื่นในซีนแทน", quad);
            return;
        }

        float scaleOnNormal = Mathf.Abs(face.renderer.transform.lossyScale[nAx]);
        float clearance     = scaleOnNormal > 0.0001f ? SurfaceOffset / scaleOnNormal : SurfaceOffset;

        Undo.RecordObject(quad, "ดัน Quad หน้าจอ");

        Vector3 pos = quad.localPosition;
        float   old = pos[nAx];
        pos[nAx]    = (best + clearance) * sign;
        quad.localPosition = pos;
        EditorUtility.SetDirty(quad.gameObject);

        string blockerName = blockerMesh < mats.Length && mats[blockerMesh] != null
                           ? mats[blockerMesh].name : $"submesh {blockerMesh}";
        float  movedMm     = Mathf.Abs(pos[nAx] - old) * scaleOnNormal * 1000f;

        Debug.Log($"[ScreenQuad] ตัวที่คลุมหน้าจออยู่คือ '{blockerName}' — ดัน Quad ออกมา {movedMm:0.##} มม. " +
                  "ให้อยู่หน้ามันแล้ว\n" +
                  "ถ้ายังเห็นแสงสะท้อนอยู่ แปลว่ามาจากที่อื่น ไม่ใช่จากโมเดลโทรศัพท์", quad);
    }

    [MenuItem("Rule of Horror/Phone/Quad หน้าจอ — หมุน 90°", false, 63)]
    private static void Rotate90()
    {
        var quad = RequireQuad();
        if (quad == null) return;

        Undo.RecordObject(quad, "หมุน Quad หน้าจอ");
        quad.localRotation *= Quaternion.Euler(0f, 0f, 90f);

        // หมุนแล้วด้านกว้างกับด้านสูงสลับแกนกัน ต้องสลับ scale ตามไม่งั้นรูปทรงเพี้ยน
        var s = quad.localScale;
        quad.localScale = new Vector3(s.y, s.x, s.z);
    }

    [MenuItem("Rule of Horror/Phone/Quad หน้าจอ — พลิกซ้าย-ขวา", false, 64)]
    private static void FlipHorizontal()
    {
        var quad = RequireQuad();
        if (quad == null) return;

        Undo.RecordObject(quad, "พลิก Quad หน้าจอ");
        var s = quad.localScale;
        quad.localScale = new Vector3(-s.x, s.y, s.z);
    }

    private static Transform RequireQuad()
    {
        var sel = Selection.activeGameObject;
        if (sel == null) { Debug.LogError("[ScreenQuad] เลือก ScreenQuad (หรือตัวโทรศัพท์) ก่อน"); return null; }

        var quad = sel.name == QuadName ? sel.transform : FindQuad(sel.transform);
        if (quad == null) Debug.LogError("[ScreenQuad] หา ScreenQuad ไม่เจอ — สร้างก่อนด้วยเมนูสร้าง Quad หน้าจอ", sel);
        return quad;
    }

    private static Transform FindQuad(Transform parent)
    {
        foreach (Transform c in parent) if (c.name == QuadName) return c;
        return null;
    }

    /// <summary>
    /// Quad ที่ปั้นเอง ไม่ใช้ primitive ของ Unity เพราะทิศที่มันหันไม่ตรงกันในแต่ละเวอร์ชัน
    /// ตัวนี้ normal = +Z, UV เต็ม 0..1, winding ตามกติกา Unity (front face = ตามเข็มเมื่อมองจากด้านหน้า)
    /// </summary>
    private static Mesh LoadOrCreateMesh()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing != null) return existing;

        var mesh = new Mesh
        {
            name     = "PhoneScreenQuad",
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),   // 0 ล่างซ้าย
                new Vector3( 0.5f, -0.5f, 0f),   // 1 ล่างขวา
                new Vector3(-0.5f,  0.5f, 0f),   // 2 บนซ้าย
                new Vector3( 0.5f,  0.5f, 0f),   // 3 บนขวา
            },
            uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
            normals  = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
            triangles = new[] { 2, 3, 1, 2, 1, 0 },
        };
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        EnsureFolder();
        AssetDatabase.CreateAsset(mesh, MeshPath);
        return mesh;
    }

    private static Material LoadOrCreateMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;

        // Unlit เพราะหน้าจอโทรศัพท์เปล่งแสงเอง ไม่ควรถูกไฟในซีนหรี่ลงตอนกลางคืน
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[ScreenQuad] หา URP/Unlit ไม่เจอ ใช้ Unlit/Texture แทนไปก่อน");
            shader = Shader.Find("Unlit/Texture");
        }

        var mat = new Material(shader) { name = "PhoneScreenUnlit" };
        var rt  = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexPath);
        if (rt == null)
            Debug.LogWarning($"[ScreenQuad] หา RenderTexture ที่ {RenderTexPath} ไม่เจอ — ต้องลากใส่ material เอง");
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
