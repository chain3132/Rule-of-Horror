using Manager;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rule5
{
    /// <summary>
    /// เครื่องมือ dev สำหรับเทส Rule 5 โดยไม่ต้องเล่นกฎ 1-4 ให้จบก่อน
    ///
    /// วิธีใช้:
    ///   1. แปะ component นี้ไว้บน GameObject ไหนก็ได้ใน scene แล้วลาก Rule5 ใส่ช่อง rule5
    ///   2. กด Play → กด Spacebar ข้าม intro → รอให้ตัวละครนั่งลงเสร็จ
    ///   3. กด F6 → เข้า Rule 5 ทันที
    ///
    /// ปุ่มลัดระหว่างเล่น (มีเฉพาะใน Editor / Development build / ไฟล์เทสที่ build จากเมนู):
    ///   F6  เข้า Rule 5                     F7  เปิด/ปิดป้าย debug (เส้นสาย / ผ้าแดง / ผี)
    ///   F8  ระฆังดัง +1 ทันที                F9  เปิด/ปิดเสียงฉิ่งด้วยมือ
    ///   F10 วาร์ปไปยืนที่จุดจับสาย
    ///
    /// ทำไมถึงไม่ชนกับกฎอื่น: เหตุผลเดียวกับ Rule4DevSkip —
    /// Rule5.RuleFlow() สั่ง IsPauseTime(true) เป็นบรรทัดแรก นาฬิกาเลยหยุดเดิน
    /// RuleManager.CheckRules ทำงานตอน OnTimeChanged เท่านั้น กฎอื่นจึงไม่มีทาง trigger แทรก
    /// </summary>
    public class Rule5DevSkip : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("Drag in the GameObject that has the Rule5 component.")]
        [SerializeField] private RuleSystem.Rule.Rule5 rule5;

        [Tooltip("Untick before shipping: all dev hotkeys stop working.")]
        [SerializeField] private bool enableDevSkip = true;

        [Header("Hotkeys")]
        [SerializeField] private Key jumpKey        = Key.F6;
        [SerializeField] private Key toggleDebugKey = Key.F7;
        [SerializeField] private Key ringBellKey    = Key.F8;
        [SerializeField] private Key toggleChingKey = Key.F9;
        [SerializeField] private Key teleportKey    = Key.F10;

        [Header("Clock")]
        [Tooltip("Freeze the clock as soon as the game starts, so another rule cannot trigger while you are still looking for the hotkey.")]
        [SerializeField] private bool freezeClockOnStart = true;

        [Tooltip("Clock time set before starting Rule 5. Should sit just before the rule's own time window.")]
        [SerializeField] private int jumpHour   = 22;
        [SerializeField] private int jumpMinute = 59;

        [Header("Debug")]
        [Tooltip("Show the thread, the grab point and the ghost position.\n" +
                 "In the Editor these are drawn as lines in the Scene view (Gizmos must be on).\n" +
                 "In a build they are drawn as on-screen labels instead.")]
        [SerializeField] private bool showDebugMarkers;

        [Header("On-Screen HUD")]
        [Tooltip("Show the hotkeys and live rule state on screen. Needed in a build, where a tester has no Console to read.")]
        [SerializeField] private bool showOnScreenHelp = true;

        [SerializeField] private float statusDuration = 6f;

        private string   _status = "";
        private float    _statusTime = -999f;
        private GUIStyle _hudStyle;
        private GUIStyle _markerStyle;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE5_TEST_BUILD
            if (!enableDevSkip) return;

            if (rule5 == null)
            {
                Notify("ยังไม่ได้ลาก Rule5 ใส่ช่อง rule5 ใน Inspector", true);
                return;
            }

            if (freezeClockOnStart && TimeManager.instance != null)
            {
                TimeManager.instance.IsPauseTime(true);
                Notify($"หยุดนาฬิกาไว้แล้ว — กด {jumpKey} เพื่อเข้า Rule 5");
            }
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE5_TEST_BUILD
            if (!enableDevSkip) return;

            // ย้ำหยุดนาฬิกาทุกเฟรมจนกว่าจะเข้า Rule 5 — CutsceneManager / BlinkRoutine
            // สั่ง IsPauseTime(false) ทีหลังเสมอ พอเข้ากฎแล้ว Rule5 เป็นคนคุมเอง
            if (freezeClockOnStart && rule5 != null && !rule5.ruleActive && TimeManager.instance != null)
                TimeManager.instance.IsPauseTime(true);

            if (Keyboard.current == null) return;

            if (Keyboard.current[jumpKey].wasPressedThisFrame) JumpToRule5();

            if (Keyboard.current[toggleDebugKey].wasPressedThisFrame)
            {
                showDebugMarkers = !showDebugMarkers;
                Notify($"ป้าย debug: {(showDebugMarkers ? "เปิด" : "ปิด")}");
            }

            if (Keyboard.current[ringBellKey].wasPressedThisFrame)    RingBell();
            if (Keyboard.current[toggleChingKey].wasPressedThisFrame) ToggleChing();
            if (Keyboard.current[teleportKey].wasPressedThisFrame)    TeleportToThread();

            if (showDebugMarkers) DrawDebugLines();
