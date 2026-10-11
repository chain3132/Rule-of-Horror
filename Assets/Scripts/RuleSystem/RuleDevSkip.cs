using Manager;
using Player;
using RuleSystem.Rule;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuleSystem
{
    /// <summary>
    /// เครื่องมือ dev สำหรับกระโดดเข้ากฎ 1-3 ตรงๆ โดยไม่ต้องเล่นไล่ตามเวลา
    ///
    /// วิธีใช้:
    ///   1. แปะ component นี้ไว้บน GameObject ไหนก็ได้ใน scene (ช่อง rule1-3 เว้นว่างได้ มันหาเอง)
    ///   2. กด Play → กด Spacebar ข้ามอินโทร
    ///   3. กด F1 / F2 / F3 → เข้ากฎ 1 / 2 / 3 ทันที
    ///
    /// ปุ่มลัด (มีเฉพาะใน Editor / Development build / ไฟล์เทส):
    ///   F1 กฎ 1        F2 กฎ 2        F3 กฎ 3
    ///   PageDown  ข้ามเฟสของกฎ 2 ไปทีละเฟส      Home  กฎ 2 → ไปซ่อมตู้ไฟเลย
    ///   End       จบกฎที่เล่นอยู่ + ปล่อยนาฬิกา
    ///
    /// กฎ 4-5 มีเครื่องมือของตัวเองอยู่แล้ว (Rule4DevSkip = F4/F5, Rule5DevSkip = F6-F10)
    /// ตัวนี้จึงจับแค่ 1-3 ไม่ไปทับปุ่มกัน — แต่ HUD โชว์สถานะกฎทุกข้อให้ดูได้
    ///
    /// ทำไมต้องหยุดนาฬิกาตอนเริ่ม: RuleManager.CheckRules ทำงานตอน OnTimeChanged เท่านั้น
    /// ถ้าปล่อยนาฬิกาเดิน เวลาจะไหลไปถึงหน้าต่างของกฎอื่นเองระหว่างที่เรายังหาปุ่มอยู่
    /// แล้วกฎนั้นจะ trigger แทรกขึ้นมาทับกฎที่เรากำลังจะเทส
    /// </summary>
    public class RuleDevSkip : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("เว้นว่างได้ — จะไปหา component ในซีนเอง ใส่เองเฉพาะกรณีมีหลายตัวในซีน")]
        [SerializeField] private Rule1 rule1;
        [Tooltip("เว้นว่างได้ — จะไปหา component ในซีนเอง")]
        [SerializeField] private Rule.Rule2 rule2;
        [Tooltip("เว้นว่างได้ — จะไปหา component ในซีนเอง")]
        [SerializeField] private Rule3 rule3;

        [Tooltip("ติ๊กออกก่อนส่งงาน: ปุ่มลัดทั้งหมดจะหยุดทำงาน")]
        [SerializeField] private bool enableDevSkip = true;

        [Header("Hotkeys")]
        [SerializeField] private Key rule1Key = Key.F1;
        [SerializeField] private Key rule2Key = Key.F2;
        [SerializeField] private Key rule3Key = Key.F3;
        [Tooltip("ข้ามเฟสของกฎ 2 ไปทีละเฟส (None → ไฟดับ → ... → ซ่อมตู้ไฟ → กลับไปนั่ง)")]
        [SerializeField] private Key rule2NextPhaseKey = Key.PageDown;
        [Tooltip("กฎ 2 → กระโดดไปเฟสซ่อมตู้ไฟตรงๆ เฟสที่ต้องเทสบ่อยสุด")]
        [SerializeField] private Key rule2FixPanelKey = Key.Home;
        [Tooltip("จบกฎที่กำลังเล่นอยู่ แล้วปล่อยนาฬิกาให้เดินต่อ")]
        [SerializeField] private Key endRuleKey = Key.End;

        [Header("Clock")]
        [Tooltip("หยุดนาฬิกาทันทีที่เริ่มเกม กันกฎอื่น trigger แทรกระหว่างที่ยังไม่ได้กดปุ่มลัด")]
        [SerializeField] private bool freezeClockOnStart = true;

        [Header("On-Screen HUD")]
        [Tooltip("โชว์ปุ่มลัด + สถานะกฎบนหน้าจอ จำเป็นในไฟล์ build ที่คนเทสไม่มี Console ให้อ่าน")]
        [SerializeField] private bool showOnScreenHelp = true;
        [SerializeField] private float statusDuration = 6f;

        private string      _status = "";
        private float       _statusTime = -999f;
        private GUIStyle    _hudStyle;
        private RuleManager _manager;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE_TEST_BUILD || RULE4_TEST_BUILD || RULE5_TEST_BUILD
            if (!enableDevSkip) return;

            ResolveRules();

            if (freezeClockOnStart && TimeManager.instance != null)
            {
                TimeManager.instance.IsPauseTime(true);
                Notify($"หยุดนาฬิกาไว้แล้ว — {rule1Key}/{rule2Key}/{rule3Key} = เข้ากฎ 1/2/3");
            }
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE_TEST_BUILD || RULE4_TEST_BUILD || RULE5_TEST_BUILD
            if (!enableDevSkip) return;

            // ย้ำหยุดนาฬิกาทุกเฟรมจนกว่าจะมีกฎเริ่มทำงาน — CutsceneManager / BlinkRoutine
            // สั่ง IsPauseTime(false) ทีหลังเสมอ พอกฎเริ่มแล้วกฎนั้นเป็นคนคุมนาฬิกาเอง
            if (freezeClockOnStart && TimeManager.instance != null && ActiveRule() == null)
                TimeManager.instance.IsPauseTime(true);

            if (Keyboard.current == null) return;

            if (Keyboard.current[rule1Key].wasPressedThisFrame) JumpToRule1();
            if (Keyboard.current[rule2Key].wasPressedThisFrame) JumpToRule2();
            if (Keyboard.current[rule3Key].wasPressedThisFrame) JumpToRule3();

            if (Keyboard.current[rule2NextPhaseKey].wasPressedThisFrame) Rule2NextPhase();
            if (Keyboard.current[rule2FixPanelKey].wasPressedThisFrame)  Rule2FixPanel();
            if (Keyboard.current[endRuleKey].wasPressedThisFrame)        EndActiveRule();
