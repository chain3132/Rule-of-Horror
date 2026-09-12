using Manager;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rule4
{
    /// <summary>
    /// เครื่องมือ dev สำหรับเทส Rule 4 โดยไม่ต้องเล่น Rule 1-3 ให้จบก่อน
    ///
    /// วิธีใช้:
    ///   1. แปะ component นี้ไว้บน GameObject ไหนก็ได้ใน scene แล้วลาก Rule4 ใส่ช่อง rule4
    ///   2. กด Play → กด Spacebar ข้าม intro → รอให้ตัวละครนั่งลงเสร็จ
    ///   3. กด F4 → เข้า Rule 4 ทันที
    ///
    /// ทำไมถึงไม่ชนกับกฎอื่น:
    ///   Rule4.RuleFlow() สั่ง IsPauseTime(true) เป็นบรรทัดแรก นาฬิกาเลยหยุดเดิน
    ///   RuleManager.CheckRules ทำงานตอน OnTimeChanged เท่านั้น → Rule 2/3 ไม่มีทาง trigger แทรก
    ///   ส่วน Rule 1 ตั้ง startHour = 23 อยู่แล้ว (trigger จากแชทเท่านั้น) จึงไม่ยุ่งด้วย
    ///
    /// ตัว component จะหยุดนาฬิกาให้ตั้งแต่ Start ด้วย กันกรณีมัวหาปุ่มอยู่แล้ว Rule 2 ชิงเริ่มก่อน
    /// </summary>
    public class Rule4DevSkip : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("ลาก GameObject ที่มี component Rule4 มาใส่")]
        [SerializeField] private RuleSystem.Rule.Rule4 rule4;

        [Tooltip("ปิดตัวนี้เมื่อจะส่งเกมจริง — ปิดแล้วปุ่มลัดทั้งหมดจะไม่ทำงาน")]
        [SerializeField] private bool enableDevSkip = true;

        [Header("Hotkeys")]
        [Tooltip("ปุ่มกระโดดเข้า Rule 4")]
        [SerializeField] private Key jumpKey = Key.F4;

        [Tooltip("ปุ่มเปิด/ปิดเส้น debug ชี้ไปยังตุ๊กตาที่เหลือ (ดูใน Scene view)")]
        [SerializeField] private Key toggleDollLinesKey = Key.F5;

        [Header("Clock")]
        [Tooltip("หยุดนาฬิกาทันทีที่เริ่มเกม — กัน Rule 2/3 ชิง trigger ระหว่างที่ยังไม่ได้กด F4")]
        [SerializeField] private bool freezeClockOnStart = true;

        [Tooltip("เวลาที่จะตั้งให้ก่อนเริ่ม Rule 4 (ปกติ 21:59 = ก่อนหน้าต่างของ Rule 4 พอดี)")]
        [SerializeField] private int jumpHour = 21;
        [SerializeField] private int jumpMinute = 59;

        [Header("Debug")]
        [Tooltip("ชี้ตำแหน่งตุ๊กตาที่ยังไม่ถูกเก็บ + ตำแหน่งผี\n" +
                 "ใน Editor วาดเป็นเส้นใน Scene view (ต้องเปิด Gizmos)\n" +
                 "ในไฟล์ build วาดเป็นป้ายบนหน้าจอแทน เพราะ Debug.DrawLine ไม่ขึ้นในเกมจริง")]
        [SerializeField] private bool showDollLines;

        [Header("On-Screen HUD")]
        [Tooltip("แสดงปุ่มลัดและสถานะบนหน้าจอ — จำเป็นในไฟล์ build เพราะคนเทสไม่มี Console ให้ดู")]
        [SerializeField] private bool showOnScreenHelp = true;

        [Tooltip("ข้อความสถานะค้างบนจอกี่วินาที")]
        [SerializeField] private float statusDuration = 6f;

        // ไฟล์ build ไม่มี Console ให้เปิดดู ทุกอย่างที่คนเทสต้องรู้จึงต้องขึ้นจอเอง
        private string   _status = "";
        private float    _statusTime = -999f;
        private GUIStyle _hudStyle;
        private GUIStyle _markerStyle;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE4_TEST_BUILD
            if (!enableDevSkip) return;

            if (rule4 == null)
            {
                Notify("ยังไม่ได้ลาก Rule4 ใส่ช่อง rule4 ใน Inspector", true);
                return;
            }

            if (freezeClockOnStart && TimeManager.instance != null)
            {
                TimeManager.instance.IsPauseTime(true);
                Notify($"หยุดนาฬิกาไว้แล้ว — กด {jumpKey} เพื่อเข้า Rule 4");
            }
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE4_TEST_BUILD
            if (!enableDevSkip) return;

            // ย้ำหยุดนาฬิกาทุกเฟรมจนกว่าจะเข้า Rule 4
            // สั่งครั้งเดียวใน Start() ไม่พอ เพราะ CutsceneManager และ GameModeController.BlinkRoutine
            // สั่ง IsPauseTime(false) ทีหลังเสมอ พอเข้า Rule 4 แล้ว Rule4 เป็นคนคุมเอง
            if (freezeClockOnStart && rule4 != null && !rule4.ruleActive && TimeManager.instance != null)
                TimeManager.instance.IsPauseTime(true);

            if (Keyboard.current == null) return;

            if (Keyboard.current[jumpKey].wasPressedThisFrame) JumpToRule4();

            if (Keyboard.current[toggleDollLinesKey].wasPressedThisFrame)
            {
                showDollLines = !showDollLines;
                Notify($"ป้ายชี้ตุ๊กตา: {(showDollLines ? "เปิด" : "ปิด")}");
                if (showDollLines) LogDollPositions();
            }

            if (showDollLines) DrawDollLines();
