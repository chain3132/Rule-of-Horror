using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Manager
{
    /// <summary>
    /// ตัวเชื่อมระหว่าง RuleBookController (ข้อมูล) กับแผงแอปกฎ (ภาพ)
    ///
    /// ทุกช่องเว้นว่างได้หมด ใส่เฉพาะอันที่จัด UI ไว้แล้ว — ตัวนี้ไม่บังคับโครงสร้าง ไม่สร้าง layout
    /// ให้ และไม่ยุ่งกับขนาด/สี มันทำแค่เอาข้อความไปหยอดในช่องที่ชี้ไว้ ตอนที่ข้อมูลเปลี่ยน
    ///
    /// บรรทัดของ "สิ่งที่รู้" กับ "มือของคุณ" เลือกได้ 2 แบบ:
    ///   • ใส่ prefab + root → สร้างแถวตามจำนวนจริง (ยืดหยุ่น เหมาะกับ Layout Group)
    ///   • ใส่ TMP_Text ที่ทำไว้แล้วเป็นแถวๆ → เติมข้อความลงไป ไม่สร้างอะไรเพิ่ม (คุมหน้าตาได้เป๊ะกว่า)
    /// ใส่ทั้งคู่ก็ได้ แบบที่ทำไว้ล่วงหน้าจะถูกใช้ก่อน
    /// </summary>
    public class RuleBookPanelBinder : MonoBehaviour
    {
        [Header("ข้อมูล")]
        [SerializeField] private RuleBookController ruleBook;

        [Header("หัวหน้า")]
        [Tooltip("แถบเลขไทยด้านบน ๑ ๒ · · ·")]
        [SerializeField] private TMP_Text numeralStripText;
        [Tooltip("หัวข้อกฎ เช่น ข้อ ๕ — สายสิญจน์")]
        [SerializeField] private TMP_Text titleText;
        [Tooltip("ช่วงเวลา — จะเป็น --:-- – --:-- เองถ้าผู้เล่นตอบแชทแบบหัวแข็ง")]
        [SerializeField] private TMP_Text scheduleText;
        [Tooltip("ตัวกฎเต็ม")]
        [SerializeField] private TMP_Text summaryText;

        [Tooltip("รูปแบบหัวข้อ — {0} = เลขไทย, {1} = ชื่อกฎ")]
        [SerializeField] private string titleFormat = "ข้อ {0} — {1}";

        [Header("สิ่งที่รู้")]
        [SerializeField] private Transform factRoot;
        [SerializeField] private GameObject factRowPrefab;
        [Tooltip("แถวขีดขาวยาวๆ สำหรับข้อที่ผู้เล่นรู้ไม่ครบ — เว้นว่าง = ไม่โชว์ช่องว่างเลย\n" +
                 "สร้างด้วย Rule of Horror ▸ Phone ▸ Upgrade Rule Book Fact Rows")]
        [SerializeField] private GameObject factMissingRowPrefab;
        [Tooltip("ถ้าทำแถวไว้แล้ว ใส่ตรงนี้แทน prefab — แถวที่เกินจะถูกซ่อน")]
        [SerializeField] private TMP_Text[] factRows;

        [Header("มือของคุณ")]
        [SerializeField] private Transform handRoot;
        [Tooltip("prefab ของแถวปุ่ม — ใส่ KeyHintRow ไว้จะได้ปุ่มเป็นภาพและรองรับหลายปุ่มต่อแถว\n" +
                 "ถ้าเป็น prefab เก่าที่มีแค่ TMP_Text 2 ตัว (ปุ่ม/คำอธิบาย) ก็ยังใช้ได้เหมือนเดิม")]
        [SerializeField] private GameObject handRowPrefab;
        [SerializeField] private TMP_Text[] handKeyTexts;
        [SerializeField] private TMP_Text[] handActionTexts;

        [Header("ยังไม่มีกฎสักข้อ")]
        [Tooltip("แสดงตอนที่ผู้เล่นยังไม่ได้อ่านแชทจนได้กฎมาเลย")]
        [SerializeField] private GameObject emptyState;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private void OnEnable()
        {
            if (ruleBook == null) return;

            ruleBook.OnRuleBookChanged    += Refresh;
            ruleBook.OnSelectedRuleChanged += HandleSelectionChanged;
            L10n.Changed                   += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (ruleBook == null) return;

            ruleBook.OnRuleBookChanged    -= Refresh;
            ruleBook.OnSelectedRuleChanged -= HandleSelectionChanged;
            L10n.Changed                   -= Refresh;
        }

        private void HandleSelectionChanged(int _) => Refresh();

        /// <summary>วาดหน้าแอปกฎใหม่ทั้งหน้า — เรียกเองได้ถ้าเปิดแผงขึ้นมาแล้วอยาก sync</summary>
        public void Refresh()
        {
            if (ruleBook == null) return;

            bool hasAny = ruleBook.HasAnyUnlocked;
            if (emptyState != null) emptyState.SetActive(!hasAny);

            if (numeralStripText != null) numeralStripText.text = ruleBook.NumeralStrip();

            var view = ruleBook.CurrentView;
            bool show = hasAny && view.Rule != null && view.Unlocked;

            if (titleText != null)
                titleText.text = show ? L10n.Format(titleFormat, view.Rule.ThaiNumeral, L10n.T(view.Rule.title)) : string.Empty;

            if (scheduleText != null)
                scheduleText.text = show ? view.ScheduleText : RuleBookController.UnknownSchedule;

            if (summaryText != null)
                summaryText.text = show ? L10n.T(view.Rule.summary) : string.Empty;

            ClearSpawned();
            FillFacts(show ? view.Facts : null, show ? view.MissingFacts : 0);
            FillHands(show ? view.Hands : null);
        }

        // ─────────────────────────── Rows ───────────────────────────

        /// <summary>
        /// วาดรายละเอียดที่รู้ แล้วต่อด้วยขีดขาวหนึ่งเส้นต่อหนึ่งข้อที่รู้ไม่ครบ
        ///
        /// ขีดขาวต้องเป็นแถวของตัวเองไม่ใช่ตัวหนังสือในแถวเดิม เพราะมันไม่ใช่เนื้อหา —
        /// มันคือที่ว่างที่ควรมีอะไรอยู่ ผู้เล่นจะได้นับออกว่าตอบหัวแข็งแล้วเสียอะไรไปกี่ข้อ
        /// </summary>
        private void FillFacts(IReadOnlyList<string> facts, int missing)
        {
            int count = facts?.Count ?? 0;

            // ── ทางเลือก: แถว TMP_Text ที่ทำไว้ล่วงหน้า ──────────────────
            // ไม่มีที่ให้วาดภาพ ช่องว่างจึงเป็นเส้นประยาวๆ เป็นตัวหนังสือแทน
            if (factRows != null && factRows.Length > 0)
            {
                for (int i = 0; i < factRows.Length; i++)
                {
                    if (factRows[i] == null) continue;

                    bool isFact    = i < count;
                    bool isMissing = !isFact && i < count + missing;

                    factRows[i].gameObject.SetActive(isFact || isMissing);
                    if (isFact)         factRows[i].text = L10n.T(facts[i]);
                    else if (isMissing) factRows[i].text = RuleBookController.EmptyFact;
                }
                return;
            }

            if (factRoot == null) return;

            // ── ทางหลัก: สร้างแถวจาก prefab ──────────────────────────────
            if (factRowPrefab != null)
            {
                for (int i = 0; i < count; i++)
                {
                    var row  = Instantiate(factRowPrefab, factRoot);
                    var text = row.GetComponentInChildren<TMP_Text>();
                    if (text != null) text.text = L10n.T(facts[i]);
                    _spawned.Add(row);
                }
            }

            if (factMissingRowPrefab == null) return;

            for (int i = 0; i < missing; i++)
                _spawned.Add(Instantiate(factMissingRowPrefab, factRoot));
        }

        private void FillHands(IReadOnlyList<RuleKeyHint> hands)
        {
            int count = hands?.Count ?? 0;

            if (handKeyTexts != null && handKeyTexts.Length > 0)
            {
                for (int i = 0; i < handKeyTexts.Length; i++)
                {
                    bool used = i < count;

                    if (handKeyTexts[i] != null)
                    {
                        handKeyTexts[i].gameObject.SetActive(used);
                        if (used) handKeyTexts[i].text = hands[i].key;
                    }

                    if (handActionTexts != null && i < handActionTexts.Length && handActionTexts[i] != null)
                    {
                        handActionTexts[i].gameObject.SetActive(used);
                        if (used) handActionTexts[i].text = L10n.T(hands[i].action);
                    }
                }
                return;
            }

            if (handRoot == null || handRowPrefab == null) return;

            for (int i = 0; i < count; i++)
            {
                var row = Instantiate(handRowPrefab, handRoot);

                // Apply เลือกให้เองว่าแถวนี้วาดเป็นภาพปุ่มหรือตัวหนังสือ ตามที่ prefab มี component อะไร
                KeyCapView.Apply(row, hands[i].key, L10n.T(hands[i].action));
                _spawned.Add(row);
            }
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
        }
    }
}
