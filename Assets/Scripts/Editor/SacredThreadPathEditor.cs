using Rule5;
using UnityEditor;
using UnityEngine;

/// <summary>
/// เครื่องมือช่วยวางสายสิญจน์ใน Scene view
///   - ลาก handle ที่จุดแต่ละจุดได้ตรงๆ โดยไม่ต้องคลิกเลือกลูกทีละตัว (มี Undo)
///   - ป้ายเลขบอกลำดับจุด + ระยะสะสม
///   - ปุ่มใน Inspector: เพิ่มจุดต่อท้าย / แทรกจุดกึ่งกลางทุกช่วง / วางทุกจุดลงพื้น
/// </summary>
[CustomEditor(typeof(SacredThreadPath))]
public class SacredThreadPathEditor : Editor
{
    private const float NewPointSpacing = 2f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var path = (SacredThreadPath)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"จุดทั้งหมด {path.WaypointCount}  •  ยาว {path.TotalLength:0.0} ม.", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("จุดหักของสาย = GameObject ลูก เรียงตามลำดับใน Hierarchy\n" +
                                "ลากจุดใน Scene ได้เลย เส้นจะตามเอง — ย้ายทั้งเส้นให้ย้ายตัวแม่", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("＋ เพิ่มจุดต่อท้าย")) AddPointAtEnd(path);
            if (GUILayout.Button("แทรกจุดกึ่งกลางทุกช่วง")) Subdivide(path);
        }
        if (GUILayout.Button("วางทุกจุดลงพื้น (Raycast ลง)")) SnapAllToGround(path);
    }

    private void OnSceneGUI()
    {
        var path = (SacredThreadPath)target;
        Transform root = path.transform;

        float cumulative = 0f;
        Vector3 prev = Vector3.zero;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform p = root.GetChild(i);

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(p.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(p, "Move Thread Point");
                p.position = newPos;
                path.Rebuild();
            }

            if (i > 0) cumulative += Vector3.Distance(prev, p.position);
            prev = p.position;

            Handles.Label(p.position + Vector3.up * 0.3f, $"P{i}  ({cumulative:0.0} ม.)", EditorStyles.whiteBoldLabel);
        }
    }

    private static void AddPointAtEnd(SacredThreadPath path)
    {
        Transform root = path.transform;
        int n = root.childCount;

        Vector3 pos = root.position;
        if (n >= 2)
        {
            Vector3 a = root.GetChild(n - 2).position, b = root.GetChild(n - 1).position;
            pos = b + (b - a).normalized * NewPointSpacing;
        }
        else if (n == 1) pos = root.GetChild(0).position + root.forward * NewPointSpacing;

        var go = new GameObject($"P{n}");
        Undo.RegisterCreatedObjectUndo(go, "Add Thread Point");
        go.transform.SetParent(root, true);
        go.transform.position = pos;
        Selection.activeGameObject = go;
        path.Rebuild();
    }

    private static void Subdivide(SacredThreadPath path)
    {
        Transform root = path.transform;
        int n = root.childCount;
        if (n < 2) return;

        int segs = path.Loop ? n : n - 1;
        var mids = new Vector3[segs];
        for (int i = 0; i < segs; i++)
            mids[i] = (root.GetChild(i).position + root.GetChild((i + 1) % n).position) * 0.5f;

        // แทรกจากหลังมาหน้า sibling index จะได้ไม่เลื่อน
        for (int i = segs - 1; i >= 0; i--)
        {
            var go = new GameObject("P");
            Undo.RegisterCreatedObjectUndo(go, "Subdivide Thread");
            go.transform.SetParent(root, true);
            go.transform.position = mids[i];
            go.transform.SetSiblingIndex(i + 1);
        }
        RenameAll(root);
        path.Rebuild();
    }

    private static void SnapAllToGround(SacredThreadPath path)
    {
        foreach (Transform p in path.transform)
        {
            if (!Physics.Raycast(p.position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f)) continue;
            Undo.RecordObject(p, "Snap Thread Point");
            p.position = hit.point;
        }
        path.Rebuild();
    }

    private static void RenameAll(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var t = root.GetChild(i);
            Undo.RecordObject(t.gameObject, "Rename Thread Point");
            t.name = $"P{i}";
        }
    }
}
