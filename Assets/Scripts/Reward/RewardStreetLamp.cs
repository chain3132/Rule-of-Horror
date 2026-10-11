using Rule4;
using UnityEngine;

/// <summary>
/// ผลของรางวัล "เสาไฟ"
///
///   ยังไม่ได้เลือก     : หรี่ไว้ตาม Unclaimed Intensity Scale — พอเห็นทางแต่ไม่สว่าง
///   เลือกแล้ว + Relax  : สว่างเต็มตามค่าที่ตั้งไว้ใน Light เอง ไม่มี cookie
///   เลือกแล้ว + Tension: ใส่ cookie รูปคน → เงาคนยืนอยู่ใต้เสาไฟ กลับ Relax แล้วหายไปเอง
///
/// ความสว่าง "เดิม" ถูกอ่านจาก Light ตอน Awake ครั้งเดียว เสาไฟแต่ละต้นจึงสว่างไม่เท่ากันได้
/// ตามที่จัดไฟไว้ในฉาก — อ่านทีหลังไม่ได้ เพราะพอหรี่ไปแล้วค่าที่อ่านได้คือค่าที่หรี่
///
/// cookie ของเดิมที่ติดมากับ Light ก็ถูกจำไว้ด้วย ตอน Relax จะคืนอันนั้นให้ ไม่ใช่ล้างเป็น None
/// </summary>
public class RewardStreetLamp : RewardEffect
{
    [Header("เสาไฟ")]
    [Tooltip("ไฟของเสาไฟทุกต้นที่รางวัลนี้คุม")]
    [SerializeField] private Light[] lamps;

    [Header("ยังไม่ได้เลือก")]
    [Tooltip("ความสว่างตอนยังไม่ได้เลือก คิดเป็นสัดส่วนของค่าเดิม (0.25 = เหลือ 25%)")]
    [Range(0f, 1f)]
    [SerializeField] private float unclaimedIntensityScale = 0.25f;

    [Header("Tension")]
    [Tooltip("รูปคนที่จะใส่เป็น Light Cookie ตอน Tension\n" +
             "Spot light ใช้รูป 2D ได้เลย / Point light ต้องเป็น Cubemap")]
    [SerializeField] private Texture tensionCookie;

    [Tooltip("ความสว่างตอน Tension คิดเป็นสัดส่วนของค่าเดิม — cookie บังแสงไปบางส่วน " +
             "ดันขึ้นเกิน 1 ได้ถ้าดูมืดกว่าที่ควร")]
    [SerializeField] private float tensionIntensityScale = 1f;

    [Header("ต่อกับไฟกระพริบของกฎ 4 (ไม่ใส่ก็ได้)")]
    [Tooltip("StreetLampFlicker ตัวที่คุมเสาไฟชุดเดียวกัน — ใส่ไว้เพื่อให้มันจำค่าความสว่างใหม่ " +
             "ถ้าความสว่างเปลี่ยนตอนที่มันกำลังกระพริบอยู่")]
    [SerializeField] private StreetLampFlicker flicker;

    private float[]   _baseIntensity;
    private Texture[] _baseCookie;
    private bool      _warnedCookieType;

    private void Awake() => CacheOriginals();

    private void CacheOriginals()
    {
        int count = lamps != null ? lamps.Length : 0;

        _baseIntensity = new float[count];
        _baseCookie    = new Texture[count];

        for (int i = 0; i < count; i++)
        {
            if (lamps[i] == null) continue;

            _baseIntensity[i] = lamps[i].intensity;
            _baseCookie[i]    = lamps[i].cookie;
        }
    }

    protected override void Apply(bool owned, GameMode mode)
    {
        if (lamps == null) return;
        if (_baseIntensity == null || _baseIntensity.Length != lamps.Length) CacheOriginals();

        bool  tension = owned && mode == GameMode.Tension;
        float scale   = !owned  ? unclaimedIntensityScale
                      : tension ? tensionIntensityScale
                                : 1f;

        for (int i = 0; i < lamps.Length; i++)
        {
            var lamp = lamps[i];
            if (lamp == null) continue;

            float wanted = _baseIntensity[i] * Mathf.Max(0f, scale);

            lamp.intensity = wanted;
            lamp.cookie    = tension ? tensionCookie : _baseCookie[i];

            // ถ้ากฎ 4 กำลังกระพริบไฟอยู่ มันคูณค่าที่ cache ไว้ตอน Play() — ไม่บอกค่าใหม่ให้มัน
            // พอกระพริบรอบหน้า RestoreLook จะดึงความสว่างกลับไปเป็นค่าก่อนได้รางวัล แล้วค้างจนจบกฎ
            if (flicker != null) flicker.SetBaseIntensity(lamp, wanted);

            WarnIfCookieTypeWrong(lamp, tension);
        }
    }

    // cookie ของ Point light ต้องเป็น Cubemap ใส่รูป 2D ไปแสงจะไม่ออกเลย (ดูเหมือนไฟเสีย)
    // ฟ้องครั้งเดียวพอ ไม่งั้นได้ log ทุกครั้งที่สลับโหมด
    private void WarnIfCookieTypeWrong(Light lamp, bool tension)
    {
        if (_warnedCookieType || !tension || tensionCookie == null) return;
        if (lamp.type != LightType.Point || tensionCookie is Cubemap) return;

        _warnedCookieType = true;
        Debug.LogWarning($"[Reward] '{lamp.name}' เป็น Point light — cookie ต้องเป็น Cubemap " +
                         "ไม่ใช่รูป 2D ถ้าอยากใช้รูปคน เปลี่ยนไฟดวงนี้เป็น Spot", lamp);
    }
}
