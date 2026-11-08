using UnityEngine;

/// <summary>
/// เทียน 1 เล่ม — คุมแสง (Light) กับเปลวไฟ (Particle Candle_Flame) ให้ติด/ดับพร้อมกัน
/// ใส่ไว้ที่ตัวราก Candle_Model — ถ้าไม่ได้ลากช่องไว้ จะหา Light / ParticleSystem ในลูกเอง
/// ตอนติด แสงจะค่อยๆ สว่างขึ้นจาก 0 จนเต็มใน fadeInSeconds วินาที
/// </summary>
public class CandleLight : MonoBehaviour
{
    [Tooltip("แสงเทียน — ว่างไว้จะหาในลูกเอง (Candle_Flame/Light)")]
    [SerializeField] private Light candleLight;
    [Tooltip("เปลวไฟ Particle — ว่างไว้จะหาในลูกเอง (Candle_Flame)")]
    [SerializeField] private ParticleSystem flame;

    [Header("ค่อยๆ สว่าง")]
    [Tooltip("วินาทีที่แสงไล่จากมืดจนสว่างเต็ม (0 = ติดทันที)")]
    [SerializeField] private float fadeInSeconds = 1.5f;

    private bool isLit = false;

    private bool  _ready;
    private bool  _visible;        // ตอนนี้กำลังแสดงแสงอยู่ไหม (ติด และไม่ได้ถูกซ่อน)
    private float _fullIntensity;  // ความสว่างเต็มที่ตั้งไว้บน Light
    private float _fade = 1f;      // 0..1 ความคืบหน้าของการค่อยๆ สว่าง

    private void Awake() => Init();

    // เรียกได้ก่อน Awake (เช่น GameModeController สั่งจุดตอนวัตถุยังปิดอยู่)
    private void Init()
    {
        if (_ready) return;
        _ready = true;

        if (candleLight == null) candleLight = GetComponentInChildren<Light>(true);
        if (flame == null)       flame       = GetComponentInChildren<ParticleSystem>(true);

        if (candleLight != null)
        {
            _fullIntensity = candleLight.intensity;
            candleLight.enabled = false;
        }

        if (flame != null)
        {
            // ปิด Play On Awake — ไม่งั้นเปิด GameObject ใหม่ทีไรเปลวไฟโผล่เองทั้งที่เทียนยังไม่ติด
            var main = flame.main;
            main.playOnAwake = false;
            flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void Update()
    {
        if (!_visible || _fade >= 1f || candleLight == null) return;

        _fade = fadeInSeconds > 0f ? Mathf.MoveTowards(_fade, 1f, Time.deltaTime / fadeInSeconds) : 1f;
        candleLight.intensity = _fullIntensity * Mathf.SmoothStep(0f, 1f, _fade);
    }

    public void LightCandle()
    {
        if (isLit) return;

        isLit = true;
        SetVisible(true);
    }

    public bool IsLit()
    {
        return isLit;
    }

    /// <summary>ดับและล้างสถานะว่าเคยจุด — ต่างจาก HideLight ที่ยังจำว่าติดอยู่ (เครื่องมือทดสอบ)</summary>
    public void PutOutCandle()
    {
        isLit = false;
        SetVisible(false);
    }

    /// <summary>ซ่อนแสงชั่วคราว (isLit ยังคงอยู่) — ใช้ตอนเข้า Tension mode</summary>
    public void HideLight()
    {
        SetVisible(false);
    }

    /// <summary>แสดงแสงคืน ถ้าเทียนติดอยู่ — ใช้ตอนเข้า Relax mode (ค่อยๆ สว่างขึ้นใหม่)</summary>
    public void ShowLight()
    {
        SetVisible(isLit);
    }

    private void SetVisible(bool show)
    {
        Init();
        if (_visible == show) return;
        _visible = show;

        if (candleLight != null)
        {
            // ติด = เริ่มจากมืดแล้วให้ Update ไล่ขึ้น / ดับ = ปิดทันที แล้วคืนค่าเต็มไว้ให้คนอื่นอ่าน
            _fade = show && fadeInSeconds > 0f ? 0f : 1f;
            candleLight.intensity = show ? _fullIntensity * _fade : _fullIntensity;
            candleLight.enabled = show;
        }

        if (flame != null)
        {
            if (show) flame.Play(true);
            else      flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
