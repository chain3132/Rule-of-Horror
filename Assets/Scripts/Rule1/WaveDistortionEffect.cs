using UnityEngine;

/// <summary>
/// ควบคุม WaveDistortion shader ที่ใช้กับ FullScreenPassRendererFeature
///
/// Setup (ทำครั้งเดียวใน Unity Editor):
///   1. คลิกขวาที่ Assets/Shaders/WaveDistortion.shader → Create → Material
///      ตั้งชื่อว่า "WaveDistortionMat"
///   2. เปิด Edit → Project Settings → Graphics → URP Global Settings
///      หรือ Assets/Settings/PC_Renderer.asset → Add Renderer Feature
///      → Full Screen Pass Renderer Feature
///      → ตั้ง Pass Material = WaveDistortionMat
///      → Injection Point = Before Rendering Post Processing
///   3. ติด WaveDistortionEffect นี้บน GameObject ใดก็ได้ในฉาก
///      แล้ว Assign "Wave Material" = WaveDistortionMat
///   4. ลาก WaveDistortionEffect component ไปใส่ Rule1 → waveEffect field
/// </summary>
public class WaveDistortionEffect : MonoBehaviour
{
    [Tooltip("Material ที่สร้างจาก WaveDistortion.shader")]
    [SerializeField] private Material waveMaterial;

    // ── Orange Zone ───────────────────────────────────────────────────
    [Header("Orange Zone Wave")]
    [Tooltip("ความกว้างการแกว่ง (UV 0–1)\n" +
             "0.012 ≈ หน้าจอ 1920px กว้างประมาณ 23px")]
    [SerializeField] private float orangeAmp   = 0.012f;
    [Tooltip("จำนวนคลื่นที่มองเห็นตลอดหน้าจอ (รอบ)  แนะนำ 4–7")]
    [SerializeField] private float orangeFreq  = 5f;
    [Tooltip("ความเร็วคลื่น (รอบ/วินาที)  แนะนำ 0.8–1.5")]
    [SerializeField] private float orangeSpeed = 1.2f;

    // ── Red Zone ──────────────────────────────────────────────────────
    [Header("Red Zone Wave (ต้องรุนแรงกว่า Orange)")]
    [Tooltip("ความกว้างการแกว่ง  ควรมากกว่า Orange\n" +
             "0.028 ≈ หน้าจอ 1920px กว้างประมาณ 54px")]
    [SerializeField] private float redAmp   = 0.028f;
    [Tooltip("จำนวนคลื่น  แนะนำ 7–12")]
    [SerializeField] private float redFreq  = 9f;
    [Tooltip("ความเร็วคลื่น  แนะนำ 2–4")]
    [SerializeField] private float redSpeed = 3.0f;

    // ── Shader Property IDs ───────────────────────────────────────────
    private static readonly int AmpID   = Shader.PropertyToID("_WaveAmp");
    private static readonly int FreqID  = Shader.PropertyToID("_WaveFreq");
    private static readonly int SpeedID = Shader.PropertyToID("_WaveSpeed");

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Awake()  => SetOff();   // ปิดตั้งแต่แรก
    private void OnDestroy() => SetOff(); // เคลียร์ตอน unload

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>เปิด wave แบบ Orange (แกว่งช้า นิ่มนวล)</summary>
    public void SetOrange()
    {
        Apply(orangeAmp, orangeFreq, orangeSpeed);
    }

    /// <summary>เปิด wave แบบ Red (แกว่งเร็ว รุนแรง)</summary>
    public void SetRed()
    {
        Apply(redAmp, redFreq, redSpeed);
    }

    /// <summary>ปิด wave effect (amp = 0)</summary>
    public void SetOff()
    {
        if (waveMaterial == null) return;
        waveMaterial.SetFloat(AmpID, 0f);
    }

    // ── Private ───────────────────────────────────────────────────────

    private void Apply(float amp, float freq, float speed)
    {
        if (waveMaterial == null)
        {
            Debug.LogWarning("[WaveDistortionEffect] waveMaterial ยังไม่ได้ assign!", this);
            return;
        }
        waveMaterial.SetFloat(AmpID,   amp);
        waveMaterial.SetFloat(FreqID,  freq);
        waveMaterial.SetFloat(SpeedID, speed);
    }
}