#endif
        }

        // ─────────────────────────── Jump ───────────────────────────

        /// <summary>เริ่ม Rule 4 ทันที (เรียกจากปุ่ม หรือจาก context menu ก็ได้)</summary>
        [ContextMenu("Jump To Rule 4")]
        public void JumpToRule4()
        {
            if (rule4 == null)
            {
                Notify("ยังไม่ได้ลาก Rule4 ใส่ช่อง rule4 ใน Inspector", true);
                return;
            }

            if (rule4.ruleActive)
            {
                Notify("Rule 4 ทำงานอยู่แล้ว — ไม่ต้องกดซ้ำ", true);
                return;
            }

            // ตั้งเวลาให้อยู่ก่อนหน้าต่างของ Rule 4 พอดี
            // (Rule4.EndRule จะ SetTime(22,39) ตอนจบ — ตั้งไว้แบบนี้ลำดับเวลาจะไม่เพี้ยน)
            if (TimeManager.instance != null)
            {
                TimeManager.instance.SetTime(jumpHour, jumpMinute);
                TimeManager.instance.IsPauseTime(true);
            }

            var pc = PlayerController.Instance;
            if (pc == null)
            {
                Notify("ไม่เจอ PlayerController ใน scene", true);
                return;
            }

            rule4.StartRule();

            if (!pc.IsSitting())
                // RuleFlow มี WaitUntil(PlayerIsSitting) อยู่แล้ว — สั่งไปเลย เดี๋ยวมันรอเอง
                Notify("สั่งเริ่ม Rule 4 แล้ว — แต่ยังไม่ได้นั่ง เดินไปที่ศาลาแล้วกด E เพื่อให้กฎเริ่มจริง");
            else
                Notify("เริ่ม Rule 4 แล้ว");
        }

        // ─────────────────────────── HUD ───────────────────────────

        /// <summary>
        /// log ลง Console และเก็บไว้โชว์บนจอด้วย
        /// ไฟล์ build ไม่มี Console ให้เปิด ถ้าไม่เอาขึ้นจอคนเทสจะไม่รู้เลยว่ากดแล้วติดอะไร
        /// </summary>
        private void Notify(string message, bool isWarning = false)
        {
            if (isWarning) Debug.LogWarning($"[Rule4DevSkip] {message}", this);
            else           Debug.Log($"[Rule4DevSkip] {message}", this);

            _status     = message;
            _statusTime = Time.unscaledTime;   // unscaled เพราะบางจังหวะ timeScale ถูกหยุด
        }

        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE4_TEST_BUILD
            if (!enableDevSkip || !showOnScreenHelp) return;

            EnsureStyles();

            bool active = rule4 != null && rule4.ruleActive;

            string text = active
                ? $"[RULE 4 TEST BUILD]  {toggleDollLinesKey} = เปิด/ปิดป้ายชี้ตุ๊กตา"
                : $"[RULE 4 TEST BUILD]  {jumpKey} = เข้า Rule 4 ทันที   |   {toggleDollLinesKey} = ป้ายชี้ตุ๊กตา";

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime - _statusTime < statusDuration)
                text += "\n" + _status;

            DrawShadowedLabel(new Rect(14f, 14f, Screen.width - 28f, 70f),
                              text, _hudStyle, active ? new Color(1f, 0.82f, 0.35f) : Color.white);

            if (showDollLines) DrawDollMarkersOnScreen();
