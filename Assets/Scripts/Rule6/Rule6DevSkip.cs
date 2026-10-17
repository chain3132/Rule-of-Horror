using Manager;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rule6
{
    /// <summary>
    /// เครื่องมือ dev ของกฎข้อ 6 — เทสบทจบทั้ง 4 แบบได้จากปุ่มเดียว
    ///
    /// ปุ่มลัด (มีเฉพาะใน Editor / Development build / ไฟล์เทส):
    ///   Numpad 1-4   เข้ากฎ 6 แล้วเรียกรถคันนั้นมาจอดเลย — กดจากตรงไหนก็ได้ ไม่ต้องนั่งก่อน
    ///   F11          เข้ากฎ 6 เฉยๆ แล้วปล่อยให้คิวรถเดินตามปกติ 1 → 2 → 3 → 4
    ///   F12          เก็บรถที่จอดอยู่ แล้วเรียกคันถัดไป
    ///   Numpad 0     สลับ "อยู่ในคืนรอบสองแล้ว" เปิด/ปิด — ไว้เทสกติกาตายรอบเดียวจบ
    ///
    /// Numpad 1-4 ทำสามขั้นในปุ่มเดียว (ตั้งนาฬิกา → เริ่มกฎ → ข้ามการรอให้นั่ง → เรียกรถ)
    /// เพราะของเดิมต้องกด F11 แล้วเดินกลับไปนั่งที่ศาลา แล้วนั่งรอคิวจนถึงคันที่อยากเทส
    /// ซึ่งคันที่ต้องเทสซ้ำบ่อยสุดคือคันที่ 4 (วนลูป — ตัวเดียวที่เกมเดินต่อ) ที่อยู่ท้ายคิว
    ///
    /// กฎ 1-3 ใช้ RuleDevSkip (F1-F3), กฎ 4-5 ใช้ Rule4DevSkip / Rule5DevSkip (F4-F10)
    /// ตัวนี้จับแค่กฎ 6 จึงไม่ไปทับปุ่มใคร
    /// </summary>
    public class Rule6DevSkip : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("เว้นว่างได้ — จะไปหา component ในซีนเอง")]
        [SerializeField] private RuleSystem.Rule.Rule6 rule6;

        [Tooltip("ติ๊กออกก่อนส่งงาน: ปุ่มลัดทั้งหมดจะหยุดทำงาน")]
        [SerializeField] private bool enableDevSkip = true;

        [Header("Hotkeys")]
        [Tooltip("เข้ากฎ 6 แล้วปล่อยให้คิวรถเดินตามปกติ")]
        [SerializeField] private Key enterRuleKey = Key.F11;

        [Tooltip("เก็บรถที่จอดอยู่แล้วเรียกคันถัดไป")]
        [SerializeField] private Key nextBusKey = Key.F12;

        [Tooltip("สลับตัวนับคืนที่วนลูปมาแล้วระหว่าง 0 กับ 1 — เปิดแล้วตายในกฎ 1-5 จะจบเกมเลย\n" +
                 "ไว้เทสกติกาคืนรอบสองโดยไม่ต้องเล่นคืนแรกให้จบก่อน")]
        [SerializeField] private Key armPermanentDeathKey = Key.Numpad0;

        [Header("ตอนกดปุ่มเทส")]
        [Tooltip("✓ = พาผู้เล่นไปนั่งที่ศาลาก่อน (เหมือนเล่นจริง — รถจะรอแค่ตามเวลาแล้วผ่านไป)\n" +
                 "✗ = ปล่อยยืนอยู่ที่เดิม รถจะรอไม่จำกัดเพราะผู้เล่นไม่ได้นั่ง เดินไปขึ้นได้เลย")]
        [SerializeField] private bool sitPlayerFirst;

        [Header("On-Screen HUD")]
        [SerializeField] private bool  showOnScreenHelp = true;
        [SerializeField] private float statusDuration   = 6f;

        private string   _status = "";
        private float    _statusTime = -999f;
        private GUIStyle _hudStyle;

        // Numpad 1-4 → ช่องรถที่ 0-3 — เรียงเป็นตารางเพื่อไม่ต้องเขียน if ซ้ำสี่รอบ
        private static readonly Key[] BusKeys =
        {
            Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4
        };

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE_TEST_BUILD || RULE6_TEST_BUILD
            if (!enableDevSkip) return;
            if (Keyboard.current == null) return;

            if (Keyboard.current[enterRuleKey].wasPressedThisFrame) EnterRule6();
            if (Keyboard.current[nextBusKey].wasPressedThisFrame)   NextBus();
            if (Keyboard.current[armPermanentDeathKey].wasPressedThisFrame) TogglePermanentDeath();

            for (int i = 0; i < BusKeys.Length; i++)
                if (Keyboard.current[BusKeys[i]].wasPressedThisFrame) SummonBus(i);