#endif
        }

        /// <summary>หา component ของกฎในซีนให้ช่องที่เว้นว่างไว้ — ไม่ต้องลากใส่เอง</summary>
        private void ResolveRules()
        {
            if (rule1    == null) rule1    = FindAnyObjectByType<Rule1>(FindObjectsInactive.Include);
            if (rule2    == null) rule2    = FindAnyObjectByType<Rule.Rule2>(FindObjectsInactive.Include);
            if (rule3    == null) rule3    = FindAnyObjectByType<Rule3>(FindObjectsInactive.Include);
            if (_manager == null) _manager = FindAnyObjectByType<RuleManager>(FindObjectsInactive.Include);
        }

        // ─────────────────────────── Jump ───────────────────────────

        /// <summary>
        /// กฎ 1 — เรียก TriggerStartRule ไม่ใช่ตั้งเวลาแล้วรอ RuleManager
        ///
        /// กฎ 1 ตั้ง startHour = 23 ไว้ใน Inspector เพื่อไม่ให้ RuleManager auto-trigger
        /// (ของจริงถูกเรียกจาก FriendListController ตอนอ่านแชทจบ) เลขเวลาในนั้นจึงเป็นค่าหลอก
        /// ตั้งนาฬิกาตามมันไม่ได้ ต้องเรียกประตูหน้าของกฎเองตรงๆ
        /// </summary>
        [ContextMenu("Jump To Rule 1")]
        public void JumpToRule1()
        {
            ResolveRules();
            if (rule1 == null) { Notify("ไม่เจอ Rule1 ในซีน", true); return; }
            if (rule1.ruleActive) { Notify("กฎ 1 ทำงานอยู่แล้ว", true); return; }

            EndActiveRule(silent: true);
            rule1.TriggerStartRule();

            if (rule1.ruleActive) Notify("เริ่มกฎ 1 แล้ว — โทรศัพท์หายไปแล้ว ไปหาให้เจอ");
            else Notify("กฎ 1 ไม่เริ่ม — เล่นจบไปแล้วรอบนี้ (_hasCompleted) ต้องกด Play ใหม่", true);
        }

        [ContextMenu("Jump To Rule 2")]
        public void JumpToRule2() => JumpToTimedRule(rule2, 2,
            "เริ่มกฎ 2 แล้ว — ต้องนั่งที่ศาลาก่อนกฎจึงจะเดินต่อ (กฎรอ WaitUntil นั่ง)");

        [ContextMenu("Jump To Rule 3")]
        public void JumpToRule3() => JumpToTimedRule(rule3, 3,
            "เริ่มกฎ 3 แล้ว — ต้องนั่งที่ศาลาก่อนกฎจึงจะเดินต่อ (กฎรอ WaitUntil นั่ง)");

        /// <summary>
        /// กฎที่ trigger ตามเวลาปกติ (2, 3) — ตั้งนาฬิกาไปที่ต้นหน้าต่างของกฎนั้น
        ///
        /// SetTime ยิง OnTimeChanged เสมอแม้นาฬิกาถูกหยุดอยู่ → RuleManager.CheckRules ทำงาน
        /// → มันเริ่มกฎให้เองเลย เราจึงต้องเช็ก ruleActive ก่อนจะเรียก StartRule ซ้ำ
        /// เรียกซ้ำ = RuleFlow() เดินสองรอบพร้อมกัน (RuleBase.StartRule ไม่มี guard)
        /// </summary>
        private void JumpToTimedRule(RuleBase rule, int number, string okMessage)
        {
            ResolveRules();
            if (rule == null) { Notify($"ไม่เจอ Rule{number} ในซีน", true); return; }
            if (rule.ruleActive) { Notify($"กฎ {number} ทำงานอยู่แล้ว", true); return; }

            EndActiveRule(silent: true);

            if (TimeManager.instance == null) { Notify("ไม่เจอ TimeManager ในซีน", true); return; }

            TimeManager.instance.IsPauseTime(true);
            TimeManager.instance.SetTime(rule.startHour, rule.startMinute);

            if (!rule.ruleActive) rule.StartRule();   // CheckRules อาจเริ่มให้แล้วจาก SetTime ข้างบน

            Notify(rule.ruleActive
                ? okMessage
                : $"ตั้งเวลา {rule.startHour:00}:{rule.startMinute:00} แล้วแต่กฎ {number} ไม่เริ่ม — " +
                  "เช็กว่าอยู่ใน RuleManager.rules หรือเปล่า");
        }

        // ─────────────────────────── Rule 2 phases ───────────────────────────

        private void Rule2NextPhase()
        {
            ResolveRules();
            if (!Rule2Ready()) return;

            rule2.DebugAdvancePhase();
            Notify($"กฎ 2 เฟส: {rule2.CurrentPhase}");
        }

        private void Rule2FixPanel()
        {
            ResolveRules();
            if (!Rule2Ready()) return;

            rule2.DebugJumpToFixPanel();
            Notify($"กฎ 2 → ซ่อมตู้ไฟ ({rule2.ActivePanelCount} ตู้ ซ่อมแล้ว {rule2.FixedPanelCount})");
        }

        private bool Rule2Ready()
        {
            if (rule2 == null) { Notify("ไม่เจอ Rule2 ในซีน", true); return false; }
            if (!rule2.ruleActive) { Notify($"กฎ 2 ยังไม่เริ่ม — กด {rule2Key} ก่อน", true); return false; }
            return true;
        }

        // ─────────────────────────── End ───────────────────────────

        /// <summary>
        /// กฎข้อไหนที่กำลังทำงานอยู่ (ดูทุกข้อใน RuleManager ไม่ใช่แค่ 1-3)
        ///
        /// ใช้ RuleManager ที่ cache ไว้ ห้ามเรียก FindAnyObjectByType ตรงนี้ —
        /// Update เรียกทุกเฟรม และ OnGUI เรียกหลายรอบต่อเฟรม (รอบละ event) การสแกนซีน
        /// ทุกครั้งจะกินเฟรมจนเกมที่กำลังเทสอยู่กระตุก แล้ววัดจังหวะอะไรไม่ได้เลย
        /// </summary>
        private RuleBase ActiveRule()
        {
            if (_manager?.rules == null) return null;

            foreach (var rule in _manager.rules)
                if (rule != null && rule.ruleActive) return rule;

            return null;
        }

        [ContextMenu("End Active Rule")]
        public void EndActiveRule() => EndActiveRule(silent: false);

        private void EndActiveRule(bool silent)
        {
            var active = ActiveRule();
            if (active == null)
            {
                if (!silent) Notify("ไม่มีกฎไหนทำงานอยู่");
                return;
            }

            active.EndRule();

            // กฎหลายข้อสั่ง IsPauseTime(false) + SetTime ใน EndRule อยู่แล้ว แต่บางข้อไม่ทำ
            // ย้ำปล่อยนาฬิกาเองกันค้าง — ไม่งั้นเวลาไม่เดิน กฎถัดไปก็ไม่ trigger
            if (TimeManager.instance != null) TimeManager.instance.IsPauseTime(false);

            if (!silent) Notify($"จบ {active.GetType().Name} แล้ว + ปล่อยนาฬิกา");
        }

        // ─────────────────────────── HUD ───────────────────────────

        private void Notify(string message, bool isWarning = false)
        {
            if (isWarning) Debug.LogWarning($"[RuleDevSkip] {message}", this);
            else           Debug.Log($"[RuleDevSkip] {message}", this);

            _status     = message;
            _statusTime = Time.unscaledTime;   // unscaled เพราะบางจังหวะ timeScale ถูกหยุด
        }

        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE_TEST_BUILD || RULE4_TEST_BUILD || RULE5_TEST_BUILD
            if (!enableDevSkip || !showOnScreenHelp) return;

            if (_hudStyle == null)
                _hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperLeft };

            var active = ActiveRule();

            string text =
                $"[RULE TEST]  {rule1Key} กฎ 1  |  {rule2Key} กฎ 2  |  {rule3Key} กฎ 3   " +
                $"(กฎ 4-5 ใช้ F4 / F6 ของเครื่องมือตัวเอง)\n" +
                $"{endRuleKey} จบกฎที่เล่นอยู่  |  {rule2NextPhaseKey} ข้ามเฟสกฎ 2  |  " +
                $"{rule2FixPanelKey} กฎ 2 → ซ่อมตู้ไฟ\n" +
                StateLine(active);

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime - _statusTime < statusDuration)
                text += "\n" + _status;

            // วางไว้มุมซ้ายล่าง — มุมซ้ายบนเป็นที่ของ Rule4DevSkip / Rule5DevSkip อยู่แล้ว
            DrawShadowedLabel(new Rect(14f, Screen.height - 110f, Screen.width - 28f, 100f),
                              text, _hudStyle, active != null ? new Color(0.55f, 0.9f, 1f) : Color.white);
