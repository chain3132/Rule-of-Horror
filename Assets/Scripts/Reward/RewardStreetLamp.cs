using System.Collections;
using Rule4;
using UnityEngine;

/// <summary>
/// ผลของรางวัล "เสาไฟ"
///
///   ยังไม่ได้เลือก     : หรี่ไว้ตาม Unclaimed Intensity Scale — พอเห็นทางแต่ไม่สว่าง
///   เลือกแล้ว + Relax  : สว่างเต็มตามค่าที่ตั้งไว้ใน Light เอง
///   เลือกแล้ว + Tension: ไฟสลับกันกระพริบไปตามเส้น ติดฝั่งนึง ดับฝั่งนึง แล้วสลับ
///                        เป็นพักๆ มีดับทั้งเส้นแทรก กลับ Relax แล้วนิ่งเองทันที
///
/// ทำไมสลับกันไม่ใช่สุ่มทุกดวง: สุ่มแยกดวงอ่านว่า "ไฟเสีย" ส่วนการสลับเป็นจังหวะอ่านว่า
/// "มีอะไรคุมอยู่" ซึ่งน่ากลัวกว่า และยังทำให้ทางเดินมืดสลับที่ไปเรื่อยๆ
/// ผู้เล่นจึงไม่เคยเห็นทั้งเส้นพร้อมกัน
///
/// ความสว่าง "เดิม" ถูกอ่านจาก Light ตอน Awake ครั้งเดียว เสาไฟแต่ละต้นจึงสว่างไม่เท่ากันได้
/// ตามที่จัดไฟไว้ในฉาก — อ่านทีหลังไม่ได้ เพราะพอหรี่หรือกระพริบไปแล้ว ค่าที่อ่านได้คือค่านั้น
/// </summary>
public class RewardStreetLamp : RewardEffect
{
    [Header("เสาไฟ")]
    [Tooltip("ไฟของเสาไฟทุกต้นที่รางวัลนี้คุม\n" +
             "ลำดับในลิสต์เป็นตัวแบ่งฝั่งที่สลับกัน — ใส่เรียงตามแนวถนนจะได้สลับต้นเว้นต้น")]
    [SerializeField] private Light[] lamps;

    [Header("ยังไม่ได้เลือก")]
    [Tooltip("ความสว่างตอนยังไม่ได้เลือก คิดเป็นสัดส่วนของค่าเดิม (0.25 = เหลือ 25%)")]
    [Range(0f, 1f)]
    [SerializeField] private float unclaimedIntensityScale = 0.25f;

    [Header("Tension — ไฟสลับกันกระพริบ")]
    [Tooltip("ความสว่างของดวงที่ 'ติด' ตอน Tension คิดเป็นสัดส่วนของค่าเดิม\n" +
             "ดันเกิน 1 ได้ถ้าอยากให้แยงตาตอนติด")]
    [SerializeField] private float tensionIntensityScale = 1f;

    [Tooltip("แบ่งเป็นกี่ฝั่งที่สลับกัน — 2 = ต้นเว้นต้น, 3 = ไล่เป็นคลื่นสามจังหวะ")]
    [Range(2, 5)]
    [SerializeField] private int groups = 2;

    [Tooltip("ค้างแต่ละฝั่งนานเท่าไรก่อนสลับ (สุ่ม min–max วินาที) — ยิ่งสั้นยิ่งรัว")]
    [SerializeField] private float swapMin = 0.18f;
    [SerializeField] private float swapMax = 0.55f;

    [Tooltip("ความสว่างของฝั่งที่ 'ดับ' คิดเป็นสัดส่วนของค่าเดิม\n" +
             "0 = ดับสนิท / ใส่ 0.05–0.1 ให้เหลือเรืองๆ พอเห็นว่าเสายังอยู่ที่เดิม")]
    [Range(0f, 1f)]
    [SerializeField] private float offIntensityScale;

    [Tooltip("โอกาสที่รอบนั้นจะดับทั้งเส้นแทนการสลับ (0–1) — ตัวที่ทำให้เดาจังหวะไม่ได้")]
    [Range(0f, 1f)]
    [SerializeField] private float blackoutChance = 0.18f;

    [Tooltip("ดับทั้งเส้นนานเท่าไร (สุ่ม min–max วินาที)")]
    [SerializeField] private float blackoutMin = 0.25f;
    [SerializeField] private float blackoutMax = 0.9f;

    [Header("ต่อกับไฟกระพริบของกฎ 4 (ไม่ใส่ก็ได้)")]
    [Tooltip("StreetLampFlicker ตัวที่คุมเสาไฟชุดเดียวกัน — ใส่ไว้แล้วสองระบบจะไม่แย่งกันเขียนค่า " +
             "และมันจะจำค่าความสว่างใหม่ได้ถ้าได้รางวัลกลางกฎ 4")]
    [SerializeField] private StreetLampFlicker flicker;

    // ─────────────────────────── Runtime ───────────────────────────

    private float[]   _baseIntensity;
    private bool[]    _baseEnabled;

    /// <summary>ความสว่างที่ควรค้างอยู่ตอนไม่ได้กระพริบ — คิดจาก owned + mode ปัจจุบัน</summary>
    private float[]   _steady;

    private Coroutine _flickerRoutine;

    private void Awake() => CacheOriginals();

