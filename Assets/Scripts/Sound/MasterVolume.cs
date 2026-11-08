using System;
using UnityEngine;

/// <summary>
/// ระดับเสียงรวมทั้งเกม (0..1) — จำไว้ใน PlayerPrefs ข้ามการเปิดเกม
///
/// คุม FMOD Master Bus ("bus:/") ซึ่งทุกเสียงในเกมไหลผ่าน และ AudioListener.volume
/// เผื่อเสียงของ Unity เอง (เช่น VideoPlayer) — ตั้งครั้งเดียวมีผลทุกซีน ไม่ต้องมีคอมโพเนนต์ในซีน
///
/// ค่าในตัวแปรคือค่าของแถบ (ที่ผู้เล่นเห็น) ส่วนที่ส่งให้ FMOD ยกกำลังสองก่อน —
/// หูคนได้ยินความดังแบบไม่เป็นเส้นตรง ถ้าส่งตรงๆ ครึ่งแรกของแถบแทบไม่ต่าง แล้วไปเบาลงฮวบช่วงท้าย
/// </summary>
public static class MasterVolume
{
    private const string PrefKey = "MasterVolume";
    public  const float  Default = 1f;

    /// <summary>ค่าของแถบ 0..1</summary>
    public static float Value { get; private set; } = Default;

    /// <summary>เรียกเมื่อค่าเปลี่ยน — ให้แถบเสียงหลายอันในฉาก (ถ้ามี) ขยับตามกัน</summary>
    public static event Action<float> Changed;

    private static bool _applied;   // ตั้งให้ FMOD สำเร็จแล้วหรือยัง (bank ยังไม่โหลด = ยัง)

    // ไม่มี domain reload — ล้าง event ที่ค้างจากรอบ Play ก่อน
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic()
    {
        Changed  = null;
        _applied = false;
        Value    = Default;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Load()
    {
        Value = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKey, Default));
        Apply();
    }

    /// <summary>ตั้งระดับเสียง — save = false ตอนลากแถบอยู่ (เขียนดิสก์ครั้งเดียวตอนปิดเมนูด้วย Save)</summary>
    public static void Set(float value, bool save = true)
    {
        value = Mathf.Clamp01(value);
        Value = value;
        Apply();

        PlayerPrefs.SetFloat(PrefKey, value);
        if (save) PlayerPrefs.Save();

        Changed?.Invoke(value);
    }

    public static void Save() => PlayerPrefs.Save();

    /// <summary>ตั้งซ้ำถ้ารอบก่อนตั้งไม่สำเร็จ (FMOD ยังไม่พร้อมตอนเริ่มเกม)</summary>
    public static void EnsureApplied()
    {
        if (!_applied) Apply();
    }

    private static void Apply()
    {
        float gain = Value * Value;
        AudioListener.volume = gain;

        try
        {
            var bus = FMODUnity.RuntimeManager.GetBus("bus:/");
            _applied = bus.setVolume(gain) == FMOD.RESULT.OK;
        }
        catch (Exception e)
        {
            _applied = false;
            Debug.LogWarning("[MasterVolume] ตั้งเสียง FMOD ไม่ได้ (bank ยังไม่โหลด?) — จะลองใหม่ตอนเปิดเมนู\n" + e.Message);
        }
    }
}