#endif
        }

        private void Resolve()
        {
            if (rule6 == null)
                rule6 = FindAnyObjectByType<RuleSystem.Rule.Rule6>(FindObjectsInactive.Include);
        }

        // ─────────────────────────── Enter ───────────────────────────

        /// <summary>
        /// ตั้งนาฬิกาไปต้นหน้าต่างของกฎ 6 — SetTime ยิง OnTimeChanged เสมอแม้นาฬิกาหยุดอยู่
        /// RuleManager.CheckRules จึงเริ่มกฎให้เองเลย ต้องเช็ก ruleActive ก่อนเรียก StartRule ซ้ำ
        /// (เรียกซ้ำ = RuleFlow เดินสองรอบพร้อมกัน RuleBase.StartRule ไม่มี guard)
        /// </summary>
        [ContextMenu("Enter Rule 6")]
        public void EnterRule6() => EnterRule6(forcePlay: false);

        private bool EnterRule6(bool forcePlay)
        {
            Resolve();
            if (rule6 == null) { Notify("ไม่เจอ Rule6 ในซีน", true); return false; }
            if (TimeManager.instance == null) { Notify("ไม่เจอ TimeManager ในซีน", true); return false; }

            if (!rule6.ruleActive)
            {
                TimeManager.instance.IsPauseTime(true);
                TimeManager.instance.SetTime(rule6.startHour, rule6.startMinute);

                if (!rule6.ruleActive) rule6.StartRule();

                if (!rule6.ruleActive)
                {
                    Notify($"ตั้งเวลา {rule6.startHour:00}:{rule6.startMinute:00} แล้วแต่กฎ 6 ไม่เริ่ม — " +
                           "เช็กว่าอยู่ใน RuleManager.rules หรือเปล่า", true);
                    return false;
                }
            }

            if (!forcePlay)
            {
                Notify(rule6.IsGameplayActive
                    ? "กฎ 6 กำลังเล่นอยู่ — รอคิวรถตามปกติ"
                    : "เริ่มกฎ 6 แล้ว — ต้องนั่งที่ศาลาก่อนรถคันแรกจะมา (หรือกด Numpad 1-4 ข้ามไปเลย)");
                return true;
            }

            if (sitPlayerFirst) SitAtPavilion();

            if (!rule6.IsGameplayActive && !rule6.DebugForceStartGameplay())
            {
                Notify("สั่งเริ่มเล่นกฎ 6 ไม่สำเร็จ — ดู Console ว่า Rule6 ฟ้องช่องไหนว่าง", true);
                return false;
            }

            return true;
        }

        /// <summary>
        /// พาผู้เล่นไปนั่งที่ศาลาก่อนเริ่มเทส — ใช้ SittingPoint ที่มีอยู่ในซีนเป็นเป้า
        ///
        /// มีไว้เพื่อเทส "รถรอแล้วผ่านไป" ซึ่งเกิดเฉพาะตอนผู้เล่นนั่งอยู่ ปิดไว้เป็นค่าเริ่มต้น
        /// เพราะการเทสที่ทำบ่อยกว่าคือเดินไปขึ้นรถ ซึ่งต้องยืนอยู่แล้ว
        /// </summary>
        private void SitAtPavilion()
        {
            var pc = PlayerController.Instance;
            if (pc == null || pc.IsSitting()) return;

            var sit = FindAnyObjectByType<SittingPoint>(FindObjectsInactive.Include);
            if (sit == null)
            {
                Notify("ไม่เจอ SittingPoint ในซีน — เริ่มเทสจากท่ายืนแทน", true);
                return;
            }

            pc.isBlockStanding = false;
            pc.StartSittingSequence(sit.transform);
        }

        // ─────────────────────────── Buses ───────────────────────────

        /// <summary>ปุ่มเดียวจบ: เข้ากฎ → ข้ามการรอให้นั่ง → เรียกรถคันที่ต้องการมาจอด</summary>
        public void SummonBus(int index)
        {
            if (!EnterRule6(forcePlay: true)) return;

            var bus = rule6.BusAt(index);
            if (bus == null) { Notify($"ช่องรถที่ {index + 1} ว่างอยู่ — ใส่รถใน Rule6.Buses ก่อน", true); return; }

            if (rule6.DebugSummonBus(index))
                Notify($"เรียกรถช่องที่ {index + 1} — '{bus.name}' ({bus.Kind}) มาจอด " +
                       $"{(bus.Seat == null ? "! คันนี้ยังไม่ได้ใส่ Seat ขึ้นไม่ได้" : "เดินขึ้นรถแล้วกด E ที่เบาะ")}",
                       bus.Seat == null);
            else
                Notify($"เรียกรถช่องที่ {index + 1} ไม่ได้ — ขึ้นรถไปแล้ว บทจบกำลังเล่น", true);
        }

        public void NextBus()
        {
            Resolve();
            if (rule6 == null || !rule6.IsGameplayActive)
            {
                Notify($"กฎ 6 ยังไม่เริ่มเล่น — กด Numpad 1-4 หรือ {enterRuleKey} ก่อน", true);
                return;
            }

            var current = rule6.CurrentBus;
            if (current == null) { Notify("ถนนว่างอยู่ รอคันถัดไปอยู่แล้ว", true); return; }

            // หาว่าคันที่จอดอยู่คือช่องไหน แล้วเรียกช่องถัดไป — DebugSummonBus เป็นคนเก็บรถ
            // คันเดิมกับเลื่อน index ให้เอง
            for (int i = 0; i < rule6.BusCount; i++)
            {
                if (rule6.BusAt(i) != current) continue;

                SummonBus((i + 1) % rule6.BusCount);
                return;
            }

            Notify("คันที่จอดอยู่ไม่อยู่ในช่อง Buses — เรียกคันถัดไปไม่ได้", true);
        }

        // ─────────────────────────── คืนรอบสอง ───────────────────────────

        /// <summary>
        /// สลับว่า "ตอนนี้อยู่ในคืนรอบสองแล้ว" หรือยัง
        ///
        /// เปิดแล้วไปตายในกฎข้อไหนก็ได้ 1-5 เพื่อเช็กว่าจอตายถาวรขึ้น เวลาหยุด และกฎ "ไม่"
        /// ย้อนเวลากลับมาให้เล่นใหม่ — ไม่ต้องเล่นคืนแรกให้จบแล้วขึ้นรถคันที่ 4 ก่อน
        /// </summary>
        [ContextMenu("Toggle Second-Loop Night")]
        public void TogglePermanentDeath()
        {
            bool armed = RuleSystem.Rule.Rule6.NightLoopCount > 0;
            RuleSystem.Rule.Rule6.DebugSetNightLoopCount(armed ? 0 : 1);

            var gate = RuleSystem.PermanentDeath.instance;

            if (gate == null)
            {
                Notify("ไม่เจอ PermanentDeath ในซีน — ตายยังไงก็ยัง rewind กลับมาเล่นใหม่ " +
                       "(รัน Build Bus Stop จะใส่ให้)", true);
                return;
            }

            Notify(gate.IsArmed
                ? "เปิดคืนรอบสองแล้ว — ตายในกฎ 1-5 ครั้งเดียวจบเกม ไม่ย้อนเวลา"
                : "ปิดคืนรอบสอง — ตายแล้วย้อนเวลากลับมาเล่นใหม่ตามปกติ");
        }

        // ─────────────────────────── HUD ───────────────────────────

        private void Notify(string message, bool warn = false)
        {
            _status     = message;
            _statusTime = Time.unscaledTime;

            if (warn) Debug.LogWarning($"[Rule6DevSkip] {message}", this);
            else      Debug.Log($"[Rule6DevSkip] {message}", this);
        }

        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RULE_TEST_BUILD || RULE6_TEST_BUILD
            if (!enableDevSkip || !showOnScreenHelp) return;

            if (_hudStyle == null)
                _hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.UpperLeft };

            string text =
                "[RULE 6]  Numpad 1 กับดัก  |  2 ทางรอด  |  3 ความตาย  |  4 วนลูป   " +
                $"({enterRuleKey} เข้ากฎเฉยๆ, {nextBusKey} คันถัดไป, " +
                $"{armPermanentDeathKey} สลับคืนรอบสอง)\n" + StateLine();

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime - _statusTime < statusDuration)
                text += "\n" + _status;

            // ขวาล่าง — ซ้ายล่างเป็นที่ของ RuleDevSkip ซ้ายบนเป็นของ Rule4/Rule5DevSkip
            var rect = new Rect(Screen.width * 0.5f - 14f, Screen.height - 110f,
                                Screen.width * 0.5f, 100f);

            var color = rule6 != null && rule6.ruleActive ? new Color(1f, 0.85f, 0.5f) : Color.white;

            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _hudStyle);
            GUI.color = color;
            GUI.Label(rect, text, _hudStyle);
            GUI.color = Color.white;