#endif
        }

        private void EnsureStyles()
        {
            if (_hudStyle == null)
                _hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperLeft };

            if (_markerStyle == null)
                _markerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        }

        /// <summary>วาดเงาดำใต้ตัวอักษร — ไม่งั้นข้อความขาวจะจมหายไปกับฉากสว่าง</summary>
        private static void DrawShadowedLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);

            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        // ─────────────────────────── Doll Debug ───────────────────────────

        private void LogDollPositions()
        {
            if (rule4 == null || rule4.ActiveDolls == null) return;

            foreach (var d in rule4.ActiveDolls)
            {
                if (d == null || !d.gameObject.activeSelf) continue;
                Debug.Log($"[Rule4DevSkip] ตุ๊กตา ({d.Sound}) อยู่ที่ {d.transform.position}", d);
            }
        }

        /// <summary>
        /// วาดเส้นจากผู้เล่นไปยังตุ๊กตาที่ยังเหลือ + ไปยังผี
        /// จำเป็นตอนที่ FMOD ยังไม่มี event เสียงตุ๊กตา เพราะตุ๊กตาจะเงียบสนิทจนหาไม่เจอ
        /// </summary>
        private void DrawDollLines()
        {
            var pc = PlayerController.Instance;
            if (pc == null || rule4 == null) return;

            Vector3 from = pc.transform.position + Vector3.up;

            if (rule4.ActiveDolls != null)
            {
                foreach (var d in rule4.ActiveDolls)
                {
                    if (d == null || !d.gameObject.activeSelf) continue;
                    Debug.DrawLine(from, d.transform.position, ColorForSound(d.Sound));
                }
            }

            var ghost = rule4.ActiveGhost;
            if (ghost != null)
                Debug.DrawLine(from, ghost.transform.position, Color.magenta);
        }

        /// <summary>
        /// ชี้ตำแหน่งตุ๊กตา/ผีเป็นป้ายบนหน้าจอ
        /// Debug.DrawLine เห็นได้แค่ใน Scene view ของ Editor เท่านั้น ในไฟล์ build มองไม่เห็นเลย
        /// ป้ายพวกนี้เลยเป็นทางเดียวที่คนเทสจะหาตุ๊กตาเจอตอนที่เสียงใน FMOD ยังไม่ครบ
        /// </summary>
        private void DrawDollMarkersOnScreen()
        {
            var cam = Camera.main;
            if (cam == null || rule4 == null) return;

            if (rule4.ActiveDolls != null)
            {
                foreach (var d in rule4.ActiveDolls)
                {
                    if (d == null || !d.gameObject.activeSelf) continue;
                    DrawWorldMarker(cam, d.transform.position, ColorForSound(d.Sound), d.Sound.ToString());
                }
            }

            var ghost = rule4.ActiveGhost;
            if (ghost != null) DrawWorldMarker(cam, ghost.transform.position, Color.magenta, "ผี");
        }

        private void DrawWorldMarker(Camera cam, Vector3 worldPos, Color color, string label)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos + Vector3.up);

            // z <= 0 คืออยู่ข้างหลังกล้อง ค่า x/y ที่ได้จะกลับด้าน ต้องพลิกกลับก่อนแล้วดันไปติดขอบจอ
            bool behind = sp.z <= 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }

            float x = Mathf.Clamp(sp.x, 60f, Screen.width  - 60f);
            float y = Mathf.Clamp(Screen.height - sp.y, 40f, Screen.height - 40f);

            float dist = Vector3.Distance(cam.transform.position, worldPos);
            string text = behind ? $"↓ {label} {dist:0} m" : $"◆ {label} {dist:0} m";

            DrawShadowedLabel(new Rect(x - 90f, y - 10f, 180f, 20f), text, _markerStyle, color);
        }

        private static Color ColorForSound(DollSound sound) => sound switch
        {
            DollSound.Cry   => Color.cyan,
            DollSound.Laugh => Color.yellow,
            DollSound.Hum   => Color.green,
            DollSound.Call  => new Color(1f, 0.5f, 0f), // ส้ม
            _               => Color.white              // Cough
        };
    }
}