#endif
        }

        /// <summary>
        /// บรรทัดสถานะ — สิ่งที่ต้องรู้ตอนกดปุ่มลัดแล้ว "ไม่มีอะไรเกิดขึ้น"
        ///
        /// กฎ 2 และ 3 ติด WaitUntil(PlayerIsSitting) เป็นบรรทัดแรกของ RuleFlow
        /// กดข้ามเข้ามาแล้วยังยืนอยู่ = กฎค้างรออยู่เงียบๆ ไม่มี log ไม่มีอะไรบอก
        /// เลยต้องโชว์สถานะนั่ง/ยืนกับตาเปิด/ปิดไว้ตรงนี้ให้เห็นว่ามันรออะไร
        /// </summary>
        private string StateLine(RuleBase active)
        {
            var    pc      = PlayerController.Instance;
            string sitting = pc == null ? "ไม่เจอ PlayerController"
                           : pc.IsSitting() ? "นั่งอยู่" : "ยืนอยู่ (กฎ 2/3 รอให้นั่งก่อน)";

            var    gmc  = GameModeController.instance;
            string eyes = gmc == null ? "-" : gmc.IsEyesOpen ? "ตาเปิด" : "ตาปิด (รอ transition)";

            var    tm    = TimeManager.instance;
            string clock = tm == null ? "ไม่เจอ TimeManager"
                         : $"{tm.currentHour:00}:{tm.currentMinute:00} {(tm.IsPaused ? "(หยุด)" : "(เดิน)")}";

            string ruleText = active == null
                ? "ไม่มีกฎทำงานอยู่"
                : active == (RuleBase)rule2
                    ? $"กฎ 2 — เฟส {rule2.CurrentPhase} " +
                      $"(ตู้ไฟ {rule2.FixedPanelCount}/{rule2.ActivePanelCount})"
                    : active.GetType().Name;

            return $"นาฬิกา {clock}  |  ผู้เล่น: {sitting}  |  {eyes}  |  {ruleText}";
        }

        /// <summary>วาดเงาดำใต้ตัวอักษร — ไม่งั้นข้อความขาวจะจมหายไปกับฉากสว่าง</summary>
        private static void DrawShadowedLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);

            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
