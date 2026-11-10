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

        [Header("เตือนให้กลับไปนั่ง (ทำครบแล้วแต่ยังไม่นั่ง)")]
        [Tooltip("บทพูดที่ขึ้นซ้ำๆ ตอนทำภารกิจของกฎครบแล้ว เหลือแค่กลับไปนั่งที่ศาลา (แปลผ่าน L10n)\n" +
                 "เว้นว่าง = ไม่เตือน")]
        [SerializeField] private string sitReminderText = "ฉันว่าฉันกลับไปนั่งดีกว่า";

        [Tooltip("ทำครบแล้วรอกี่วินาทีก่อนเตือนครั้งแรก")]
        [Min(0f)]
        [SerializeField] private float sitReminderFirstDelay = 5f;

        [Tooltip("เตือนซ้ำทุกกี่วินาที จนกว่าจะนั่ง")]
        [Min(1f)]
        [SerializeField] private float sitReminderInterval = 15f;

        private float _sitReminderAt = -1f;

        /// <summary>
        /// ทำภารกิจของกฎข้อนี้ครบแล้ว เหลือแค่กลับไปนั่งที่ศาลา — ระหว่างนี้ผู้เล่นที่ยังไม่นั่งจะได้ยิน
        /// ตัวเองพูด "ฉันว่าฉันกลับไปนั่งดีกว่า" เป็นระยะ (กฎที่จบด้วยการนั่งต้อง override)
        /// ต้องเป็น false ตอนฉากตายเล่นอยู่
        /// </summary>
        public virtual bool WaitingForPlayerToSit => false;

        /// <summary>
        /// ผ่านกฎข้อนี้สำเร็จ (ไม่ใช่ตาย ไม่ใช่ถูกย้อนเวลา) — สมุดโน้ตฟังตัวนี้เพื่อปลดล็อกโน้ตของข้อนั้น
        ///
        /// แยกจาก EndRule เพราะ EndRule ถูกเรียกทั้งตอนผ่านและตอนตาย (ทางตายเรียก base.EndRule()
        /// เพื่อให้ RuleManager trigger ข้อเดิมใหม่) ดูจาก EndRule อย่างเดียวจึงแยกสองทางไม่ออก
        /// </summary>
        public static event System.Action<RuleBase> OnRuleCompleted;

        // ปิด Reload Domain แล้ว static ไม่ถูกล้างเอง — event ที่ค้างจากรอบ Play ก่อนจะชี้ไปหา object ที่ถูกทำลายไปแล้ว
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            OnRuleCompleted      = null;
            DebugSkipMessageGate = false;
            _friendList          = null;
            _allRules            = null;
        }

        private static RuleBase[] _allRules;

        /// <summary>
        /// กฎที่กำลังทำงานอยู่ตอนนี้ (null = ไม่มี) — รวมกฎ 1 ซึ่งไม่ได้อยู่ใน RuleManager.rules
        /// เก็บลิสต์ไว้ เรียกจาก HUD ของปุ่มเทสได้ทุกเฟรมโดยไม่ต้องสแกนซีนซ้ำ
        /// </summary>
        public static RuleBase ActiveRuleInScene()
        {
            if (_allRules == null)
                _allRules = FindObjectsByType<RuleBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var rule in _allRules)
            {
                if (rule == null)
                {
                    _allRules = null;   // โหลดซีนใหม่ ชุดเก่าถูกทำลายแล้ว รอบหน้าหาใหม่
                    return null;
                }

                if (rule.ruleActive) return rule;
            }

            return null;
        }

        /// <summary>
        /// DEBUG: จบกฎนี้เดี๋ยวนี้ นับว่าผ่าน — ปุ่มเทสเรียกตัวนี้แทน EndRule
        ///
        /// EndRule ของกฎที่เล่นอยู่ปฏิเสธคำสั่งโดยตั้งใจ (กฎ 4-6 จบตามเงื่อนไขของมันเท่านั้น)
        /// และไม่ได้เก็บของที่ยังค้างกลางกฎ แต่ละข้อจึงต้อง override ถ้ามีของต้องเก็บเพิ่ม
        /// </summary>
        public virtual void DebugForceEnd()
        {
            StopAllCoroutines();
            ReportCompleted();
            EndRule();
        }

        // ─────────────────────────── ก่อนเข้ากฎ ───────────────────────────

        /// <summary>หลังอ่านข้อความสุดท้ายจบ รออีกเท่านี้ก่อนเข้ากฎ ผู้เล่นจะได้อ่านบรรทัดสุดท้ายทัน (วินาที)</summary>
        private const float AfterReadingSeconds = 1.5f;

        /// <summary>
        /// DEBUG: ไม่ต้องรออ่านข้อความก่อนเข้ากฎ — ปุ่มลัดกระโดดข้ามเวลาทำให้ข้อความทุกชุดก่อนหน้า
        /// ถูกส่งพร้อมกัน ถ้าต้องอ่านให้จบก่อนทุกครั้งก็เทสกฎตรงๆ ไม่ได้
        /// </summary>
        public static bool DebugSkipMessageGate;

        private static FriendListController _friendList;

        protected static bool HasUnreadMessages()
        {
            if (DebugSkipMessageGate) return false;

            if (_friendList == null)
                _friendList = FindAnyObjectByType<FriendListController>(FindObjectsInactive.Include);

            return _friendList != null && _friendList.HasUnreadMessages;
        }

        /// <summary>
        /// ทางเข้ากฎทุกข้อ: ต้องอ่านและตอบข้อความที่ส่งถึงแล้วให้จบก่อน แล้วค่อยนั่งที่ศาลา
        ///
        /// นั่งรออยู่ก็ไม่เริ่มถ้ายังอ่านไม่จบ และยืนอ่านจบแล้วก็ยังไม่เริ่มจนกว่าจะกลับมานั่ง
        /// เรียกหลังหยุดนาฬิกาแล้ว ระหว่างรอจึงไม่มีข้อความใหม่เข้ามาเพิ่ม
        ///
        /// เสียงแจ้งเตือนดังอีกครั้งตอนเริ่มรอ เพราะนาฬิกาหยุดอยู่ ผู้เล่นที่นั่งรอเฉยๆ จะนึกว่าเกมค้าง
        /// </summary>
        /// <param name="remindAs">
        /// กฎที่กำลังจะเริ่ม — อ่านแชทจบแล้วแต่ยังไม่นั่ง ผู้เล่นจะพูดเตือนตัวเอง "ฉันว่าฉันกลับไปนั่งดีกว่า"
        /// เป็นระยะ ตามข้อความ/จังหวะที่ตั้งไว้ในกฎข้อนั้น (null = ไม่เตือน)
        /// </param>
        public static IEnumerator WaitForMessagesAndSeat(RuleBase remindAs = null)
        {
            if (HasUnreadMessages())
            {
                if (AudioManager.instance != null) AudioManager.instance.PlayMessageNotification();

                yield return new WaitUntil(() => !HasUnreadMessages());
                yield return new WaitForSeconds(AfterReadingSeconds);
            }

            while (PlayerController.Instance == null || !PlayerController.Instance.IsSitting())
            {
                if (remindAs != null) remindAs.MarkAwaitingSeat();
                yield return null;
            }
        }

        // ── รอนั่งก่อนเริ่มกฎ ──
        //
        // ใช้ "เฟรมล่าสุดที่ยังรออยู่" แทนธง bool: coroutine ที่รอนั่งถูกหยุดกลางทางได้ (ตาย / ปุ่มเทส
        // StopAllCoroutines) ถ้าเป็นธง มันจะค้าง true แล้วผู้เล่นโดนเตือนให้นั่งทั้งที่ไม่มีกฎรออยู่
        // แบบนี้พอ coroutine เลิกมาทำเครื่องหมาย การเตือนก็หยุดเองในเฟรมถัดไป
        private int _awaitingSeatFrame = -100;

        /// <summary>กฎนี้กำลังรอผู้เล่นนั่งก่อนเริ่ม (อ่านแชทจบแล้ว) — เรียกทุกเฟรมที่ยังรออยู่</summary>
        protected void MarkAwaitingSeat() => _awaitingSeatFrame = Time.frameCount;

        private bool AwaitingSeat => Time.frameCount - _awaitingSeatFrame <= 2;

        /// <summary>เลขของกฎจากชื่อคลาส (Rule3 → 3) — 0 = ไม่มีเลข</summary>
        public int Number
        {
            get
            {
                string n = GetType().Name;
                int i = n.Length;
                while (i > 0 && char.IsDigit(n[i - 1])) i--;
                return i < n.Length && int.TryParse(n.Substring(i), out int number) ? number : 0;
            }
        }

        /// <summary>เรียกที่ทางผ่านของกฎแต่ละข้อ ก่อน EndRule() — ทางตายห้ามเรียก</summary>
        public void ReportCompleted() => OnRuleCompleted?.Invoke(this);

        
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
        /// ตอนนี้กำลังเล่นกฎข้อนี้อยู่จริงไหม — HeartbeatSystem ใช้ตัดสินว่ากติกาเขตตอน Tension มีผลหรือเปล่า
        ///
        /// กฎต้อง override ให้เป็น false ตั้งแต่เริ่มฉากตาย ภาพจะหยุดบิดและไม่นับเวลาตายซ้ำ
        /// </summary>
        public virtual bool UsesTensionZones => ruleActive;

        /// <summary>
        /// อยู่นอกเขตจนหมดเวลาตอน Tension — ตายตามทางของกฎข้อนั้น แล้วเริ่มกฎเดิมใหม่
        /// ใช้ทางตายเดิมของกฎ เพราะแต่ละข้อมีของที่ต้องเก็บไม่เหมือนกัน
        /// </summary>
        public virtual void DieOutsidePlayArea() { }

        /// <summary>
        /// ลุกจากที่นั่งให้อัตโนมัติเมื่อกฎจบ (กฎ 1-5)
        ///
        /// ทุกกฎจบลงตอนผู้เล่นนั่งอยู่ที่ศาลา เดิมต้องเล็งม้านั่งแล้วกด E ลุกเอง ซึ่งไม่มีอะไรบอก
        /// และหน้าตาเหมือนเกมค้าง — กฎจบแล้ว เวลาเดินแล้ว แต่ขยับไปไหนไม่ได้
        ///
        /// รอให้ตาปิดสนิทก่อนแล้วค่อยลุก การลุกจึงเกิดตอนจอดำ ลืมตามาก็ยืนอยู่แล้ว ไม่ใช่เห็น
        /// ตัวเองค่อยๆ ยืดขึ้นกลางอาการหน้ามืด
        ///
        /// ล็อก isBlockStanding ไว้ตลอดช่วงรอ ไม่ให้กด E ลุกเองระหว่างช่วงเวียนหัวก่อนตาปิด
        /// แล้วค่อยปลดตอนสั่งลุก — ฝั่งกฎจึงไม่ต้องปลดเองหลังเรียกตัวนี้
        /// </summary>
        protected void StandUpAfterRule()
        {
            var pc = PlayerController.Instance;
            if (pc == null) return;

            if (!pc.IsSitting())
            {
                pc.isBlockStanding = false;   // ยืนอยู่แล้ว ไม่มีอะไรต้องรอ แต่ต้องไม่ทิ้งล็อกค้างไว้ให้ตอนนั่งครั้งหน้า
                return;
            }

            pc.isBlockStanding = true;
            StartCoroutine(StandUpAfterRuleRoutine());
        }

        private IEnumerator StandUpAfterRuleRoutine()
        {
            // กฎ 1 สั่ง blink หลังเรียก base.EndRule() — รอหนึ่งเฟรมให้ทุกกฎสั่งครบก่อนค่อยถาม
            yield return null;

            // ไม่มี timeout โดยตั้งใจ: ช่วงเวียนหัวก่อนตาปิดยาวตาม frame rate (ราว 1.5 วิที่ 30 fps
            // ถึง 7 วิที่ 240 fps) เดิมรอแค่ 2.5 วิ เครื่องที่ fps สูงจึงลุกทั้งที่ยังไม่ทันเข้า Relax
            //
            // ไม่มี blink เล่นอยู่ (ทางตาย: ลืมตาเสร็จแล้วค่อยเรียก base.EndRule) = ลุกได้เลย
            var mode = GameModeController.instance;
            while (mode != null && mode.IsBlinking && !mode.IsEyesFullyClosed)
            {
                yield return null;
                mode = GameModeController.instance;
            }

            var pc = PlayerController.Instance;
            if (pc == null) yield break;

            pc.isBlockStanding = false;
            if (pc.IsSitting()) pc.StartStandingSequence();
        }

        /// <summary>
        /// ส่งผู้เล่นกลับมายืนหน้าม้านั่งที่ศาลา — เรียกตอนตาปิด (จอดำ) เท่านั้น
        ///
        /// กฎที่เดินออกไปไกลจากศาลา (กฎ 4 ตุ๊กตา / กฎ 5 สายสิญจน์) ใช้ตัวนี้ตอนตาย: กฎจะ retrigger
        /// แล้ว RuleFlow รอให้นั่งก่อนเริ่ม ถ้าไม่ส่งกลับ ผู้เล่นจะฟื้นอยู่กลางทางตรงจุดที่ตาย
        ///
        /// ใช้จุดที่ยืนก่อนนั่งครั้งล่าสุดเป็นเป้า ไม่ใช่ช่อง Inspector ใหม่ เพราะเป็นจุดเดียวกับที่
        /// StandUpRoutine ส่งกลับอยู่แล้วตอนลุกปกติ — การันตีว่ายืนได้จริงบนพื้นที่มี collider
        /// และไม่ต้องมีใครมาคอยอัปเดตตำแหน่งถ้าย้ายศาลาในซีน
        ///
        /// นั่งอยู่แล้ว = อยู่ที่ศาลาอยู่แล้ว ไม่ต้องย้าย — TeleportTo จะเปิด CharacterController
        /// แล้ว snap ลงพื้น ทำให้ท่านั่งเพี้ยน และ StandUpAfterRule จะพาลุกเองอยู่แล้ว
        /// </summary>
        protected void ReturnPlayerToPavilion()
        {
            var pc = PlayerController.Instance;
            if (pc == null || pc.IsSitting()) return;

            if (!pc.HasSatOnce)
            {
                // ข้าม intro มาด้วย dev skip → ไม่เคยนั่ง → ไม่รู้ว่าศาลาอยู่ไหน
                // ย้ายไป Vector3.zero จะแย่กว่าปล่อยไว้ที่เดิมมาก
                Debug.LogWarning($"[{GetType().Name}] ผู้เล่นยังไม่เคยนั่งในรอบนี้ — ไม่รู้ตำแหน่งศาลา จึงไม่ย้ายกลับ", this);
                return;
            }

            pc.TeleportTo(pc.BeforeSitPosition, pc.BeforeSitRotation);
        }

        protected virtual void Update()
        {
            if (ruleActive) UpdateRule();

            // เตือนสองช่วง: ทำภารกิจครบแล้วรอกลับไปนั่ง (จบกฎ) และอ่านแชทจบแล้วรอนั่ง (เริ่มกฎ)
            // ช่วงหลังกฎ 1 ยังไม่ active (รอ 19:00 / รอนั่ง) จึงต้องเช็กนอกเงื่อนไข ruleActive
            UpdateSitReminder((ruleActive && WaitingForPlayerToSit) || AwaitingSeat);
        }

        /// <summary>
        /// ยังไม่นั่งทั้งที่ควรไปนั่งแล้ว → พูดเตือนเป็นระยะจนกว่าจะนั่ง
        /// มีบทพูด/hint อื่นอยู่บนจอ (เช่น hint สมุด, บทพูดของกฎ) = รอให้หายก่อน ไม่พูดทับ
        /// </summary>
        private void UpdateSitReminder(bool shouldSit)
        {
            var pc = PlayerController.Instance;
            bool waiting = !string.IsNullOrWhiteSpace(sitReminderText) &&
                           shouldSit && pc != null && !pc.IsSitting();

            if (!waiting)
            {
                _sitReminderAt = -1f;
                return;
            }

            if (_sitReminderAt < 0f)
            {
                _sitReminderAt = Time.time + sitReminderFirstDelay;
                return;
            }
            if (Time.time < _sitReminderAt) return;

            var dialogue = PlayerDialogueUI.instance;
            var mode     = GameModeController.instance;
            if (dialogue == null || dialogue.IsShowing || (mode != null && mode.IsBlinking))
            {
                _sitReminderAt = Time.time + 1f;   // จอไม่ว่าง ลองใหม่อีกวินาที
                return;
            }

            dialogue.ShowLine(sitReminderText);
            _sitReminderAt = Time.time + sitReminderInterval;
        }

        protected virtual void UpdateRule() { }
    }
}
