using UnityEngine;

/// <summary>
/// สะพานระหว่าง WindZone ของ Unity กับ shader "Rule of Horror/TreeIt Wind"
///
/// WindZone ไม่ส่งค่าให้ shader ทั่วไปเอง (มีผลแค่ Tree Creator / SpeedTree / Particle)
/// script นี้อ่านค่าจาก WindZone ทุกเฟรมแล้วยิงเป็น global shader property:
///     _TreeItWindDir    = ทิศลม (world)  w = 1 เพื่อบอก shader ว่ามีคนตั้งค่า
///     _TreeItWindParams = (main, turbulence, pulseMagnitude, pulseFrequency)
///
/// วิธีใช้: ลากใส่ GameObject ที่มี WindZone (Directional) — มีตัวเดียวในฉากพอ
/// ปรับแรงลมจาก WindZone ตามปกติ ต้นไม้ทุกต้นที่ใช้ shader นี้จะตามเอง
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(WindZone))]
public class TreeItWindZone : MonoBehaviour
{
    private static readonly int WindDirId    = Shader.PropertyToID("_TreeItWindDir");
    private static readonly int WindParamsId = Shader.PropertyToID("_TreeItWindParams");

    [Tooltip("คูณเพิ่มจาก WindZone.main — ปรับตรงนี้ถ้าอยากให้แรงกว่าโดยไม่ยุ่ง particle ที่ใช้ WindZone เดียวกัน")]
    [SerializeField] private float strengthMultiplier = 1f;

    private WindZone _zone;

    private void OnEnable()
    {
        _zone = GetComponent<WindZone>();
        Push();
    }

    private void Update() => Push();

    private void OnDisable()
    {
        // ปิด script → บอก shader ให้กลับไปใช้ค่า default ของ material (w = 0)
        Shader.SetGlobalVector(WindDirId, new Vector4(1f, 0f, 0f, 0f));
    }

    private void Push()
    {
        if (_zone == null) return;

        // Spherical zone ไม่มีทิศ — ใช้ forward ของ transform ไปก่อน
        Vector3 dir = transform.forward;

        Shader.SetGlobalVector(WindDirId, new Vector4(dir.x, dir.y, dir.z, 1f));
        Shader.SetGlobalVector(WindParamsId, new Vector4(
            _zone.windMain * strengthMultiplier,
            _zone.windTurbulence,
            _zone.windPulseMagnitude,
            _zone.windPulseFrequency));
    }
}