#endif
        }

        // ─────────────────────────── Actions ───────────────────────────

        /// <summary>เริ่ม Rule 5 ทันที</summary>
        [ContextMenu("Jump To Rule 5")]
        public void JumpToRule5()
        {
            if (rule5 == null)
            {
                Notify("ยังไม่ได้ลาก Rule5 ใส่ช่อง rule5 ใน Inspector", true);
                return;
            }

            if (rule5.ruleActive)
            {
                Notify("Rule 5 ทำงานอยู่แล้ว — ไม่ต้องกดซ้ำ", true);
                return;
            }

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

            rule5.StartRule();

            if (!pc.IsSitting())
                // RuleFlow มี WaitUntil(PlayerIsSitting) อยู่แล้ว — สั่งไปเลย เดี๋ยวมันรอเอง
                Notify("สั่งเริ่ม Rule 5 แล้ว — แต่ยังไม่ได้นั่ง เดินไปที่ศาลาแล้วกด E เพื่อให้กฎเริ่มจริง");
            else
                Notify("เริ่ม Rule 5 แล้ว");
        }

        private void RingBell()
        {
            if (!RuleReady()) return;
            rule5.DebugRingBell();
            Notify($"ระฆัง {rule5.BellCount}/{rule5.RequiredBells}");
        }

        private void ToggleChing()
        {
            if (!RuleReady()) return;
            rule5.DebugToggleChing();
            Notify($"ฉิ่ง: {(rule5.IsChingActive ? "ดังอยู่ (ห้ามเดิน)" : "เงียบ")}");
        }

        /// <summary>วาร์ปไปยืนที่จุดที่ต้องกด E จับสาย — ข้ามการเดินหาผ้าแดง</summary>
        private void TeleportToThread()
        {
            if (!RuleReady()) return;

            var walker = rule5.Walker;
            if (walker == null || walker.Thread == null)
            {
                Notify("ยังไม่ได้ใส่ walker / thread ใน Inspector", true);
                return;
            }
            if (walker.IsHolding)
            {
                Notify("กำลังจับสายอยู่ — ปล่อยมือก่อน (E)", true);
                return;
            }

            var pc = PlayerController.Instance;
            if (pc == null) return;

            // ยืนเยื้องออกจากเส้นเล็กน้อย ไม่ให้ไปโผล่ทับตัวสาย
            float   d      = walker.GrabPointDistance;
            Vector3 target = walker.Thread.GetGroundPoint(d) + walker.Thread.GetRight(d) * 0.6f;

            // ต้องปิด CharacterController ก่อนเขียน position ตรงๆ ไม่งั้นมันดีดกลับที่เดิม
            var cc = pc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pc.transform.position = target + Vector3.up * 0.1f;
            if (cc != null) cc.enabled = true;

            Notify($"วาร์ปมาที่จุดจับสาย ({d:0.0} ม. จากต้นสาย) — กด E เพื่อจับ");
        }

        private bool RuleReady()
        {
            if (rule5 == null)
            {
                Notify("ยังไม่ได้ลาก Rule5 ใส่ช่อง rule5 ใน Inspector", true);
                return false;
            }
            if (!rule5.IsGameplayActive)
            {
                Notify($"กฎยังไม่เริ่มเล่นจริง — กด {jumpKey} แล้วรอให้ตาเปิดก่อน", true);
                return false;
            }
            return true;
        }

        // ─────────────────────────── HUD ───────────────────────────

        private void Notify(string message, bool isWarning = false)
        {
            if (isWarning) Debug.LogWarning($"[Rule5DevSkip] {message}", this);
            else           Debug.Log($"[Rule5DevSkip] {message}", this);

            _status     = message;
            _statusTime = Time.unscaledTime;   // unscaled เพราะบางจังหวะ timeScale ถูกหยุด
        }

        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE5_TEST_BUILD
            if (!enableDevSkip || !showOnScreenHelp) return;

            EnsureStyles();

            bool playing = rule5 != null && rule5.IsGameplayActive;

            string text = playing
                ? $"[RULE 5 TEST]  {ringBellKey} ระฆัง+1  |  {toggleChingKey} ฉิ่ง  |  " +
                  $"{teleportKey} วาร์ปไปจุดจับสาย  |  {toggleDebugKey} ป้าย debug\n{StateLine()}"
                : $"[RULE 5 TEST]  {jumpKey} = เข้า Rule 5 ทันที  |  {toggleDebugKey} = ป้าย debug";

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime - _statusTime < statusDuration)
                text += "\n" + _status;

            DrawShadowedLabel(new Rect(14f, 14f, Screen.width - 28f, 90f),
                              text, _hudStyle, playing ? new Color(1f, 0.82f, 0.35f) : Color.white);

            if (showDebugMarkers) DrawDebugMarkersOnScreen();
