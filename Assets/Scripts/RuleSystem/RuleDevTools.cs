using System.Collections;
using Manager;
using Player;
using UnityEngine;

namespace RuleSystem
{
    /// <summary>
    /// ทางเดียวของปุ่มเทสทุกตัว (RuleDevSkip, Rule4/5/6DevSkip) ในการข้ามไปกฎอื่นและจบกฎ
    ///
    /// รวมไว้ที่เดียวเพราะการข้ามกฎต้องทำตามลำดับ และแต่ละเครื่องมือเคยทำไม่เหมือนกัน:
    ///   1. จบกฎที่เล่นอยู่ก่อน ไม่งั้นได้สองกฎเล่นซ้อนกัน
    ///   2. รอให้กะพริบตาของกฎเก่าจบ ไม่งั้นการกะพริบของกฎใหม่จะซ้อนแล้วโหมดสลับผิด
    ///   3. ข้ามการรออ่านข้อความ เพราะการข้ามเวลาทำให้ข้อความก่อนหน้าส่งถึงพร้อมกันหมด
    ///   4. ล้างสถานะ "เล่นไปแล้ว" ของกฎปลายทาง จะได้ข้ามกลับไปเทสกฎเดิมซ้ำได้
    ///
    /// ไม่ได้ครอบด้วย #if เพราะตัวมันไม่ทำอะไรเองถ้าไม่มีปุ่มเทสเรียก
    /// </summary>
    public static class RuleDevTools
    {
        /// <summary>กำลังข้ามกฎอยู่ — กันกดปุ่มซ้อนระหว่างรอกะพริบตา</summary>
        public static bool IsJumping { get; private set; }

        /// <summary>ใช้ปุ่มเทสไปแล้วในรอบ Play นี้ — หลังจากนี้เครื่องมือเลิกหยุดนาฬิกาให้เอง</summary>
        public static bool HasBeenUsed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            IsJumping   = false;
            HasBeenUsed = false;
        }

        /// <summary>จบกฎที่เล่นอยู่ นับว่าผ่าน — คืนกฎที่จบ (null = ไม่มีกฎเล่นอยู่)</summary>
        public static RuleBase EndActiveRule()
        {
            HasBeenUsed = true;

            // flashback ที่เล่นค้างอยู่ตัดทิ้ง ไม่ให้ปุ่มเทสต้องรอมันจบ
            Flashbacks.FlashbackPlayer.AbortPlaying();

            RuleBase active = RuleBase.ActiveRuleInScene();
            if (active == null) return null;

            RestorePlayerControl();

            // DebugForceEnd นับว่าผ่าน → flashback "หลังผ่านกฎ" ของข้อนั้นเข้าคิว แล้วเล่นใน blink กลับ Relax
            // เหมือนผ่านกฎจริง — เปิด DevEnding ไว้ให้มันเล่นแม้เคยดูไปแล้ว (เทสซ้ำได้)
            Flashbacks.FlashbackPlayer.DevEnding = true;
            try { active.DebugForceEnd(); }
            finally { Flashbacks.FlashbackPlayer.DevEnding = false; }

            return active;
        }

        /// <summary>
        /// จบกฎที่เล่นอยู่แล้วเข้ากฎ target — ใช้ใน coroutine ของเครื่องมือ
        /// จากนั้นกฎจะรอให้นั่งที่ศาลาตามปกติ
        /// </summary>
        public static IEnumerator JumpTo(RuleBase target)
        {
            if (target == null || IsJumping) yield break;

            IsJumping   = true;
            HasBeenUsed = true;

            Flashbacks.FlashbackPlayer.AbortPlaying();
            if (RuleBase.ActiveRuleInScene() != target) EndActiveRule();

            yield return WaitForBlink();

            RuleBase.DebugSkipMessageGate = true;   // ต้องตั้งก่อน SetTime — CheckRules เริ่มกฎได้ในบรรทัดนั้นเลย
            target.ResetForNewLoop();

            // flashback "ก่อนเริ่มกฎ" ของกฎ 2-6 เข้าคิวเองตอนผู้เล่นนั่ง (RuleFlow → WaitForMessagesAndSeat)
            // แล้วเล่นใน blink เข้า Tension — บอกให้รอบนี้เล่นแม้เคยดูแล้ว (Play Once) จะได้เทสซ้ำได้
            // ตั้งก่อน SetTime เหตุผลเดียวกับข้างบน / กฎที่เล่นอยู่แล้วไม่ตั้ง (มันผ่านช่วงรอนั่งไปแล้ว)
            if (!(target is Rule1) && !target.ruleActive)
                Flashbacks.FlashbackPlayer.MarkDevStart(target);

            TimeManager clock = TimeManager.instance;
            if (clock != null)
            {
                clock.IsPauseTime(true);
                clock.SetTime(target.startHour, target.startMinute);
            }

            if (target is Rule1 rule1)
            {
                // กฎ 1 ไม่ได้เริ่มตามเวลา และไม่กะพริบเข้า Tension เอง ปกติแชทเป็นคนทำให้
                // ถ้าเริ่มจาก Relax กติกาเขตจะไม่ทำงาน และออกนอกเขตได้แค่ถูกหันตัวกลับ
                //
                // กฎ 1 ไม่ผ่าน WaitForMessagesAndSeat (ทางที่ใส่ flashback เข้าคิว) — ใส่ให้ตรงนี้ แล้วเปิดให้
                // blink นี้เล่นได้ มี flashback แต่อยู่ Tension อยู่แล้วก็ยังกะพริบ ไม่งั้นไม่มีจังหวะให้ฉายฉาก
                var  mode         = GameModeController.instance;
                bool hasFlashback = Flashbacks.FlashbackPlayer.HasFlashbackFor(
                                        rule1, Flashbacks.FlashbackTrigger.BeforeRuleStarts);

                if (mode != null && (mode.CurrentMode != GameMode.Tension || hasFlashback))
                {
                    if (hasFlashback)
                        Flashbacks.FlashbackPlayer.QueueFor(rule1, Flashbacks.FlashbackTrigger.BeforeRuleStarts,
                                                            ignorePlayOnce: true);

                    mode.DirectBlinkToMode(GameMode.Tension, allowFlashback: hasFlashback);
                    yield return WaitForBlink();
                }

                rule1.TriggerStartRule();
            }
            else if (!target.ruleActive)
            {
                target.StartRule();   // CheckRules อาจเริ่มให้แล้วจาก SetTime ข้างบน ห้ามเริ่มซ้ำ
            }

            IsJumping = false;
        }

        private static IEnumerator WaitForBlink()
        {
            yield return null;   // รอหนึ่งเฟรม กฎบางข้อสั่งกะพริบหลังจบกฎในเฟรมถัดไป (กฎ 3)
            yield return null;

            var mode = GameModeController.instance;
            while (mode != null && mode.IsBlinking)
            {
                yield return null;
                mode = GameModeController.instance;
            }

            yield return null;
        }

        /// <summary>กฎอาจถูกจบกลางฉากตายที่ล็อกกล้องกับการเดินไว้ ซึ่ง coroutine ปลดล็อกถูกหยุดไปแล้ว</summary>
        private static void RestorePlayerControl()
        {
            var pc = PlayerController.Instance;
            if (pc == null || (pc.CanLook && pc.CanMove)) return;

            pc.ResetCameraAfterDeath();
            if (!pc.IsSitting()) pc.SetMovement(true);
        }
    }
}
