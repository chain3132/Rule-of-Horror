using UnityEngine;

/// <summary>
/// หน้าตาศาลาของโหมดหนึ่ง (Relax / Tension) — GameModeController สลับให้ตอนเปลี่ยนโหมด
/// </summary>
[System.Serializable]
public class SaraModel
{
    [Tooltip("Mesh ของศาลาในโหมดนี้ — ใช้ทั้งแสดงผลและ Mesh Collider")]
    public Mesh saraMeshFilter;

    [Tooltip("วัสดุเรียงตามช่องของโมเดล\n" +
             "Element 0 = Zinc (หลังคาสังกะสี ด้านบน)\n" +
             "Element 1 = Wood (ไม้ ด้านล่าง)")]
    public Material[] materials = new Material[2];

    // แบบเก่า: วัสดุเดียวใส่ทุกช่อง — เก็บไว้ใช้แทนช่องที่ยังว่างใน Materials ศาลาจะได้ไม่เป็นสีชมพู
    [HideInInspector] public Material saraMeshRenderer;
}