#endif
        }

        /// <summary>บรรทัดสถานะกฎ — สิ่งที่ต้องรู้ตอนเทสว่าตรรกะทำงานถูกไหม</summary>
        private string StateLine()
        {
            var walker = rule5.Walker;
            string hold = walker == null ? "ไม่มี walker"
                        : walker.IsHolding ? (walker.IsWalking ? "จับ+เดิน" : "จับ (หยุด)")
                        : walker.EverGrabbed ? "ปล่อยมือ" : "ยังไม่เคยจับ";

            var    g     = rule5.ActiveGhost;
            string ghost = g == null ? "-"
                         : g.Mode == Rule5GhostMode.Escort
                            ? $"Escort {g.EscortDistance:0.0} ม. ({g.EscortCloseness01 * 100f:0}%)"
                            : g.Mode.ToString();
            float  dist  = walker != null ? walker.Distance : 0f;

            var spawner = rule5.ObstacleSpawner;
            string obs  = walker == null ? "-"
                        : spawner != null ? $"{walker.BlockingObstacleCount} กั้นอยู่ / รอคิว {spawner.PendingCount}"
                        : $"{walker.BlockingObstacleCount} กั้นอยู่";

            return $"ระฆัง {rule5.BellCount}/{rule5.RequiredBells}  |  " +
                   $"สวด {(rule5.HasPrayed ? "แล้ว" : "ยัง")}  |  " +
                   $"ฉิ่ง {(rule5.IsChingActive ? "ดัง!" : "เงียบ")}  |  " +
                   $"สาย: {hold} @ {dist:0.0} ม.  |  ผี: {ghost}  |  ขวาง: {obs}";
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

        // ─────────────────────────── Debug draw ───────────────────────────

        /// <summary>วาดเส้นสาย + จุดที่ต้องจับ + ตำแหน่งผี (Scene view เท่านั้น)</summary>
        private void DrawDebugLines()
        {
            if (rule5 == null) return;
            var pc = PlayerController.Instance;
            if (pc == null) return;

            Vector3 from   = pc.transform.position + Vector3.up;
            var     walker = rule5.Walker;

            if (walker != null && walker.Thread != null)
            {
                var thread = walker.Thread;
                float len  = thread.TotalLength;

                // เส้นสายเป็นช่วงๆ ทุก 1 ม. — Debug.DrawLine ไม่มี LineRenderer ให้พึ่งใน play mode
                for (float d = 0f; d < len; d += 1f)
                    Debug.DrawLine(thread.GetPoint(d), thread.GetPoint(Mathf.Min(d + 1f, len)), Color.yellow);

                if (!walker.IsHolding)
                    Debug.DrawLine(from, thread.GetGroundPoint(walker.GrabPointDistance), Color.green);
            }

            var ghost = rule5.ActiveGhost;
            if (ghost != null) Debug.DrawLine(from, ghost.transform.position, Color.magenta);
        }

        /// <summary>
        /// ป้ายบนหน้าจอ — Debug.DrawLine เห็นได้แค่ใน Scene view ของ Editor
        /// ในไฟล์ build ป้ายพวกนี้เป็นทางเดียวที่คนเทสจะรู้ว่าจุดจับสาย/ผีอยู่ทางไหน
        /// </summary>
        private void DrawDebugMarkersOnScreen()
        {
            var cam = Camera.main;
            if (cam == null || rule5 == null) return;

            var walker = rule5.Walker;
            if (walker != null && walker.Thread != null && !walker.IsHolding)
                DrawWorldMarker(cam, walker.Thread.GetGroundPoint(walker.GrabPointDistance),
                                Color.green, "จุดจับสาย");

            var ghost = rule5.ActiveGhost;
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
            DrawShadowedLabel(new Rect(x - 90f, y - 10f, 180f, 20f),
                              behind ? $"↓ {label} {dist:0} m" : $"◆ {label} {dist:0} m",
                              _markerStyle, color);
        }
    }
}