#endif
        }

        /// <summary>
        /// บรรทัดสถานะ — สิ่งที่ต้องรู้ตอนกดปุ่มลัดแล้ว "ไม่มีอะไรเกิดขึ้น"
        ///
        /// เคสที่เจอบ่อยสุดสองอย่างคือรถยังไม่ได้ใส่ Seat (ขึ้นไม่ได้แต่ไม่มีอะไรบอก) และ
        /// กฎยังรอให้นั่งอยู่ ทั้งสองอย่างจึงต้องอยู่ในบรรทัดนี้
        /// </summary>
        private string StateLine()
        {
            var    pc      = PlayerController.Instance;
            string sitting = pc == null ? "ไม่เจอ PlayerController"
                           : pc.IsSitting() ? "นั่งอยู่" : "ยืนอยู่";

            if (rule6 == null) return $"ไม่เจอ Rule6  |  ผู้เล่น: {sitting}";

            string bus = rule6.CurrentBus == null
                       ? "ถนนว่าง"
                       : $"จอดอยู่: {rule6.CurrentBus.Kind}" +
                         (rule6.CurrentBus.Seat == null ? " (ยังไม่ได้ใส่ Seat!)" : "");

            string phase = rule6.Resolution.HasValue ? $"บทจบ {rule6.Resolution.Value}"
                         : rule6.IsGameplayActive    ? "กำลังเลือกรถ"
                         : rule6.ruleActive          ? "รอให้นั่งที่ศาลา"
                                                     : "ยังไม่เริ่ม";

            var    gate  = RuleSystem.PermanentDeath.instance;
            string death = gate == null ? "ไม่มี PermanentDeath ในซีน"
                         : gate.IsArmed ? "ตายรอบเดียวจบ"
                                        : "ตายแล้วเล่นใหม่ได้";

            return $"{phase}  |  {bus}  |  ผู้เล่น: {sitting}  |  " +
                   $"วนลูป {RuleSystem.Rule.Rule6.NightLoopCount} รอบ ({death})";
        }
    }
}
