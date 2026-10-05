using System.Collections.Generic;
using Enum;
using UnityEngine;

namespace Manager
{
    /// <summary>
    /// แถบคำใบ้ปุ่มมุมซ้ายล่าง — บอกว่า "ตอนนี้กดอะไรได้บ้าง" แล้วเปลี่ยนตามสถานการณ์
    ///
    ///   เก็บโทรศัพท์   [Tab] เปิดโทรศัพท์
    ///   หน้าโฮม        [1][2][3] เปิดแอป   [Tab] เก็บโทรศัพท์
    ///   หน้ารายชื่อ    [W][S] เลือก   [Enter] เปิดแชท   [Tab] เก็บโทรศัพท์
    ///   มีคำตอบให้เลือก [1][2] เลือกคำตอบ   [Scroll] เลื่อนแชท   [Tab] เก็บโทรศัพท์
    ///   แอปกฎ          [A][D] ดูกฎข้อก่อน   [1] กลับไปแชท   [Tab] เก็บโทรศัพท์
    ///
    /// ต่างจาก KeyPromptManager ตรงที่ตัวนั้นผูกกับ "วัตถุในโลก" แล้วลอยอยู่ข้างวัตถุนั้น
    /// ส่วนตัวนี้ผูกกับ "สถานะของระบบ" และอยู่มุมจอตายตัว สองอันทำงานพร้อมกันได้
    /// (ยืนหน้าสายสิญจน์ตอนเก็บโทรศัพท์ = เห็นทั้ง prompt ลอยข้างสาย และแถบมุมซ้ายล่าง)
    ///
    /// ตัวนี้อ่านสถานะเอง ไม่ต้องให้ใครมาเรียก — ระบบอื่นเลยไม่ต้องรู้จักมันเลย
    /// ยกเว้นตอนกฎอยากยึดแถบไปใช้ชั่วคราว ให้เรียก Push()/Pop()
    /// </summary>
    public class ContextPromptBar : MonoBehaviour
    {
        // ─────────────────────────── Inner Types ───────────────────────────

        [System.Serializable]
        public class ContextHints
        {
            public PromptContext context;

            [Tooltip("บรรทัดที่จะโชว์ตอนอยู่ในสถานการณ์นี้ — key ใส่หลายปุ่มได้โดยคั่นด้วย | เช่น 1|2|3")]
            public RuleKeyHint[] hints;
        }

        private class Override
        {
            public string id;
            public RuleKeyHint[] hints;
            public bool replaceAll;
        }

        // ─────────────────────────── Inspector ───────────────────────────

        [Header("UI")]
        [Tooltip("ที่ให้แถวไปเกิด — ควรมี HorizontalLayoutGroup อยู่แล้ว")]
        [SerializeField] private Transform rowRoot;

        [Tooltip("prefab ของคำใบ้ 1 บรรทัด — ควรมี KeyHintRow (รองรับหลายปุ่ม) หรืออย่างน้อย KeyCapView")]
        [SerializeField] private GameObject rowPrefab;

        [Tooltip("ซ่อนทั้งแถบตอนไม่มีอะไรจะบอก แทนที่จะปล่อยกรอบเปล่าค้างไว้")]
        [SerializeField] private GameObject barRoot;

        [Header("ต่อกับระบบอื่น")]
        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;

        [Tooltip("ใช้ดูว่ามีตัวเลือกคำตอบค้างอยู่ไหม — เว้นว่างได้ แค่จะไม่มีสถานะ ChatReply")]
        [SerializeField] private ChatUIController chatUI;

        [Header("คำใบ้ของแต่ละสถานการณ์")]
        [Tooltip("ไม่ได้ใส่สถานการณ์ไหนไว้ = แถบจะว่างตอนอยู่ในสถานการณ์นั้น")]
        [SerializeField] private List<ContextHints> contexts = new List<ContextHints>();

        [Header("พฤติกรรม")]
        [Tooltip("✓ = ซ่อนแถบไปเลยตอนไม่มีคำใบ้")]
        [SerializeField] private bool hideWhenEmpty = true;

        [Tooltip("✓ = ไม่โผล่ระหว่างฉากเปิด รอจนคืนการควบคุมให้ผู้เล่นก่อน\n" +
                 "ช่วงนั้นผู้เล่นกดอะไรไม่ได้อยู่แล้ว บอกปุ่มไปก็มีแต่จะเกะกะภาพ")]
        [SerializeField] private bool hideDuringCutscene = true;

