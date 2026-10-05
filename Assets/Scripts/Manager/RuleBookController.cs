using System;
using System.Collections.Generic;
using Enum;
using InputSystem;
using ScriptableObject;
using UnityEngine;

namespace Manager
{
    /// <summary>
    /// สมองของ "แอปกฎ" (ปุ่ม 3) — เก็บว่าผู้เล่นรู้อะไรบ้าง และตอนนี้เปิดดูกฎข้อไหนอยู่
    ///
    /// ตัวนี้ไม่แตะ UI เลยสักบรรทัด มันแค่บอกว่า "ตอนนี้ควรแสดงอะไร" ผ่าน property + event
    /// ฝั่ง UI subscribe OnRuleBookChanged แล้วไปอ่าน CurrentView เอา
    ///
    /// กฎถูกบันทึกจากแชท ไม่ใช่จากเวลา — ผู้เล่นที่ไม่เปิดอ่านแชทจะไม่มีกฎในแอป
    /// ซึ่งตั้งใจให้เป็นแบบนั้น ความรู้ทั้งหมดในเกมนี้มาจากคนปริศนาคนเดียว
    /// </summary>
    public class RuleBookController : MonoBehaviour
    {
        // ─────────────────────────── Inner Types ───────────────────────────

        /// <summary>สิ่งที่ผู้เล่นรู้เกี่ยวกับกฎข้อหนึ่ง ณ ตอนนี้</summary>
        public readonly struct RuleView
        {
            public readonly RuleEntryData  Rule;
            public readonly bool           Unlocked;
            public readonly bool           KnowsSchedule;   // false = ตอบหัวแข็ง เวลาเป็น --:--
            public readonly IReadOnlyList<string> Facts;    // เติม "—" ให้ครบ factSlots แล้ว

            public RuleView(RuleEntryData rule, bool unlocked, bool knowsSchedule, IReadOnlyList<string> facts)
            {
                Rule = rule; Unlocked = unlocked; KnowsSchedule = knowsSchedule; Facts = facts;
            }

            public string ScheduleText => !Unlocked || !KnowsSchedule ? UnknownSchedule : Rule.ScheduleText;
            public IReadOnlyList<RuleKeyHint> Hands => Rule != null ? Rule.hands : System.Array.Empty<RuleKeyHint>();
        }

        /// <summary>สิ่งที่ถูกบันทึกไว้จริงตอนแชทจบ — ไม่เปลี่ยนอีกหลังจากนั้น</summary>
        private class Entry
        {
            public bool knowsSchedule;
        }

        public const string UnknownSchedule = "--:-- – --:--";
        public const string EmptyFact       = "—";
        public const string LockedNumeral   = "·";

        // ─────────────────────────── Inspector ───────────────────────────

        [Header("กฎทั้งหมดในเกม")]
        [Tooltip("ใส่เรียงตามลำดับที่อยากให้โชว์บนแถบเลขไทยด้านบน (๑ ๒ ๓ ๔ ๕) — ข้อที่ยังไม่ได้บันทึกจะเป็น ·")]
        [SerializeField] private List<RuleEntryData> rules = new List<RuleEntryData>();

        [Header("ต่อกับระบบอื่น")]
        [Tooltip("ตัวเดินบทสนทนา — แอปกฎดักจาก node ที่ติดธง recordsRule ไว้")]
        [SerializeField] private ConversationRunner runner;

        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;
        [SerializeField] private InputHandler inputHandler;

        [Header("พฤติกรรม")]
        [Tooltip("✓ = พอบันทึกกฎใหม่ ให้เด้งไปหน้ากฎข้อนั้นเลย ผู้เล่นจะได้ไม่ต้องกด A/D หา")]
        [SerializeField] private bool jumpToNewestOnRecord = true;

        [Tooltip("✓ = กด A/D วนกลับหัวท้ายได้ / ✗ = ชนขอบแล้วหยุด")]
        [SerializeField] private bool wrapNavigation;

        // ─────────────────────────── Events ───────────────────────────

        /// <summary>มีกฎใหม่ถูกบันทึก หรือข้อมูลของกฎเปลี่ยน — UI วาดใหม่ทั้งหน้าได้เลย</summary>
        public event Action OnRuleBookChanged;

