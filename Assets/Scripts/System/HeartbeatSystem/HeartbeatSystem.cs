using Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace System.HeartbeatSystem
{
    /// <summary>
    /// ระดับโซนที่ผู้เล่นอยู่ปัจจุบัน
    /// Green   = โซนปลอดภัย
    /// Orange  = โซนเตือน
    /// Red     = โซนอันตราย (ยังมีเวลา)
    /// Outside = นอกทุกโซน → countdown ตาย
    /// </summary>
    public enum ZoneLevel { Green = 0, Orange = 1, Red = 2, Outside = 3 }

    /// <summary>
    /// เขตของศาลา + กติกาเขตตอน Tension ("อย่าไปไกลกว่านี้" เดิมเป็นของกฎ 1 ตอนนี้ใช้ทุกกฎ)
    ///
    /// มีผลเฉพาะตอน Tension และมีกฎที่กำลังเล่นอยู่จริง: ยิ่งออกไกล ภาพยิ่งบิดและหัวใจยิ่งเต้นแรง
    /// อยู่นอกทุกโซนครบ Outside Death Time = ตายตามทางของกฎข้อนั้น (RuleBase.DieOutsidePlayArea)
    /// ส่วนตอน Relax เป็นหน้าที่ของ PlayAreaBoundary ที่หันตัวผู้เล่นกลับเข้ามาแทน
    ///
    /// ไม่นับตอนนั่ง เพราะตอนนั่งผู้เล่นไม่ได้เดินไปเอง — รถของกฎ 6 พาผู้เล่นนั่งออกนอกเขตได้
    ///
    /// ทำไมอยู่ที่นี่ ไม่อยู่ในกฎ 1: กฎ 1 เป็นแค่หนึ่งในหกข้อที่ใช้กติกานี้ และตัวนี้รู้จักโซนอยู่แล้ว
    /// กฎแต่ละข้อจึงมีแค่สองอย่าง: บอกว่าตอนนี้เล่นอยู่ไหม (UsesTensionZones) กับตายยังไง
    /// </summary>
    public class HeartbeatSystem : MonoBehaviour
    {
        public Transform player;

        // Green zone (ในสุด)
        public float greenMinX;
        public float greenMaxX;
        public float greenMinZ;
        public float greenMaxZ;

        // Orange zone
        public float orangeMinX;
        public float orangeMaxX;
        public float orangeMinZ;
        public float orangeMaxZ;

        // Red zone
        public float redMinX;
        public float redMaxX;
        public float redMinZ;
        public float redMaxZ;

        [Header("กติกาเขตตอน Tension (ใช้ทุกกฎ)")]
        [Tooltip("วินาทีที่อยู่นอกทุกโซนก่อนตาย")]
        [SerializeField] private float outsideDeathTime = 5f;

        [Tooltip("LensDistortion ของ Orange zone — แนะนำ -0.10 ถึง -0.20 (เบาๆ wave เป็นหลัก)")]
        [SerializeField] private float orangeLens = -0.12f;

        [Tooltip("ChromaticAberration ของ Orange zone")]
        [SerializeField] private float orangeChroma = 0.30f;

        [Tooltip("LensDistortion ของ Red zone — แนะนำ -0.25 ถึง -0.40")]
        [SerializeField] private float redLens = -0.30f;

        [Tooltip("ChromaticAberration ของ Red zone")]
        [SerializeField] private float redChroma = 0.60f;

        [Tooltip("ตัวคุม WaveDistortion shader — เว้นว่าง = หาในซีนให้เอง")]
        [SerializeField] private WaveDistortionEffect waveEffect;

        public static HeartbeatSystem instance;

        /// <summary>โซนที่ผู้เล่นอยู่ปัจจุบัน — อัปเดตทุกครั้งที่เรียก CheckPlayerInsideZone()</summary>
        public ZoneLevel CurrentZoneLevel { get; private set; } = ZoneLevel.Green;

        private RuleSystem.RuleBase[] _rules;
        private ZoneLevel?            _appliedZone;   // null = ไม่ได้ใส่ effect อยู่
        private float                 _outsideSeconds;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDisable() => StopZoneEffects();

        // ─────────────────────────── กติกาเขตตอน Tension ───────────────────────────

        private void Update()
        {
            RuleSystem.RuleBase rule = FindRuleInPlay();
            var mode = GameModeController.instance;

            // ระหว่างกะพริบตาไม่นับ — GameModeController เขียน lens/chroma ช่วงเวียนหัวอยู่ และ
            // profile จะถูกสลับตอนตาปิด ถ้าเขียนซ้อนกันภาพจะกระตุก และค่าจะไปค้างผิด profile
            bool enforcing = rule != null && player != null && mode != null
                          && mode.CurrentMode == GameMode.Tension && !mode.IsBlinking;

            if (!enforcing)
            {
                StopZoneEffects();
                return;
            }

            CheckPlayerInsideZone();

            var  pc     = PlayerController.Instance;
            bool seated = pc != null && pc.IsSitting();
            ZoneLevel zone = seated ? ZoneLevel.Green : CurrentZoneLevel;

            ApplyZoneEffects(zone, mode);

            AudioManager.instance.SetZoneHeartbeat(HeartValueOf(zone));
            AudioManager.instance.UpdateHeartbeat();

            if (zone != ZoneLevel.Outside)
            {
                _outsideSeconds = 0f;
                return;
            }

            _outsideSeconds += Time.deltaTime;
            if (_outsideSeconds < outsideDeathTime) return;

            _outsideSeconds = 0f;
            Debug.Log($"[HeartbeatSystem] อยู่นอกเขตครบ {outsideDeathTime:0.#} วิ — ตายในกฎ {rule.Number}", this);
            rule.DieOutsidePlayArea();
        }

        /// <summary>กฎที่กำลังเล่นอยู่ตอนนี้ (null = ไม่มี) — ระหว่างรอเริ่มกฎ / ฉากตาย / บทจบ จะไม่มี</summary>
        private RuleSystem.RuleBase FindRuleInPlay()
        {
            if (_rules == null)
                _rules = FindObjectsByType<RuleSystem.RuleBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var rule in _rules)
            {
                if (rule == null)
                {
                    _rules = null;   // โหลดซีนใหม่ กฎชุดเก่าถูกทำลายไปแล้ว — เฟรมหน้าหาใหม่
                    return null;
                }

                if (rule.UsesTensionZones) return rule;
            }

            return null;
        }

        private static float HeartValueOf(ZoneLevel zone)
        {
            switch (zone)
            {
                case ZoneLevel.Green:  return 0f;
                case ZoneLevel.Orange: return 0.5f;
                default:               return 1f;
            }
        }

        // เขียนเฉพาะตอนโซนเปลี่ยน ไม่เขียนทุกเฟรม — effect พวกนี้ไม่ได้ขยับเองระหว่างอยู่ในโซนเดียวกัน
        private void ApplyZoneEffects(ZoneLevel zone, GameModeController mode)
        {
            if (_appliedZone == zone) return;
            _appliedZone = zone;

            if (waveEffect == null)
                waveEffect = FindAnyObjectByType<WaveDistortionEffect>(FindObjectsInactive.Include);

            switch (zone)
            {
                case ZoneLevel.Green:
                    SetLensAndChroma(mode, 0f, 0f);
                    if (waveEffect != null) waveEffect.SetOff();
                    break;

                case ZoneLevel.Orange:
                    SetLensAndChroma(mode, orangeLens, orangeChroma);
                    if (waveEffect != null) waveEffect.SetOrange();
                    break;

                case ZoneLevel.Red:
                    SetLensAndChroma(mode, redLens, redChroma);
                    if (waveEffect != null) waveEffect.SetRed();
                    break;

                // กำลังนับถอยหลังตาย — ภาพบิดสุดแบบนิ่งๆ น่ากลัวกว่าภาพที่ยังแกว่ง
                case ZoneLevel.Outside:
                    SetLensAndChroma(mode, -1f, 1f);
                    if (waveEffect != null) waveEffect.SetOff();
                    break;
            }
        }

        private void StopZoneEffects()
        {
            _outsideSeconds = 0f;

            if (_appliedZone == null) return;
            _appliedZone = null;

            if (waveEffect != null) waveEffect.SetOff();
            if (GameModeController.instance != null) SetLensAndChroma(GameModeController.instance, 0f, 0f);
            if (AudioManager.instance != null) AudioManager.instance.ClearZoneHeartbeat();
        }

        private static void SetLensAndChroma(GameModeController mode, float lens, float chroma)
        {
            Volume volume = mode.globalVolume;
            if (volume == null || volume.profile == null) return;

            if (volume.profile.TryGet(out LensDistortion lensDistortion))
            {
                lensDistortion.intensity.value = lens;
                lensDistortion.center.value    = Vector2.zero;
            }

            if (volume.profile.TryGet(out ChromaticAberration aberration))
                aberration.intensity.value = chroma;
        }

        // ─────────────────────────── โซน ───────────────────────────

        public void CheckPlayerInsideZone()
        {
            Vector3 pos = player.position;

            if (Inside(pos, greenMinX, greenMaxX, greenMinZ, greenMaxZ))
                CurrentZoneLevel = ZoneLevel.Green;
            else if (Inside(pos, orangeMinX, orangeMaxX, orangeMinZ, orangeMaxZ))
                CurrentZoneLevel = ZoneLevel.Orange;
            else if (Inside(pos, redMinX, redMaxX, redMinZ, redMaxZ))
                CurrentZoneLevel = ZoneLevel.Red;
            else
                CurrentZoneLevel = ZoneLevel.Outside;
        }

        bool Inside(Vector3 p, float minX, float maxX, float minZ, float maxZ)
        {
            return p.x > minX && p.x < maxX && p.z > minZ && p.z < maxZ;
        }

        void OnDrawGizmos()
        {
            // Green zone
            Gizmos.color = Color.green;
            DrawRect(greenMinX, greenMaxX, greenMinZ, greenMaxZ);

            // Orange zone
            Gizmos.color = new Color(1f, 0.5f, 0f);
            DrawRect(orangeMinX, orangeMaxX, orangeMinZ, orangeMaxZ);

            // Red zone
            Gizmos.color = Color.red;
            DrawRect(redMinX, redMaxX, redMinZ, redMaxZ);
        }

        void DrawRect(float minX, float maxX, float minZ, float maxZ)
        {
            Vector3 a = new Vector3(minX, 0, minZ);
            Vector3 b = new Vector3(maxX, 0, minZ);
            Vector3 c = new Vector3(maxX, 0, maxZ);
            Vector3 d = new Vector3(minX, 0, maxZ);

            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }
}