        // ─────────────────────────── Runtime ───────────────────────────

        private readonly List<GameObject> _rows      = new List<GameObject>();
        private readonly List<Override>   _overrides = new List<Override>();

        private PromptContext _shown;
        private bool          _hasShown;
        private bool          _dirty = true;
        private bool          _blocked;
        private CanvasGroup   _selfGroup;

        /// <summary>สถานการณ์ที่ระบบตีความได้ตอนนี้ — ไม่นับ override</summary>
        public PromptContext CurrentContext => Resolve();

        // ─────────────────────────── Public API ───────────────────────────

        /// <summary>
        /// ยึดแถบไปใช้ชั่วคราว — ใช้ตอนกฎพาผู้เล่นเข้าสถานการณ์ที่ไม่เกี่ยวกับโทรศัพท์
        /// เช่น Rule 5 ตอนจับสายสิญจน์เดิน: [W] เดิน  [E] ปล่อยมือ
        ///
        /// id เดิมที่เรียกซ้ำจะทับของเก่า เรียก Pop(id) เพื่อคืนแถบให้ระบบปกติ
        /// </summary>
        public void Push(string id, bool replaceAll, params RuleKeyHint[] hints)
        {
            if (string.IsNullOrEmpty(id)) return;

            var entry = _overrides.Find(o => o.id == id);
            if (entry == null)
            {
                entry = new Override { id = id };
                _overrides.Add(entry);
            }

            entry.hints      = hints;
            entry.replaceAll = replaceAll;
            _dirty           = true;
        }

        public void Push(string id, params RuleKeyHint[] hints) => Push(id, true, hints);

        public void Pop(string id)
        {
            int index = _overrides.FindIndex(o => o.id == id);
            if (index < 0) return;

            _overrides.RemoveAt(index);
            _dirty = true;
        }

        public void ClearOverrides()
        {
            if (_overrides.Count == 0) return;

            _overrides.Clear();
            _dirty = true;
        }

        /// <summary>วาดใหม่ทันที — เรียกหลังแก้ contexts ตอน Play อยู่</summary>
        public void Refresh() => _dirty = true;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void OnEnable() => _dirty = true;

        private void Update()
        {
            bool blocked = hideDuringCutscene && CutsceneManager.IsPlaying;
            if (blocked != _blocked)
            {
                _blocked = blocked;
                _dirty   = true;
            }

            var context = Resolve();

            // วาดใหม่เฉพาะตอนเปลี่ยนจริง — ไม่งั้นจะ Instantiate ใหม่ทุกเฟรม
            if (!_dirty && _hasShown && context == _shown) return;

            _shown    = context;
            _hasShown = true;
            _dirty    = false;
            Rebuild(context);
        }

        // ─────────────────────────── Internal ───────────────────────────

        private PromptContext Resolve()
        {
            if (phoneSystem == null) return PromptContext.World;

            switch (phoneSystem.CurrentState)
            {
                case PhoneState.AppSelection:
                case PhoneState.PhoneRaised:
                    return PromptContext.PhoneHome;

                case PhoneState.FriendList:
                    return PromptContext.FriendList;

                case PhoneState.ChatView:
                    // ปุ่มเลขเปลี่ยนความหมายตอนมีตัวเลือกคำตอบ แถบต้องบอกให้ตรง
                    return chatUI != null && chatUI.AwaitingReply
                         ? PromptContext.ChatReply
                         : PromptContext.Chat;

                case PhoneState.FlashLight:
                    return PromptContext.FlashLight;

                case PhoneState.RuleBook:
                    return PromptContext.RuleBook;

                default:
                    return PromptContext.World;
            }
        }

        private void Rebuild(PromptContext context)
        {
            foreach (var row in _rows) if (row != null) Destroy(row);
            _rows.Clear();

            var hints = _blocked ? new List<RuleKeyHint>() : Collect(context);

            // ระหว่างฉากเปิดต้องซ่อนทั้งแถบเสมอ ไม่สนว่าตั้ง hideWhenEmpty ไว้ยังไง
            if (hideWhenEmpty || _blocked) SetBarVisible(hints.Count > 0);

            if (rowRoot == null || rowPrefab == null) return;

            foreach (var hint in hints)
            {
                if (hint == null || string.IsNullOrWhiteSpace(hint.key)) continue;

                var row = Instantiate(rowPrefab, rowRoot);
                KeyCapView.Apply(row, hint.key, hint.action);
                _rows.Add(row);
            }
        }

