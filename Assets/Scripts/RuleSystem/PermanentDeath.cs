using System;
using Manager;
using Player;
using UnityEngine;

namespace RuleSystem
{
    /// <summary>
    /// กติกาของคืนรอบสอง: ตายแม้แต่ครั้งเดียว = จบ ไม่มี rewind ให้เล่นใหม่อีก
    ///
    /// มาจากดีไซน์หน้ารถคันที่ 4 ของกฎข้อ 6 — "ครั้งนี้หากผู้เล่นตายแม้แต่รอบเดียว เราจะไม่มี
    /// วันกลับมาที่ศาลานี้อีกเลย" การขึ้นรถคันที่พาวนลูปจึงไม่ใช่การได้โอกาสอีกครั้ง มันคือการ
    /// แลกโอกาสทั้งหมดที่เหลือกับการได้เล่นคืนเดิมซ้ำ
    ///
    /// ทำเป็นตัวกลางตัวเดียว ไม่ไปเขียนเงื่อนไขซ้ำในกฎทั้ง 5 ข้อ เพราะกฎแต่ละข้อ rewind เวลา
    /// กลับคนละที่คนละเวลา (19:50 / 20:40 / 21:55 / 22:55) และกฎ 1 ไม่ rewind เลย ตัวที่
    /// เหมือนกันคือ "ก่อนจะย้อนเวลา ให้ถามก่อนว่ายังมีโอกาสเหลืออยู่ไหม"
    ///
    /// วิธีใช้ในกฎ — ใส่ก่อนบรรทัดที่ย้อนเวลา หลัง cleanup ของกฎนั้นทำงานเสร็จแล้ว:
    /// <code>
    ///   if (PermanentDeath.Claim(this)) yield break;   // รอบสอง: ตายรอบเดียวจบ
    ///   TimeManager.instance.SetTime(gameOverResetHour, gameOverResetMinute);
    /// </code>
    ///
    /// วางตรงนั้นเพราะกฎต้องเก็บของตัวเองให้หมดก่อน (ดับเสียง ลบผี คืนไฟ) ไม่ว่าจะได้เล่นใหม่
    /// หรือไม่ — แต่ต้องถามก่อนจะปล่อยนาฬิกา ไม่งั้นเวลาจะเดินต่อใต้จอ game over
    /// </summary>
    public class PermanentDeath : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("จอตอนตายถาวร")]
        [Tooltip("จอที่เปิดตอนตายในคืนรอบสอง — ที่เดียวกับที่บทจบของกฎ 6 ไปอยู่\n" +
                 "เว้นว่างได้ กติกายังทำงาน (เวลาหยุด ผู้เล่นขยับไม่ได้) แต่จอจะไม่มีอะไรบอก")]
        [SerializeField] private GameObject deathScreen;

        [Tooltip("บทพูดตอนตายถาวร")]
        [SerializeField] private DialogueLine[] lines;

        [Header("เงื่อนไข")]
        [Tooltip("ต้องวนลูปมาแล้วกี่รอบ กติกานี้จึงมีผล\n" +
                 "1 = มีผลตั้งแต่คืนรอบสอง (ค่าตามดีไซน์) · 0 = มีผลตั้งแต่คืนแรก (ไว้เทส)")]
        [Min(0)]
        [SerializeField] private int requiredLoops = 1;

        // ─────────────────────────── Runtime ───────────────────────────

        private static PermanentDeath _instance;
        private bool _triggered;

        /// <summary>ตัวในซีน — หาให้เองถ้ายังไม่มีใครเซ็ต (ลำดับ Awake ระหว่าง component ไม่การันตี)</summary>
        public static PermanentDeath instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = FindAnyObjectByType<PermanentDeath>(FindObjectsInactive.Include);
                return _instance;
            }
        }

        /// <summary>
        /// true = ตอนนี้ตายแล้วจบเลย ไม่ได้เล่นใหม่ (ใช้โชว์ใน HUD / dev tool)
        ///
        /// ต้องเขียนชื่อเต็ม RuleSystem.Rule.Rule6 — ในนี้ชื่อ "Rule6" ไปตรงกับ namespace
        /// ของโฟลเดอร์รถ (Rule6/Rule6Bus.cs) ซึ่งชนะคลาสที่อยู่ลึกกว่า เขียนสั้นจะคอมไพล์ไม่ผ่าน
        /// </summary>
        public bool IsArmed => RuleSystem.Rule.Rule6.NightLoopCount >= requiredLoops;

        /// <summary>ยิงตอนตายถาวร — ตัวเชื่อมสำหรับระบบ ending / credits ที่จะมาทีหลัง</summary>
        public static event Action OnPermanentDeath;

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ─────────────────────────── API ───────────────────────────

        /// <summary>
        /// ถามว่าการตายครั้งนี้จบเกมเลยไหม
        ///
        /// คืน true = จบ ผู้เรียกต้อง "ไม่" ย้อนเวลาและไม่เรียก EndRule ต่อ (yield break ทันที)
        /// คืน false = ยังมีโอกาสเหลือ ให้ทำตามทางเดิมของกฎนั้นต่อไป
        ///
        /// ไม่มีตัวนี้ในซีน = คืน false เสมอ เกมจึงเล่นได้ปกติเหมือนก่อนมีกติกานี้
        /// </summary>
        public static bool Claim(RuleBase rule)
        {
            var gate = instance;
            if (gate == null || !gate.IsArmed) return false;

            gate.Trigger(rule);
            return true;
        }

        private void Trigger(RuleBase rule)
        {
            if (_triggered) return;
            _triggered = true;

            // ปลด ruleActive เองไม่ผ่าน EndRule() — EndRule ของแต่ละกฎจะไปทำพิธีจบกฎต่อ
            // (ปล่อยนาฬิกา, ดึงผู้เล่นลุกจากที่นั่ง, blink กลับ Relax) ซึ่งทั้งหมดไม่ควรเกิด
            // ตอนเกมจบแล้ว ที่ต้องการแค่ "หยุด Update ของกฎนั้น"
            if (rule != null) rule.ruleActive = false;

            // ยึดนาฬิกาไว้ ไม่งั้นเวลาจะเดินต่อใต้จอ game over แล้วกฎข้อถัดไปจะ trigger ขึ้นมา
            if (TimeManager.instance != null) TimeManager.instance.IsPauseTime(true);

            var pc = PlayerController.Instance;
            if (pc != null)
            {
                pc.SetMovement(false);
                pc.isBlockStanding = true;
            }

            if (deathScreen != null) deathScreen.SetActive(true);
            else Debug.LogWarning("[PermanentDeath] ยังไม่ได้ใส่ Death Screen — เกมจบแล้วแต่จอ" +
                                  "จะค้างอยู่ที่ท่าตายโดยไม่มีอะไรบอกผู้เล่น", this);

            if (lines != null && lines.Length > 0 && PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowSequence(lines);

            OnPermanentDeath?.Invoke();

            Debug.Log($"[PermanentDeath] ตายในคืนรอบที่ {RuleSystem.Rule.Rule6.NightLoopCount + 1} " +
                      $"({rule?.GetType().Name ?? "ไม่ทราบกฎ"}) — จบเกม ไม่ rewind", this);
        }
    }
}
