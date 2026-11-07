using System.Collections;
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
    ///   End       จบกฎที่เล่นอยู่ ข้อไหนก็ได้ 1-6 นับว่าผ่าน + ปล่อยนาฬิกา
    ///
    /// กฎ 4-6 มีเครื่องมือของตัวเองอยู่แล้ว (Rule4DevSkip = F4/F5, Rule5DevSkip = F6-F10,
    /// Rule6DevSkip = F11/F12/Numpad) ตัวนี้จึงจับแค่ 1-3 ไม่ไปทับปุ่มกัน
    /// ทุกตัวข้ามกฎผ่าน RuleDevTools: จบกฎเก่าให้ก่อน แล้วไม่ต้องอ่านข้อความเพื่อเข้ากฎใหม่
    /// ข้อความที่ส่งถึงพร้อมกันตอนข้ามเวลายังเปิดอ่านได้ และแชทกฎ 1 จะไม่ทำให้โทรศัพท์หาย
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

            // ย้ำหยุดนาฬิกาทุกเฟรมจนกว่าจะกดปุ่มเทสครั้งแรก — CutsceneManager / BlinkRoutine
            // สั่ง IsPauseTime(false) ทีหลังเสมอ หลังจากนั้นกฎเป็นคนคุมนาฬิกาเอง
            if (freezeClockOnStart && !RuleDevTools.HasBeenUsed && TimeManager.instance != null)
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
        }

        // ─────────────────────────── Jump ───────────────────────────

        [ContextMenu("Jump To Rule 1")]
        public void JumpToRule1()
        {
            ResolveRules();
            StartCoroutine(JumpRoutine(rule1, 1, "โทรศัพท์หายไปแล้ว ไปหาให้เจอ"));
        }

        [ContextMenu("Jump To Rule 2")]
        public void JumpToRule2()
        {
            ResolveRules();
            StartCoroutine(JumpRoutine(rule2, 2, "ไปนั่งที่ศาลาเพื่อให้กฎเริ่ม"));
        }

        [ContextMenu("Jump To Rule 3")]
        public void JumpToRule3()
        {
            ResolveRules();
            StartCoroutine(JumpRoutine(rule3, 3, "ไปนั่งที่ศาลาเพื่อให้กฎเริ่ม"));
        }

        private IEnumerator JumpRoutine(RuleBase rule, int number, string okMessage)
        {
            if (rule == null) { Notify($"ไม่เจอ Rule{number} ในซีน", true); yield break; }
            if (rule.ruleActive) { Notify($"กฎ {number} ทำงานอยู่แล้ว", true); yield break; }
            if (RuleDevTools.IsJumping) { Notify("กำลังข้ามกฎอยู่ รอสักครู่", true); yield break; }

            Notify($"กำลังข้ามไปกฎ {number}...");
            yield return RuleDevTools.JumpTo(rule);

            if (rule.ruleActive) Notify($"เริ่มกฎ {number} แล้ว — {okMessage}");
            else Notify($"กฎ {number} ไม่เริ่ม — เช็กว่าอยู่ใน RuleManager.rules หรือเปล่า", true);
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

        /// <summary>จบกฎที่กำลังเล่นอยู่ ข้อไหนก็ได้ 1-6 นับว่าผ่าน แล้วนาฬิกาเดินต่อ</summary>
        [ContextMenu("End Active Rule")]
        public void EndActiveRule()
        {
            if (RuleDevTools.IsJumping) { Notify("กำลังข้ามกฎอยู่ รอสักครู่", true); return; }

            var ended = RuleDevTools.EndActiveRule();
            if (ended == null)
            {
                Notify("ไม่มีกฎไหนทำงานอยู่");
                return;
            }

            // กฎหลายข้อสั่ง IsPauseTime(false) + SetTime ใน EndRule อยู่แล้ว แต่บางข้อไม่ทำ
            // ย้ำปล่อยนาฬิกาเองกันค้าง — ไม่งั้นเวลาไม่เดิน กฎถัดไปก็ไม่ trigger
            if (TimeManager.instance != null) TimeManager.instance.IsPauseTime(false);

            Notify($"จบ {ended.GetType().Name} แล้ว + ปล่อยนาฬิกา");
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

            var active = RuleBase.ActiveRuleInScene();

            string text =
                $"[RULE TEST]  {rule1Key} กฎ 1  |  {rule2Key} กฎ 2  |  {rule3Key} กฎ 3   " +
                $"(กฎ 4 = F4, กฎ 5 = F6, กฎ 6 = F11 หรือ Numpad 1-4)\n" +
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
        /// กฎ 2-6 รอให้นั่งก่อนเริ่ม กดข้ามเข้ามาแล้วยังยืนอยู่ = กฎค้างรออยู่เงียบๆ
        /// เลยต้องโชว์สถานะนั่ง/ยืน โหมด และกติกาเขตไว้ตรงนี้ให้เห็นว่ามันรออะไร
        /// </summary>
        private string StateLine(RuleBase active)
        {
            var    pc      = PlayerController.Instance;
            string sitting = pc == null ? "ไม่เจอ PlayerController"
                           : pc.IsSitting() ? "นั่งอยู่" : "ยืนอยู่ (กฎรอให้นั่งก่อน)";

            var    gmc  = GameModeController.instance;
            string eyes = gmc == null ? "-" : $"{gmc.CurrentMode} {(gmc.IsEyesOpen ? "ตาเปิด" : "ตาปิด")}";

            var    heart = global::System.HeartbeatSystem.HeartbeatSystem.instance;
            string zone  = heart == null ? "ไม่มี HeartbeatSystem"
                         : !heart.IsEnforcing ? "กติกาเขต: ไม่มีผล"
                         : heart.OutsideSeconds > 0f
                             ? $"นอกเขต! {heart.OutsideSeconds:0.0}/{heart.OutsideDeathTime:0} วิ"
                             : $"เขต: {heart.CurrentZoneLevel}";

            var    tm    = TimeManager.instance;
            string clock = tm == null ? "ไม่เจอ TimeManager"
                         : $"{tm.currentHour:00}:{tm.currentMinute:00} {(tm.IsPaused ? "(หยุด)" : "(เดิน)")}";

            string ruleText = active == null
                ? "ไม่มีกฎทำงานอยู่"
                : active == (RuleBase)rule2
                    ? $"กฎ 2 — เฟส {rule2.CurrentPhase} " +
                      $"(ตู้ไฟ {rule2.FixedPanelCount}/{rule2.ActivePanelCount})"
                    : active.GetType().Name;

            return $"นาฬิกา {clock}  |  ผู้เล่น: {sitting}  |  {eyes}  |  {zone}  |  {ruleText}";
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