        /// <summary>ผู้เล่นเปลี่ยนหน้าไปกฎข้ออื่น (int = index ใน rules)</summary>
        public event Action<int> OnSelectedRuleChanged;

        /// <summary>กฎที่เพิ่งถูกบันทึกเดี๋ยวนั้น — ใช้ยิง badge "มีกฎใหม่" บนไอคอนแอป</summary>
        public event Action<RuleEntryData> OnRuleRecorded;

        // ─────────────────────────── Runtime ───────────────────────────

        private readonly Dictionary<RuleEntryData, Entry> _records = new Dictionary<RuleEntryData, Entry>();
        private int _selected;

        // ─────────────────────────── Public API ───────────────────────────

        public IReadOnlyList<RuleEntryData> Rules => rules;
        public int  SelectedIndex   => _selected;
        public int  UnlockedCount   => _records.Count;
        public bool HasAnyUnlocked  => _records.Count > 0;

        /// <summary>ข้อมูลพร้อมแสดงของกฎที่เปิดอยู่ — UI อ่านตัวนี้ตัวเดียวก็วาดได้ทั้งหน้า</summary>
        public RuleView CurrentView => ViewOf(_selected);

        /// <summary>แถบบนสุด "๑ ๒ · · ·" — ข้อที่ยังไม่รู้เป็นจุด ไม่ใช่เลข เพื่อให้เห็นว่ายังมีอีก</summary>
        public string NumeralStrip()
        {
            var sb = new System.Text.StringBuilder(rules.Count * 2);
            for (int i = 0; i < rules.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(rules[i] != null && _records.ContainsKey(rules[i]) ? rules[i].ThaiNumeral : LockedNumeral);
            }
            return sb.ToString();
        }

        public RuleView ViewOf(int index)
        {
            if (index < 0 || index >= rules.Count || rules[index] == null) return default;
            return ViewOf(rules[index]);
        }

        public RuleView ViewOf(RuleEntryData rule)
        {
            if (rule == null) return default;

            bool unlocked = _records.TryGetValue(rule, out var rec);
            bool knows    = unlocked && rec.knowsSchedule;

            var source = !unlocked ? System.Array.Empty<string>()
                       : knows     ? rule.factsIfCooperative
                                   : rule.factsIfDefiant;

            // เติม "—" จนครบจำนวนช่อง — ช่องว่างคือข้อมูลอย่างหนึ่ง มันบอกว่ายังมีที่ไม่รู้
            int   slots = Mathf.Max(rule.factSlots, source?.Length ?? 0);
            var   facts = new string[slots];
            for (int i = 0; i < slots; i++)
                facts[i] = source != null && i < source.Length && !string.IsNullOrWhiteSpace(source[i])
                         ? source[i] : EmptyFact;

            return new RuleView(rule, unlocked, knows, facts);
        }

        public bool IsUnlocked(RuleEntryData rule) => rule != null && _records.ContainsKey(rule);
        public bool IsUnlocked(int index) => index >= 0 && index < rules.Count && IsUnlocked(rules[index]);

        /// <summary>
        /// บันทึกกฎลงแอป — เรียกเองได้ แต่ปกติมาจาก node ในแชทที่ติดธง recordsRule ไว้
        /// เรียกซ้ำไม่มีผล กฎที่รู้แล้วจะไม่ถูกลดข้อมูลลงจากการเล่นแชทซ้ำ
        /// </summary>
        public void Record(RuleEntryData rule, bool cooperative)
        {
            if (rule == null || _records.ContainsKey(rule)) return;

            _records[rule] = new Entry { knowsSchedule = cooperative };

            int index = rules.IndexOf(rule);
            if (index < 0)
            {
                // กฎที่ไม่ได้อยู่ในลิสต์ยังบันทึกได้ แต่จะไม่มีที่ยืนบนแถบเลขไทย เตือนไว้ดีกว่าเงียบ
                Debug.LogWarning($"[RuleBook] บันทึก '{rule.name}' แล้วแต่ไม่ได้อยู่ในลิสต์ rules — " +
                                 "ใส่ใน Inspector ด้วยไม่งั้นผู้เล่นเปิดดูไม่ได้", this);
            }
            else if (jumpToNewestOnRecord)
            {
                _selected = index;
                OnSelectedRuleChanged?.Invoke(_selected);
            }

            Debug.Log($"[RuleBook] บันทึก '{rule.title}' ({(cooperative ? "รู้ครบ" : "รู้ไม่ครบ")})", this);
            OnRuleRecorded?.Invoke(rule);
            OnRuleBookChanged?.Invoke();
        }

