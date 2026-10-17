using System.Collections;
using Player;
using UnityEngine;

namespace RuleSystem
{
    public abstract class RuleBase : MonoBehaviour
    {
        public int startHour;
        public int startMinute;

        public int endHour;
        public int endMinute;

        public bool ruleActive;
        private HorrorTextUI _horrorTextUI;

        
        public virtual void StartRule()
        {
            ruleActive = true;
        }

        public virtual void EndRule()
        {
            ruleActive = false;
            StandUpAfterRule();
        }

        /// <summary>
        /// ล้างสถานะ "เล่นข้อนี้ไปแล้ว" ให้กฎข้อนี้เริ่มได้อีกครั้งในคืนใหม่
        ///
        /// ใช้ตอนผู้เล่นขึ้นรถคันที่ 4 ของกฎข้อ 6 (บทจบวนลูป) แล้วเวลาย้อนกลับไป 18:40
        /// RuleManager.RearmAllRules() เป็นคนเรียกให้ทุกข้อ พร้อมปลด ruleActive ให้แล้ว
        ///
        /// ไม่ทำอะไรเป็นค่าเริ่มต้น — กฎส่วนใหญ่รีเซ็ตสถานะของตัวเองตอน StartRule อยู่แล้ว
        /// ข้อที่ต้อง override คือข้อที่มีธงกันเล่นซ้ำซึ่งอยู่นอก StartRule (กฎ 1)
        /// </summary>
        public virtual void ResetForNewLoop() { }

        /// <summary>
        /// ลุกจากที่นั่งให้อัตโนมัติเมื่อกฎจบ (กฎ 1-5)
        ///
        /// ทุกกฎจบลงตอนผู้เล่นนั่งอยู่ที่ศาลา เดิมต้องเล็งม้านั่งแล้วกด E ลุกเอง ซึ่งไม่มีอะไรบอก
        /// และหน้าตาเหมือนเกมค้าง — กฎจบแล้ว เวลาเดินแล้ว แต่ขยับไปไหนไม่ได้
        ///
        /// รอให้ตาปิดก่อนแล้วค่อยลุก: ทุกกฎสั่ง BlinkToMode(Relax) ก่อนเรียก base.EndRule()
        /// อยู่แล้ว การลุกจึงเกิดตอนจอดำ ลืมตามาก็ยืนอยู่แล้ว ไม่ใช่เห็นตัวเองค่อยๆ ยืดขึ้น
        /// กลางอาการหน้ามืด
        /// </summary>
        protected void StandUpAfterRule()
        {
            var pc = PlayerController.Instance;
            if (pc == null || !pc.IsSitting()) return;

            StartCoroutine(StandUpAfterRuleRoutine());
        }

        /// <summary>นานสุดที่จะรอให้ตาปิด ก่อนจะยอมลุกทั้งที่จอยังสว่าง (วินาที)</summary>
        private const float EyesCloseTimeout = 2.5f;

        private IEnumerator StandUpAfterRuleRoutine()
        {
            // นับด้วย unscaledDeltaTime เพราะช่วง faint ก่อน blink หั่น Time.timeScale ลงถึง 0.6
            // ถ้าใช้ deltaTime timeout จะยืดตามไปด้วยโดยไม่มีเหตุผล
            float waited = 0f;
            while (waited < EyesCloseTimeout)
            {
                var mode = GameModeController.instance;
                if (mode == null || !mode.IsEyesOpen) break;

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            var pc = PlayerController.Instance;
            if (pc == null || !pc.IsSitting()) yield break;

            // Rule 1 เรียก base.EndRule() ก่อนปลด isBlockStanding ส่วนกฎอื่นปลดก่อน
            // ถ้าไม่เคลียร์ตรงนี้ StartStandingSequence() จะเงียบออกไปเฉยๆ แล้วค้างในท่านั่ง
            pc.isBlockStanding = false;
            pc.StartStandingSequence();
        }

        protected virtual void Update()
        {
            if (!ruleActive) return;

            UpdateRule();
        }

        protected virtual void UpdateRule() { }
    }
}
