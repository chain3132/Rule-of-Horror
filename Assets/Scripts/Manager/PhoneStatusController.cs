using System;
using UnityEngine;

namespace Manager
{
    /// <summary>
    /// แถบสถานะของโทรศัพท์ — นาฬิกา แบต สัญญาณ และอาการหลอน
    ///
    /// แบตไม่ใช่ตัวจับเวลาที่เดินเองอิสระ แต่คำนวณจากนาฬิกาในเกมตรงๆ
    /// เหตุผล: ค่ามันต้องเท่าเดิมทุกรอบที่เล่น ("22:43 ต้องเป็น 41%") และต้องหยุดตามตอนที่
    /// TimeManager สั่งหยุดเวลา ถ้าปล่อยให้นับเองด้วย Time.deltaTime มันจะเพี้ยนทุกครั้งที่
    /// มีคัตซีนหรือผู้เล่นอ่านแชทนาน
    ///
    /// ส่วนที่ "หลอน" ถึงจะสะสมแยก — ยิ่งอยู่ในโหมด Tension นาน แบตยิ่งหายเกินที่ควร
    /// ตรงนี้ตั้งใจให้ผู้เล่นรู้สึกว่าโทรศัพท์โดนอะไรบางอย่างดูดอยู่ ไม่ใช่แค่แบตเสื่อม
    ///
    /// ตัวนี้ไม่แตะ UI — ต่อ event แล้วเอาไปวาดเอง
    /// </summary>
    public class PhoneStatusController : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("แบตเตอรี่ — ไล่เป็นเส้นตรงตามนาฬิกาในเกม")]
        [Tooltip("เวลาเริ่มคืน และแบตตอนนั้น")]
        [SerializeField] private int   fromHour = 18;
        [SerializeField] private int   fromMinute;
        [SerializeField] private float fromPercent = 80f;

        [Tooltip("เวลาจบคืน และแบตตอนนั้น (ยังไม่รวมส่วนที่โดนหลอนดูด)")]
        [SerializeField] private int   toHour = 24;
        [SerializeField] private int   toMinute;
        [SerializeField] private float toPercent = 38f;

        [Tooltip("ต่ำกว่านี้ไม่ลงแล้ว — โทรศัพท์ต้องไม่ดับจนอ่านกฎไม่ได้")]
        [Range(0f, 20f)]
        [SerializeField] private float floorPercent = 5f;

        [Header("อาการหลอน (ช่วง Tension)")]
        [Tooltip("แบตที่หายเพิ่มต่อ 1 นาทีในเกม ขณะอยู่โหมด Tension")]
        [SerializeField] private float hauntedDrainPerMinute = 0.08f;

        [Tooltip("สัญญาณตอนปกติ / ตอนโดนหลอน (0-4 ขีด)")]
        [Range(0, 4)] [SerializeField] private int calmSignalBars    = 4;
        [Range(0, 4)] [SerializeField] private int hauntedSignalBars = 1;

        [Tooltip("ความแรงของจอกระตุกตอนโดนหลอน 0-1 — ใช้ขับ shader/animation ที่ฝั่ง UI")]
        [Range(0f, 1f)] [SerializeField] private float hauntedGlitch = 0.6f;

        [Tooltip("วินาทีที่ค่อยๆ ไล่ขึ้น/ลงเวลาสลับโหมด — ตัดทันทีจะดูเหมือนบั๊กมากกว่าผี")]
        [SerializeField] private float glitchBlendSeconds = 1.5f;

        // ─────────────────────────── Events ───────────────────────────

        /// <summary>เปอร์เซ็นต์แบตที่จะโชว์เปลี่ยน (ยิงเฉพาะตอนเลขจำนวนเต็มเปลี่ยนจริง)</summary>
        public event Action<int> OnBatteryChanged;

        /// <summary>จำนวนขีดสัญญาณเปลี่ยน 0-4</summary>
        public event Action<int> OnSignalChanged;

        /// <summary>นาฬิกาบนแถบสถานะ (ชั่วโมง, นาที)</summary>
        public event Action<int, int> OnClockChanged;

        // ─────────────────────────── Runtime ───────────────────────────

        private float _hauntedDrop;      // แบตที่หายไปเพราะโดนหลอน สะสมไปเรื่อยๆ ไม่คืน
        private float _glitch;           // ค่าที่ไล่ตามอยู่จริง
        private float _glitchTarget;
        private int   _shownPercent = -1;
        private int   _shownBars    = -1;

        /// <summary>แบตแบบทศนิยม ถ้าต้องการความละเอียด (เช่นวาดหลอดแบต)</summary>
        public float BatteryExact { get; private set; } = 100f;

        /// <summary>เลขเปอร์เซ็นต์ที่ควรโชว์</summary>
        public int Battery => Mathf.Clamp(Mathf.RoundToInt(BatteryExact), 0, 100);

        /// <summary>ขีดสัญญาณตอนนี้ 0-4</summary>
        public int SignalBars { get; private set; }

        /// <summary>0 = จอปกติ, 1 = กระตุกเต็มที่ — ส่งเข้า material/animator ของจอได้เลย</summary>
        public float Glitch01 => _glitch;

        /// <summary>true = ตอนนี้โทรศัพท์กำลังโดนหลอน</summary>
        public bool IsHaunted => _glitchTarget > 0f;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void OnEnable()
        {
            TimeManager.OnTimeChanged      += HandleTimeChanged;
            GameModeController.OnModeChanged += HandleModeChanged;
        }

        private void OnDisable()
        {
            TimeManager.OnTimeChanged      -= HandleTimeChanged;
            GameModeController.OnModeChanged -= HandleModeChanged;
        }

        private void Start()
        {
            if (GameModeController.instance != null)
                HandleModeChanged(GameModeController.instance.CurrentMode);

            if (TimeManager.instance != null)
                HandleTimeChanged(TimeManager.instance.currentHour, TimeManager.instance.currentMinute);

            _glitch = _glitchTarget;
        }

        private void Update()
        {
            if (Mathf.Approximately(_glitch, _glitchTarget)) return;

            float step = glitchBlendSeconds > 0.01f ? Time.deltaTime / glitchBlendSeconds : 1f;
            _glitch = Mathf.MoveTowards(_glitch, _glitchTarget, step);
        }

        // ─────────────────────────── Handlers ───────────────────────────

        private void HandleTimeChanged(int hour, int minute)
        {
            OnClockChanged?.Invoke(hour, minute);

            if (IsHaunted) _hauntedDrop += hauntedDrainPerMinute;

            BatteryExact = Recalculate(hour, minute);

            int shown = Battery;
            if (shown == _shownPercent) return;

            _shownPercent = shown;
            OnBatteryChanged?.Invoke(shown);
        }

        private void HandleModeChanged(GameMode mode)
        {
            bool haunted  = mode == GameMode.Tension;
            _glitchTarget = haunted ? hauntedGlitch : 0f;

            SignalBars = haunted ? hauntedSignalBars : calmSignalBars;
            if (SignalBars == _shownBars) return;

            _shownBars = SignalBars;
            OnSignalChanged?.Invoke(SignalBars);
        }

        // ─────────────────────────── Battery ───────────────────────────

        private float Recalculate(int hour, int minute)
        {
            int now   = hour      * 60 + minute;
            int start = fromHour  * 60 + fromMinute;
            int end   = toHour    * 60 + toMinute;

            float t = end > start ? Mathf.Clamp01((now - start) / (float)(end - start))
                                  : (now >= start ? 1f : 0f);

            return Mathf.Max(floorPercent, Mathf.Lerp(fromPercent, toPercent, t) - _hauntedDrop);
        }

        /// <summary>ดูดแบตทีเดียวเป็นก้อน — ใช้ตอนผีเล่นงานโทรศัพท์แบบเห็นชัดๆ</summary>
        public void DrainBurst(float percent)
        {
            if (percent <= 0f) return;

            _hauntedDrop += percent;
            if (TimeManager.instance != null)
                HandleTimeChanged(TimeManager.instance.currentHour, TimeManager.instance.currentMinute);
        }

        /// <summary>เริ่มคืนใหม่ — ล้างส่วนที่โดนหลอนดูดทิ้ง</summary>
        public void ResetHaunting()
        {
            _hauntedDrop = 0f;
            _shownPercent = -1;
        }
    }
}