        /// <summary>
        /// ซ่อน/โชว์แถบ โดยไม่เผลอปิด GameObject ของตัวเอง
        ///
        /// barRoot มักถูกชี้มาที่ตัว component เองหรือพ่อของมัน ซึ่งถ้าสั่ง SetActive(false) ไปตรงๆ
        /// Update() จะหยุดรันทันที แล้วจะไม่มีใครเหลือคอยเช็กว่าควรกลับมาโชว์ได้หรือยัง —
        /// แถบหายถาวรทั้งที่เงื่อนไขหมดไปแล้ว เคสนี้เลยหรี่ด้วย CanvasGroup แทนการปิด GameObject
        /// </summary>
        private void SetBarVisible(bool visible)
        {
            if (barRoot == null) return;

            if (!transform.IsChildOf(barRoot.transform))
            {
                barRoot.SetActive(visible);
                return;
            }

            if (_selfGroup == null)
            {
                _selfGroup = barRoot.GetComponent<CanvasGroup>();
                if (_selfGroup == null) _selfGroup = barRoot.AddComponent<CanvasGroup>();
            }

            _selfGroup.alpha          = visible ? 1f : 0f;
            _selfGroup.blocksRaycasts = visible;
            _selfGroup.interactable   = visible;
        }

        private List<RuleKeyHint> Collect(PromptContext context)
        {
            var result = new List<RuleKeyHint>();

            // override ตัวหลังสุดที่สั่ง replaceAll ชนะ — กฎที่เพิ่งเริ่มควรทับของเก่า
            for (int i = _overrides.Count - 1; i >= 0; i--)
            {
                if (!_overrides[i].replaceAll) continue;

                if (_overrides[i].hints != null) result.AddRange(_overrides[i].hints);
                return result;
            }

            var set = contexts.Find(c => c != null && c.context == context);
            if (set?.hints != null) result.AddRange(set.hints);

            foreach (var entry in _overrides)
                if (entry.hints != null) result.AddRange(entry.hints);

            return result;
        }

        // ─────────────────────────── Defaults ───────────────────────────

        /// <summary>
        /// เติมคำใบ้ตามดีไซน์ให้ตอนแปะ component ครั้งแรก — แก้ทีหลังใน Inspector ได้
        /// (คลิกขวาที่หัว component → Reset เพื่อเรียกใหม่)
        /// </summary>
        private void Reset()
        {
            contexts = new List<ContextHints>
            {
                Set(PromptContext.World,      Hint("Tab", "เปิดโทรศัพท์")),
                Set(PromptContext.PhoneHome,  Hint("1|2|3", "เปิดแอป"),
                                              Hint("Tab", "เก็บโทรศัพท์")),
                Set(PromptContext.FriendList, Hint("W|S", "เลือก"),
                                              Hint("Enter", "เปิดแชท"),
                                              Hint("Tab", "เก็บโทรศัพท์")),
                Set(PromptContext.Chat,       Hint("Scroll", "เลื่อนแชท"),
                                              Hint("Tab", "เก็บโทรศัพท์")),
                Set(PromptContext.ChatReply,  Hint("1|2", "เลือกคำตอบ"),
                                              Hint("Scroll", "เลื่อนแชท"),
                                              Hint("Tab", "เก็บโทรศัพท์")),
                Set(PromptContext.FlashLight, Hint("2", "ปิดไฟฉาย"),
                                              Hint("Tab", "เก็บโทรศัพท์")),
                Set(PromptContext.RuleBook,   Hint("A|D", "ดูกฎข้อก่อน"),
                                              Hint("1", "กลับไปแชท"),
                                              Hint("Tab", "เก็บโทรศัพท์")),
            };
        }

        private static ContextHints Set(PromptContext context, params RuleKeyHint[] hints)
            => new ContextHints { context = context, hints = hints };

        private static RuleKeyHint Hint(string key, string action)
            => new RuleKeyHint { key = key, action = action };
    }
}