        public void Select(int index)
        {
            if (rules.Count == 0) return;

            index = wrapNavigation
                  ? ((index % rules.Count) + rules.Count) % rules.Count
                  : Mathf.Clamp(index, 0, rules.Count - 1);

            if (index == _selected) return;

            _selected = index;
            OnSelectedRuleChanged?.Invoke(_selected);
        }

        public void SelectNext()     => Select(_selected + 1);
        public void SelectPrevious() => Select(_selected - 1);

        /// <summary>เริ่มเกมใหม่ / โหลดเซฟ — ล้างความรู้ทั้งหมดทิ้ง</summary>
        public void ResetAll()
        {
            _records.Clear();
            _selected = 0;
            OnRuleBookChanged?.Invoke();
        }

        // ─────────────────────────── Debug ───────────────────────────

        /// <summary>
        /// บันทึกกฎทุกข้อทันทีโดยไม่ต้องอ่านแชท — ใช้แยกว่าปัญหาอยู่ฝั่งไหน
        ///
        /// กดแล้วกฎขึ้นในแอป  = ฝั่ง UI ปกติดี ปัญหาอยู่ที่บทสนทนายังไม่ได้เล่น node ที่ติดธงไว้
        /// กดแล้วยังว่างเหมือนเดิม = ปัญหาอยู่ที่ RuleBookPanelBinder หรือ UI ที่ต่อไว้
        ///
        /// คลิกขวาที่หัว component ตอน Play อยู่
        /// </summary>
        [ContextMenu("ทดสอบ — บันทึกกฎทั้งหมด (แบบรู้ครบ)")]
        private void DebugRecordAll()
        {
            foreach (var rule in rules) Record(rule, true);

            Debug.Log($"[RuleBook] บันทึกครบ {UnlockedCount}/{rules.Count} ข้อ — แถบเลข: {NumeralStrip()}\n" +
                      $"กำลังเปิดดูข้อ {_selected + 1}: " +
                      $"{(CurrentView.Rule != null ? CurrentView.Rule.title : "(ไม่มี)")}", this);
        }

        [ContextMenu("ทดสอบ — ล้างกฎที่บันทึกไว้")]
        private void DebugClear()
        {
            ResetAll();
            Debug.Log("[RuleBook] ล้างกฎที่บันทึกไว้แล้ว", this);
        }

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void OnEnable()
        {
            if (runner == null)
            {
                // ไม่มี runner = ไม่มีใครบอกว่ากฎถูกบันทึกเมื่อไร แอปจะว่างตลอดเกมแบบเงียบๆ
                Debug.LogError("[RuleBook] ยังไม่ได้ใส่ ConversationRunner — กฎจะไม่ถูกบันทึกจากแชทเลย", this);
                return;
            }

            runner.OnNodeDisplayed += HandleNodeDisplayed;
        }

        private void OnDisable()
        {
            if (runner != null) runner.OnNodeDisplayed -= HandleNodeDisplayed;
        }

        private void Update()
        {
            // A/D พลิกหน้ากฎ — รับเฉพาะตอนเปิดแอปกฎอยู่ ไม่งั้นไปชนกับการเดิน
            if (inputHandler == null || phoneSystem == null) return;
            if (phoneSystem.CurrentState != PhoneState.RuleBook) return;

            if (inputHandler.WasRulePrevPressed()) SelectPrevious();
            if (inputHandler.WasRuleNextPressed()) SelectNext();
        }

        private void HandleNodeDisplayed(ChatNode node)
        {
            if (node?.recordsRule != null) Record(node.recordsRule, node.recordsAsCooperative);
        }
    }
}
