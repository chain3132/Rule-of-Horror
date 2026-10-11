using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ผลของรางวัลแบบ "เปิดของในฉากคนละชุดตามโหมด" — ตอนนี้ใช้กับหิ่งห้อย
///
///   ยังไม่ได้เลือก     : ปิดทั้งสองชุด
///   เลือกแล้ว + Relax  : เปิดชุด Relax (หิ่งห้อยสีเหลือง) ปิดชุด Tension
///   เลือกแล้ว + Tension: ปิดชุด Relax เปิดชุด Tension (หิ่งห้อยสีแดง)
///
/// ใช้ซ้ำกับวิทยุ/ธูปได้ ถ้าผลของมันคือการสลับของในฉากเหมือนกัน — ตั้ง Kind ให้ตรงก็พอ
///
/// วิธีวาง: แปะตัวนี้บน "ตัวแม่" ที่เปิดอยู่ตลอด แล้วลากลูกใส่สองช่องข้างล่าง
/// และปล่อยช่อง Objects To Enable ของรางวัลนี้ใน RewardSystem ว่างไว้ ตัวนี้จัดการให้หมดแล้ว
/// </summary>
public class RewardModeObjects : RewardEffect
{
    [Header("ของในฉาก")]
    [Tooltip("เปิดตอน Relax เช่นหิ่งห้อยสีเหลือง")]
    [SerializeField] private List<GameObject> relaxObjects = new List<GameObject>();

    [Tooltip("เปิดตอน Tension เช่นหิ่งห้อยสีแดง")]
    [SerializeField] private List<GameObject> tensionObjects = new List<GameObject>();

    protected override void Apply(bool owned, GameMode mode)
    {
        SetAll(relaxObjects,   owned && mode == GameMode.Relax);
        SetAll(tensionObjects, owned && mode == GameMode.Tension);
    }

    private void SetAll(List<GameObject> objects, bool on)
    {
        if (objects == null) return;

        foreach (var obj in objects)
        {
            if (obj == null) continue;

            // ปิดตัวเองแล้ว OnEnable/Refresh จะไม่วิ่งอีก เปิดกลับมาเองไม่ได้ตลอดเกม
            if (obj == gameObject)
            {
                Debug.LogWarning($"[Reward] '{name}' มีตัวเองอยู่ในลิสต์ — ข้ามให้ " +
                                 "ย้าย RewardModeObjects ไปไว้บนตัวแม่ที่เปิดอยู่ตลอดแทน", this);
                continue;
            }

            if (obj.activeSelf != on) obj.SetActive(on);
        }
    }
}