    private void CacheOriginals()
    {
        int count = lamps != null ? lamps.Length : 0;

        _baseIntensity = new float[count];
        _baseEnabled   = new bool[count];
        _steady        = new float[count];

        for (int i = 0; i < count; i++)
        {
            if (lamps[i] == null) continue;

            _baseIntensity[i] = lamps[i].intensity;
            _baseEnabled[i]   = lamps[i].enabled;
            _steady[i]        = lamps[i].intensity;
        }
    }

    // ─────────────────────────── Apply ───────────────────────────

    protected override void Apply(bool owned, GameMode mode)
    {
        if (lamps == null || lamps.Length == 0) return;
        if (_baseIntensity == null || _baseIntensity.Length != lamps.Length) CacheOriginals();

        // หยุดกระพริบรอบก่อนและคืนไฟให้นิ่งก่อนเสมอ — Apply ถูกเรียกซ้ำได้ทั้งตอนสลับโหมด
        // ตอนเพิ่งได้รางวัล และตอน component เปิดใหม่ ถ้าไม่หยุดจะได้ coroutine ซ้อนกันหลายตัว
        // แย่งกันเขียน lamp.enabled แล้วกระพริบมั่วไม่เป็นจังหวะ
        StopFlicker();

        bool  tension = owned && mode == GameMode.Tension;
        float scale   = !owned  ? unclaimedIntensityScale
                      : tension ? tensionIntensityScale
                                : 1f;

        for (int i = 0; i < lamps.Length; i++)
        {
            var lamp = lamps[i];
            if (lamp == null) continue;

            _steady[i] = _baseIntensity[i] * Mathf.Max(0f, scale);

            // ถ้ากฎ 4 กำลังกระพริบไฟอยู่ มันคูณค่าที่ cache ไว้ตอน Play() — ไม่บอกค่าใหม่ให้มัน
            // พอกระพริบรอบหน้า RestoreLook จะดึงความสว่างกลับไปเป็นค่าก่อนได้รางวัล แล้วค้างจนจบกฎ
            if (flicker != null) flicker.SetBaseIntensity(lamp, _steady[i]);
        }

        RestoreSteady();

        if (tension && isActiveAndEnabled)
            _flickerRoutine = StartCoroutine(AlternateFlicker());
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        StopFlicker();   // ปิด component กลางกระพริบแล้วทิ้งไฟดับค้างไว้ไม่ได้
    }

    // ─────────────────────────── Flicker ───────────────────────────

    private IEnumerator AlternateFlicker()
    {
        int phase = Random.Range(0, groups);

        while (true)
        {
            // กฎ 4 กระพริบเสาไฟชุดเดียวกันอยู่ → ยกเวทีให้มันไปเลย
            //
            // ไฟกระพริบของกฎ 4 เป็นส่วนของการเล่น (บอกว่ากฎเริ่มแล้ว) ส่วนอันนี้เป็นบรรยากาศ
            // ถ้าเขียน lamp.enabled แย่งกันทั้งคู่ จังหวะของทั้งสองฝั่งจะพังพร้อมกัน
            if (flicker != null && flicker.IsRunning)
            {
                yield return null;
                continue;
            }

            if (Random.value < blackoutChance)
            {
                SetAllEnabled(false);
                yield return new WaitForSeconds(Random.Range(blackoutMin, blackoutMax));

                // กลับมาด้วยฝั่งสุ่ม ไม่ใช่ฝั่งถัดไป — ไม่งั้นนับจังหวะตามได้ว่าดับแล้วจะติดฝั่งไหน
                phase = Random.Range(0, groups);
                continue;
            }

            ApplyPhase(phase);
            yield return new WaitForSeconds(Random.Range(swapMin, Mathf.Max(swapMin, swapMax)));

            phase = (phase + 1) % groups;
        }
    }

    /// <summary>ติดเฉพาะดวงที่อยู่ในฝั่งนี้ ที่เหลือหรี่หรือดับตาม Off Intensity Scale</summary>
    private void ApplyPhase(int phase)
    {
        bool hardOff = offIntensityScale <= 0f;

        for (int i = 0; i < lamps.Length; i++)
        {
            var lamp = lamps[i];
            if (lamp == null) continue;

            bool lit = i % groups == phase;

            if (lit)
            {
                lamp.enabled   = _baseEnabled[i];
                lamp.intensity = _steady[i];
            }
            else if (hardOff)
            {
                lamp.enabled = false;
            }
            else
            {
                lamp.enabled   = _baseEnabled[i];
                lamp.intensity = _baseIntensity[i] * offIntensityScale;
            }
        }
    }

    private void StopFlicker()
    {
        if (_flickerRoutine == null) return;

        StopCoroutine(_flickerRoutine);
        _flickerRoutine = null;
        RestoreSteady();
    }

    /// <summary>คืนไฟเป็นค่าที่ควรค้างอยู่ของโหมดปัจจุบัน</summary>
    private void RestoreSteady()
    {
        if (_steady == null) return;

        for (int i = 0; i < lamps.Length && i < _steady.Length; i++)
        {
            if (lamps[i] == null) continue;

            lamps[i].intensity = _steady[i];
            lamps[i].enabled   = _baseEnabled[i];
        }
    }

    private void SetAllEnabled(bool on)
    {
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null) continue;
            lamps[i].enabled = on && _baseEnabled[i];
        }
    }
}
