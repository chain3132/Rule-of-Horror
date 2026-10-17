using System;
using System.Collections;
using Enum;
using InputSystem;
using Manager;
using Player;
using Rule6;
using UnityEngine;

namespace RuleSystem.Rule
{
    /// <summary>
    /// กฎข้อที่ 6 — รถประจำทาง (กฎข้อสุดท้าย)
    ///
    /// Flow:
    ///   StartRule() → รอผู้เล่นนั่งที่ศาลา → blink เข้า Tension → เริ่มคิวรถ
    ///   รถวิ่งมาจอดทีละคันตามลำดับ 1 → 2 → 3 → 4 แล้ววนกลับคัน 1
    ///   ผู้เล่นเลือกว่าจะลุกไปขึ้นคันไหน — ขึ้นแล้วคือบทจบ ไม่มีย้อน
    ///
    /// จังหวะของรถ (ตามดีไซน์ข้อ 05-07):
    ///   ไม่ลุกเลย      → รถรอครบ Idle Seconds If Seated แล้วผ่านไป คันถัดไปมาแทน
    ///   ลุกไปเช็ครถ    → รถหยุดรอไม่จำกัด ผู้เล่นเดินดูป้าย/ภายในได้เต็มที่
    ///   กลับไปนั่ง     → รถออกทันที = "ขอดูคันถัดไป"
    ///
    /// นาฬิกาถูกยึดหยุดไว้ทั้งกฎ กฎนี้ไม่ได้ใช้เวลาเป็นตัวจับจังหวะเลย — มันจบเมื่อขึ้นรถ
    /// เท่านั้น ถ้าปล่อยนาฬิกาเดิน RuleManager จะสั่ง EndRule ตอน 24:40 ทั้งที่ผู้เล่นยัง
    /// นั่งอ่านรถอยู่ แล้วคืนนั้นจะจบลงโดยไม่มีบทจบอะไรเลย (เหตุผลเดียวกับ Rule3/Rule5)
    ///
    /// ต่างจากกฎ 1-5 ตรงที่กฎนี้ไม่มีทาง "ตายแล้วเล่นใหม่" — รถทั้ง 4 คันเป็นบทจบคนละแบบ
    /// คันที่ 3 ที่พาไปตายก็เป็นบทจบ ไม่ใช่ game over ที่ rewind เวลากลับ มีแค่คันที่ 4
    /// (วนลูป) ที่เกมเดินต่อ เพราะมันพาผู้เล่นกลับไปเริ่มคืนเดิมใหม่ที่ 18:40
    ///
    /// ดีไซน์เดิมมีคันที่ 5 (รถหลอก — ขึ้นแล้วกลับมาเลือกใหม่) ถอดออกตามที่สั่ง
    /// หน้าที่ของมันคือ "ได้เลือกใหม่" ซึ่งการที่คิวรถวนกลับไปคันที่ 1 เองทำให้อยู่แล้ว
    /// </summary>
    public class Rule6 : RuleBase
    {
        // ════════════════════════════════════════════════════════════════
        #region Inspector
        // ════════════════════════════════════════════════════════════════

        [Header("References")]
        [Tooltip("ส่งต่อให้รถทุกคันไปรับปุ่ม E ตอนผู้เล่นยืนข้างเบาะ")]
        [SerializeField] private InputHandler inputHandler;

        [Tooltip("แป้งที่ยืนหน้าศาลา — จะชี้รถคันที่ 2 (Escape) เองตอนมันมาจอด\n" +
                 "เว้นว่างได้ กฎยังเล่นได้ปกติ แต่ผู้เล่นจะไม่มีเบาะแสว่าคันไหนคือทางรอด")]
        [SerializeField] private PaengBusPointer paeng;

        [Header("รถ")]
        [Tooltip("เรียงตามลำดับที่รถวิ่งมาจอด — ช่อง 0 คือคันที่มาก่อน\n" +
                 "ลำดับนี้ไม่ผูกกับ Kind ของรถ สลับลำดับที่มาได้โดยไม่ต้องแก้ตัวรถ")]
        [SerializeField] private Rule6Bus[] buses;

        [Header("เส้นทาง")]
        [Tooltip("จุดที่รถโผล่มาตอนเริ่มวิ่งเข้า (พ้นสายตาผู้เล่น — หลังโค้ง)")]
        [SerializeField] private Transform approachPoint;

        [Tooltip("จุดจอดหน้าศาลา — ประตูรถควรหันเข้าหาทางเดินที่ผู้เล่นเดินมา")]
        [SerializeField] private Transform stopPoint;

        [Tooltip("จุดที่รถวิ่งหายไป")]
        [SerializeField] private Transform departPoint;

        [SerializeField] private float driveInSeconds  = 5f;
        [SerializeField] private float driveOutSeconds = 4f;

        [Tooltip("เวลาว่างระหว่างรถออกกับคันถัดไปโผล่ (วินาที) — ช่วงที่ถนนเงียบคือช่วงที่น่ากลัวที่สุด")]
        [SerializeField] private float gapBetweenBuses = 7f;

        [Tooltip("เวลาก่อนรถคันแรกมาถึง นับจากกฎเริ่ม (วินาที)")]
        [SerializeField] private float firstBusDelay = 6f;

        [Header("จังหวะที่รถรอ")]
        [Tooltip("รถรอที่ป้ายนานเท่านี้แล้วผ่านไป — นับเฉพาะตอนผู้เล่น \"นั่งอยู่\"\n" +
                 "ลุกไปเช็ครถ = หยุดนับ รถรอไม่จำกัด (ดีไซน์ข้อ 06)")]
        [SerializeField] private float idleSecondsIfSeated = 14f;

        [Tooltip("หลังนั่งลงในรถแล้ว รออีกกี่วินาทีก่อนรถจะออก — ช่วงที่ผู้เล่นได้มองรอบตัว\n" +
                 "ในรถที่ตัวเองเพิ่งเลือก ซึ่งเป็นวินาทีที่สำคัญที่สุดของกฎ")]
        [SerializeField] private float sitSettleSeconds = 1.5f;

        [Header("ตอนรถพาออกไป (ทุกคัน)")]
        [Tooltip("รถวิ่งจากป้ายไปจุด Depart นานเท่านี้ (วินาที) — ผู้เล่นนั่งอยู่ในรถตลอดช่วงนี้\n" +
                 "ไม่ตัดจอดำทันทีที่นั่ง เพราะวินาทีหลังตัดสินใจคือวินาทีที่สำคัญที่สุดของกฎ")]
        [SerializeField] private float rideSeconds = 11f;

        [Tooltip("ขับไปถึงสัดส่วนนี้ของทาง (0-1) แล้วเริ่มหลับตา — 1 = หลับตอนถึงปลายทางพอดี\n" +
                 "0.75 คือใกล้ถึงแล้วแต่ยังไม่ถึง ซึ่งคือจังหวะที่ดีไซน์บอกไว้")]
        [Range(0.1f, 1f)]
        [SerializeField] private float eyesCloseAtRide = 0.75f;

        [Header("บทจบวนลูป (คันที่ 4)")]
        [Tooltip("ค่อยๆ ขับจากจุด Approach เข้ามาจอดที่ศาลา (วินาที)\n" +
                 "ช้ากว่ารถคันอื่นเพราะนี่คือฉากเปิดคืนใหม่ ผู้เล่นต้องได้เห็นศาลาค่อยๆ โตขึ้น")]
        [SerializeField] private float loopArriveSeconds = 14f;

        [Tooltip("ข้อความ hint ตอนนั่งอยู่ในรถที่จอดแล้ว")]
        [SerializeField] private string getOffHint = "E   ลงจากรถ";

        [Tooltip("ยืนห่างจากเบาะเท่านี้ (เมตร) = ลงจากรถแล้ว รถจึงออกได้\n" +
                 "ต้องรอ \"เดินออก\" ไม่ใช่แค่ \"ลุก\" ไม่งั้นรถจะวิ่งหายไปทิ้งผู้เล่นยืนอยู่กลางถนน")]
        [SerializeField] private float offBusDistance = 4.5f;

        [Tooltip("หลังผู้เล่นลงจากรถแล้ว รออีกเท่านี้รถจึงค่อยๆ ขับออกไป (วินาที)")]
        [SerializeField] private float loopDepartDelay = 2f;

        [Header("ศาลา")]
        [Tooltip("ม้านั่งที่ศาลา — ใช้วางผู้เล่นกลับตอนบทจบกับดักและบทจบวนลูป\n" +
                 "เว้นว่าง = ไม่ย้าย (ผู้เล่นจะยืนค้างอยู่ตรงประตูรถตอนบทจบ)")]
        [SerializeField] private Transform pavilionSeat;

        [Header("บทพูด")]
        [Tooltip("บทพูดตอนกฎเริ่ม")]
        [SerializeField] private DialogueLine[] introLines;

        [Tooltip("คันที่ 1 กับดัก — แป้งถูกทิ้งซ้ำ เราเป็นคนส่งข้อความคนต่อไป")]
        [SerializeField] private DialogueLine[] trapLines;

        [Tooltip("คันที่ 2 ทางรอด — ระหว่างขึ้นรถมีสายจากคนส่งข้อความด่าว่าทำอะไรลงไป")]
        [SerializeField] private DialogueLine[] escapeLines;

        [Tooltip("คันที่ 3 ความตาย")]
        [SerializeField] private DialogueLine[] deathLines;

        [Tooltip("คันที่ 4 วนลูป — เวลาย้อนกลับไป 18:40 แต่แชทเก่ายังอยู่")]
        [SerializeField] private DialogueLine[] loopLines;

        [Header("บทจบ")]
        [Tooltip("object ที่จะเปิดตอนบทจบนั้นมาถึง (แผ่น UI / cutscene / credits)\n" +
                 "เว้นว่างได้ — กฎจะ log บทจบไว้ให้และยิง event OnEnding ออกมาอยู่ดี")]
        [SerializeField] private GameObject trapEndingObject;
        [SerializeField] private GameObject escapeEndingObject;
        [SerializeField] private GameObject deathEndingObject;
        [SerializeField] private GameObject loopEndingObject;

        [Header("วนลูป (คันที่ 4)")]
        [Tooltip("เวลาที่ย้อนกลับไปเริ่มคืนใหม่")]
        [SerializeField] private int loopRestartHour   = 18;
        [SerializeField] private int loopRestartMinute = 40;

        [Tooltip("RuleManager — ต้องรีเซ็ต ruleActive ของกฎทุกข้อ ไม่งั้นคืนใหม่จะไม่มีกฎไหนเริ่มเลย\n" +
                 "เว้นว่าง = จะไปหาในซีนเอง")]
        [SerializeField] private RuleManager ruleManager;

        [Tooltip("Rule1 — คืนรอบสองไม่มีข้อความเด้งแล้ว จึงไม่มีใครปลุกกฎ 1 ให้เหมือนคืนแรก\n" +
                 "(ปกติ FriendListController เรียกตอนผู้เล่นอ่านแชทกฎจบ) กฎ 6 ต้องเรียกเอง\n" +
                 "เว้นว่าง = จะไปหาในซีนเอง")]
        [SerializeField] private Rule1 rule1;

        // หมายเหตุ: กฎนี้ไม่มีช่อง Game Over Reset เหมือนกฎ 2-5 โดยตั้งใจ
        // รถคันที่ 3 ที่พาไปตายคือบทจบ ไม่ใช่ game over ที่ rewind เวลากลับมาให้เลือกใหม่

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Runtime
        // ════════════════════════════════════════════════════════════════

        private bool      _gameplayActive;
        private bool      _isEnding;
        private bool      _resolved;
        private int       _busIndex;
        private Rule6Bus  _currentBus;
        private Coroutine _cycle;
        private Coroutine _flow;

        /// <summary>รถที่ผู้เล่นขึ้น (null = ยังไม่ขึ้นคันไหน) — บทจบของคืนนี้</summary>
        public Rule6BusKind? Resolution { get; private set; }

        /// <summary>true = กฎกำลังเล่นอยู่จริง (ผ่าน blink เข้ามาแล้ว ยังไม่ขึ้นรถ)</summary>
        public bool IsGameplayActive => _gameplayActive;

        /// <summary>รถที่จอดอยู่ที่ป้ายตอนนี้ (null = ถนนว่าง) — ใช้โดย Rule6DevSkip</summary>
        public Rule6Bus CurrentBus => _currentBus;

        /// <summary>
        /// จำนวนครั้งที่ผู้เล่นขึ้นรถคันที่ 4 แล้ววนกลับมาเริ่มคืนใหม่
        ///
        /// static เพราะมันต้องอยู่รอดข้ามการเริ่มคืนใหม่ ซึ่ง component ตัวนี้ถูกรีเซ็ตด้วย
        /// ดีไซน์หน้าคันที่ 4 บอกว่า "ครั้งนี้หากผู้เล่นตายแม้แต่รอบเดียว เราจะไม่มีวันกลับมาที่
        /// ศาลานี้อีกเลย" — ค่านี้คือตัวที่กฎ 1-5 ต้องมาอ่านเพื่อทำกติกานั้น ยังไม่มีใครอ่าน
        /// </summary>
        public static int NightLoopCount { get; private set; }

        /// <summary>
        /// ล้างตัวนับตอนเริ่ม Play ทุกครั้ง
        ///
        /// static ไม่ถูกล้างให้เองถ้าปิด Reload Domain ใน Enter Play Mode Settings ซึ่งเปิดกัน
        /// บ่อยเพราะมันทำให้กด Play เร็วขึ้นมาก — ถ้าไม่ล้าง รอบที่สองของการกด Play จะกลายเป็น
        /// "คืนรอบสอง" ทั้งที่เพิ่งเริ่มเกม แล้วตายครั้งแรกก็จบเลยโดยไม่มีสาเหตุที่หาได้
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetNightLoopCount() => NightLoopCount = 0;

        /// <summary>ยิงตอนบทจบมาถึง — ตัวเชื่อมสำหรับระบบ ending / credits ที่จะมาทีหลัง</summary>
        public event Action<Rule6BusKind> OnEnding;

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Debug hooks (Rule6DevSkip เท่านั้น)
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// DEBUG: ตั้งตัวนับคืนที่วนลูปมาแล้วด้วยมือ
        ///
        /// มีเพราะการเทสกติกา "คืนรอบสองตายรอบเดียวจบ" ตามปกติต้องเล่นให้จบคืนแรกทั้งคืน
        /// แล้วขึ้นรถคันที่ 4 ก่อน ซึ่งเป็นหลายนาทีต่อการเทสเงื่อนไขที่อยู่ในกฎ 1-5 หนึ่งข้อ
        /// </summary>
        public static void DebugSetNightLoopCount(int value) => NightLoopCount = Mathf.Max(0, value);

        /// <summary>จำนวนช่องรถใน Inspector — ใช้วาด HUD ของปุ่มเทส</summary>
        public int BusCount => buses?.Length ?? 0;

        /// <summary>รถในช่องที่ i (null = ช่องว่าง)</summary>
        public Rule6Bus BusAt(int i) =>
            buses != null && i >= 0 && i < buses.Length ? buses[i] : null;

        /// <summary>
        /// DEBUG: เริ่มเล่นกฎเดี๋ยวนี้ โดยไม่รอให้ผู้เล่นไปนั่งที่ศาลาก่อน
        ///
        /// RuleFlow ติด WaitUntil(PlayerIsSitting) เป็นบรรทัดแรกเหมือนกฎ 2/3/5 ซึ่งถูกต้อง
        /// สำหรับเกมจริง แต่ตอนเทสมันหมายถึงต้องเดินกลับไปนั่งทุกครั้งที่กด Play
        ///
        /// ต้องหยุด RuleFlow ที่ค้างรออยู่ด้วย ไม่ใช่แค่เรียก StartGameplay() เฉยๆ — ไม่งั้น
        /// พอผู้เล่นเดินไปนั่งทีหลัง RuleFlow จะตื่นมาเรียก StartGameplay() ซ้ำรอบสอง
        /// แล้วคิวรถจะเดินสองชุดพร้อมกัน รถสองคันจอดทับกันที่ป้ายเดียว
        /// </summary>
        public bool DebugForceStartGameplay()
        {
            if (_gameplayActive) return true;
            if (!ruleActive) return false;

            if (_flow != null)
            {
                StopCoroutine(_flow);
                _flow = null;
            }

            // เข้าโหมด Tension ให้บรรยากาศถูก แต่ไม่รอ blink จบ — StartGameplay ยึดนาฬิกาเอง
            // และ UpdateRule ย้ำหยุดทุกเฟรม จังหวะที่ blink ปล่อยนาฬิกาจึงไม่มีผล
            GameModeController.instance.BlinkToMode(GameMode.Tension);
            StartGameplay();

            return _gameplayActive;
        }

        /// <summary>
        /// DEBUG: ข้ามคิวไปเรียกรถคันที่ต้องการมาจอดเลย
        ///
        /// มีเพราะการเทสบทจบทั้ง 4 แบบด้วยการนั่งรอคิวจริงใช้เวลาเป็นนาทีต่อหนึ่งคัน และคันที่
        /// อยากเทสซ้ำที่สุด (คันที่ 4 วนลูป) อยู่ท้ายคิว
        /// </summary>
        public bool DebugSummonBus(int index)
        {
            if (!_gameplayActive || _resolved) return false;
            if (BusAt(index) == null) return false;

            if (_cycle != null)
            {
                StopCoroutine(_cycle);
                _cycle = null;
            }

            // รถที่จอดอยู่เก็บทันทีไม่ต้องรออนิเมชั่นออก — นี่คือปุ่มเทส ไม่ใช่จังหวะของเกม
            foreach (var bus in buses)
                if (bus != null) bus.Hide();

            if (paeng != null) paeng.StopPointing();
            _currentBus = null;

            _busIndex = (index + 1) % buses.Length;
            _cycle    = StartCoroutine(DebugBusThenResume(buses[index]));
            return true;
        }

        IEnumerator DebugBusThenResume(Rule6Bus bus)
        {
            yield return RunBus(bus);
            if (!_gameplayActive || _resolved) yield break;

            yield return new WaitForSeconds(gapBetweenBuses);
            yield return BusLoop();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Lifecycle
        // ════════════════════════════════════════════════════════════════

        public override void StartRule()
        {
            base.StartRule();
            _flow = StartCoroutine(RuleFlow());
        }

        bool PlayerIsSitting()  => PlayerController.Instance.IsSitting();
        bool PlayerEyesOpened() => GameModeController.instance.IsEyesOpen;

        IEnumerator RuleFlow()
        {
            TimeManager.instance.IsPauseTime(true);

            yield return new WaitUntil(PlayerIsSitting);

            GameModeController.instance.BlinkToMode(GameMode.Tension);
            yield return null;
            yield return new WaitUntil(() => !PlayerEyesOpened());
            yield return new WaitUntil(PlayerEyesOpened);

            StartGameplay();
        }

        protected override void UpdateRule()
        {
            if (!_gameplayActive) return;

            // กฎจบด้วยการขึ้นรถเท่านั้น — ยึดนาฬิกาไว้ (BlinkRoutine ปล่อยได้ทุกเมื่อ)
            if (!TimeManager.instance.IsPaused) TimeManager.instance.IsPauseTime(true);

            AudioManager.instance.UpdateHeartbeat();
        }

        /// <summary>
        /// กฎนี้ไม่มีทางจบเองตามเวลา — EndRule จาก RuleManager จึงหมายถึง "มีอะไรผิด"
        ///
        /// ยอมจบเฉพาะตอนบทจบเล่นแล้ว (Resolution มีค่า) หรือตอนถูกสั่งก่อนกฎเริ่มเล่น
        /// นอกนั้นปฏิเสธแล้วย้ำหยุดนาฬิกา เหมือนที่ Rule5 ทำ
        /// </summary>
        public override void EndRule()
        {
            if (_isEnding) return;

            if (_gameplayActive && !_resolved)
            {
                Debug.LogWarning("[Rule6] ถูกสั่ง EndRule ทั้งที่ยังเลือกรถอยู่ — เมิน " +
                                 "(กฎจบเมื่อขึ้นรถคันใดคันหนึ่งเท่านั้น)", this);
                TimeManager.instance.IsPauseTime(true);
                return;
            }

            _isEnding       = true;
            _gameplayActive = false;

            CleanupGameplay();

            PlayerController.Instance.isBlockStanding = false;
            TimeManager.instance.IsPauseTime(false);

            base.EndRule();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Gameplay
        // ════════════════════════════════════════════════════════════════

        void StartGameplay()
        {
            // ต้อง pause ซ้ำตรงนี้ — BlinkRoutine สั่ง IsPauseTime(false) ทุกครั้งที่ตาเปิดสุด
            // ค่าที่ RuleFlow ตั้งไว้ตอนต้นจึงถูกล้างไปแล้วพอดีตอนที่ถึงบรรทัดนี้
            TimeManager.instance.IsPauseTime(true);

            _isEnding       = false;
            _resolved       = false;
            _gameplayActive = true;
            _busIndex       = 0;
            _currentBus     = null;
            Resolution      = null;

            // กฎนี้ต้องลุกได้ — ดีไซน์บังคับให้เดินไปเช็ครถถึงจะอ่านป้าย/ภายในออก
            // (ต่างจากกฎ 3 กับ 5 ที่ล็อกให้นั่งอยู่กับที่)
            PlayerController.Instance.isBlockStanding = false;
            AudioManager.instance.SilenceHeartbeat();

            if (buses == null || buses.Length == 0)
            {
                Debug.LogError("[Rule6] ยังไม่ได้ใส่รถในช่อง Buses — ไม่มีรถคันไหนมาเลย", this);
                return;
            }
            if (approachPoint == null || stopPoint == null || departPoint == null)
            {
                Debug.LogError("[Rule6] ยังไม่ได้ใส่จุด Approach / Stop / Depart — รถไม่รู้จะวิ่งไปไหน", this);
                return;
            }

            foreach (var bus in buses)
                if (bus != null) bus.Setup(this, inputHandler);

            if (paeng != null) paeng.Appear();

            AudioManager.instance.StartRule6Background();

            if (introLines != null && introLines.Length > 0 && PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowSequence(introLines);

            _cycle = StartCoroutine(BusCycleRoutine());
        }

        /// <summary>
        /// คิวรถ — วนไม่จำกัดจนผู้เล่นขึ้นคันใดคันหนึ่ง
        ///
        /// ใช้ yield return ของ IEnumerator ตรงๆ ไม่ใช่ StartCoroutine ซ้อน เพราะตอนผู้เล่น
        /// ขึ้นรถ TryBoardBus จะ StopCoroutine(_cycle) — coroutine ที่ซ้อนด้วย StartCoroutine
        /// เป็นตัวแยกอิสระ หยุดตัวนอกแล้วตัวในวิ่งต่อ รถจะขับออกไปกลางบทจบ
        /// </summary>
        IEnumerator BusCycleRoutine()
        {
            yield return new WaitForSeconds(firstBusDelay);
            yield return BusLoop();
        }

        /// <summary>คิวรถล้วนๆ ไม่มีหน่วงคันแรก — แยกออกมาเพื่อให้ปุ่มเทสเรียกต่อเองได้</summary>
        IEnumerator BusLoop()
        {
            while (_gameplayActive && !_resolved)
            {
                Rule6Bus bus = NextBus();
                if (bus == null) yield break;

                yield return RunBus(bus);
                if (!_gameplayActive || _resolved) yield break;

                yield return new WaitForSeconds(gapBetweenBuses);
            }
        }

        /// <summary>
        /// คันถัดไปตามลำดับ แล้ววนกลับคันที่ 1
        ///
        /// การวนกลับคือสิ่งที่มาแทนคันที่ 5 (รถหลอก) ที่ถอดออกแล้ว — ผู้เล่นที่ปล่อยผ่านครบ
        /// ทั้ง 4 คันยังได้ดูใหม่ไม่จำกัด ไม่มีทางติดอยู่ที่ศาลาโดยไม่มีรถมาอีก
        /// </summary>
        Rule6Bus NextBus()
        {
            for (int i = 0; i < buses.Length; i++)
            {
                Rule6Bus candidate = buses[_busIndex % buses.Length];
                _busIndex = (_busIndex + 1) % buses.Length;

                if (candidate != null) return candidate;
            }

            Debug.LogError("[Rule6] ช่อง Buses มีแต่ช่องว่าง — ไม่มีรถคันไหนมาเลย", this);
            return null;
        }

        /// <summary>รถคันหนึ่ง: วิ่งเข้า → จอดรอ → วิ่งออก</summary>
        IEnumerator RunBus(Rule6Bus bus)
        {
            _currentBus = bus;

            bus.Appear();
            bus.transform.SetPositionAndRotation(approachPoint.position, approachPoint.rotation);
            AudioManager.instance.PlayRule6BusArrive(approachPoint.position);

            yield return Drive(bus.transform, approachPoint, stopPoint, driveInSeconds, decelerate: true);

            // รถไม่มีประตู — จอดแล้วเดินขึ้นได้เลย ธงนี้คือสิ่งเดียวที่กันการขึ้นรถตอนมันยังวิ่ง
            bus.SetBoardable(true);

            // แป้งชี้เฉพาะคันที่เป็นทางรอด — เบาะแสเดียวของกฎนี้
            if (bus.Kind == Rule6BusKind.Escape && paeng != null) paeng.PointAt(bus.transform);

            yield return WaitAtStop();

            // ขึ้นรถแล้ว — ปล่อยให้ ResolveRoutine เป็นเจ้าของรถคันนี้ต่อ ห้ามขับออก
            if (_resolved || !_gameplayActive) yield break;

            if (paeng != null) paeng.StopPointing();

            bus.SetBoardable(false);
            AudioManager.instance.PlayRule6BusDepart(bus.transform.position);

            yield return Drive(bus.transform, stopPoint, departPoint, driveOutSeconds, decelerate: false);

            bus.Hide();
            _currentBus = null;
        }

        /// <summary>
        /// รถจอดรออยู่ที่ป้าย — คืนเมื่อถึงเวลาปล่อยผ่าน (หรือผู้เล่นขึ้นรถ)
        ///
        /// ตัวจับเวลาเดินเฉพาะตอนผู้เล่นนั่งอยู่ที่ศาลา: ลุกไปยืนดูรถ = รถรอไม่จำกัด
        /// และถ้าลุกไปแล้วกลับมานั่ง = บอกว่า "ขอดูคันถัดไป" รถออกทันทีไม่ต้องรอครบเวลา
        /// </summary>
        IEnumerator WaitAtStop()
        {
            bool  leftSeat = false;
            float idle     = idleSecondsIfSeated;

            while (_gameplayActive && !_resolved)
            {
                if (!PlayerIsSitting())
                {
                    leftSeat = true;
                    yield return null;
                    continue;
                }

                if (leftSeat) yield break;    // ลุกไปดูแล้วกลับมานั่ง = ขอดูคันถัดไป

                idle -= Time.deltaTime;
                if (idle <= 0f) yield break;  // ไม่ลุกเลย = ปล่อยผ่านไปตามลำดับ

                yield return null;
            }
        }

        /// <summary>
        /// ขับรถจากจุดหนึ่งไปอีกจุด — ตัวจริงอยู่ใน BusRide ที่ใช้ร่วมกับฉากเปิดเกม
        ///
        /// decelerate = true ตอนเข้าจอด (ช้าลงท้ายทาง) / false ตอนออก (ค่อยๆ เร่ง)
        /// </summary>
        IEnumerator Drive(Transform bus, Transform from, Transform to,
                          float seconds, bool decelerate, bool carryPlayer = false)
            => _ride.Drive(bus, from, to, seconds, decelerate, carryPlayer);

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region พาผู้เล่นไปกับรถ
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// ตัวพาผู้เล่นไปกับรถ + ตัวขับรถ — ตัวเดียวกับที่ฉากเปิดเกมใช้
        ///
        /// อยู่ใน BusRide ไม่ได้อยู่ในนี้ เพราะฉากเปิดเกมคือฉากเดียวกับบทจบวนลูป (รถพาเรามา
        /// ถึงศาลาตอน 18:40 แล้วเราลงจากรถ) ถ้าเขียนแยกกันสองที่ สองฉากนั้นจะค่อยๆ ไม่เหมือน
        /// กันทุกครั้งที่แก้ข้างใดข้างหนึ่ง แล้วมุก "คืนเดิมซ้ำ" จะพัง
        /// </summary>
        private readonly BusRide _ride = new BusRide();

        void BeginCarry(Transform bus) => _ride.BeginCarry(bus);
        void CarryStep()               => _ride.Step();
        void EndCarry()                => _ride.End();

        /// <summary>ย้ายรถทั้งคันไปจุดใหม่ พร้อมผู้เล่นที่นั่งอยู่ในนั้น — ใช้ตอนจอดำ</summary>
        void PlaceBusAt(Rule6Bus bus, Transform point) => _ride.PlaceAt(bus.transform, point);

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Boarding
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// เรียกโดย Rule6Bus เมื่อผู้เล่นกด E ที่ประตูรถ
        ///
        /// คืน false = ปฏิเสธ (มีบทจบเล่นอยู่แล้ว) เพื่อให้รถไม่มาร์กตัวเองว่าขึ้นแล้ว
        /// กันเคสรถสองคันจอดทับกันแล้วผู้เล่นอยู่ในวงที่นั่งของทั้งคู่พร้อมกัน
        /// </summary>
        public bool TryBoardBus(Rule6Bus bus)
        {
            if (bus == null || !_gameplayActive || _resolved) return false;

            _resolved  = true;
            Resolution = bus.Kind;

            if (_cycle != null)
            {
                StopCoroutine(_cycle);
                _cycle = null;
            }

            AudioManager.instance.PlayRule6Board(bus.transform.position);
            Debug.Log($"[Rule6] ผู้เล่นขึ้นรถ '{bus.name}' — บทจบ {bus.Kind}", this);

            StartCoroutine(ResolveRoutine(bus));
            return true;
        }

        IEnumerator ResolveRoutine(Rule6Bus bus)
        {
            _gameplayActive = false;   // หยุด UpdateRule + หยุดไม่ให้คันถัดไปมา

            if (paeng != null) paeng.StopPointing();

            bus.SetBoardable(false);
            AudioManager.instance.StopRule6Background();

            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            // พาไปนั่งจริงก่อนบทจบ — ผู้เล่นกด E แล้ว แต่ภาพที่ต้องเห็น
            // คือตัวเองนั่งอยู่ในรถคันนั้น ไม่ใช่ยืนค้างกลางทางเดินแล้วจอดับ
            yield return SitInBus(bus);

            // ไม่ล็อกการหันหน้า — ทั้งบทจบคือการนั่งอยู่ในรถที่กำลังเคลื่อน ผู้เล่นต้องหันไปดู
            // คนที่นั่งอยู่รอบตัว (หรือดูที่นั่งว่างทั้งคัน) ได้ ไม่งั้นก็เท่ากับตัดจอดำทันทีที่นั่ง
            // เดินไม่ได้อยู่แล้วเพราะนั่ง — SetMovement(false) แค่กันเคส SitInBus ไม่สำเร็จ
            var player = PlayerController.Instance;
            player.SetMovement(false);

            switch (bus.Kind)
            {
                case Rule6BusKind.Trap:   yield return TrapEnding(bus);    break;
                case Rule6BusKind.Escape: yield return EscapeEnding(bus);  break;
                case Rule6BusKind.Death:  yield return DeathEnding(bus);   break;
                default:                  yield return LoopEnding(bus);    break;
            }
        }

        /// <summary>
        /// พาผู้เล่นไปนั่งที่ที่นั่งของรถคันนั้น — ตัวจริงอยู่ใน BusRide
        ///
        /// ถ้าพาไปนั่งไม่สำเร็จจะเล่นบทจบจากท่ายืนแทน ไม่ยกเลิกบทจบ: ยืนอยู่ในรถที่กำลังวิ่ง
        /// ออกไปยังอ่านได้ แต่ถ้าหยุดกลางทางผู้เล่นจะค้างอยู่ในรถที่ไม่ไปไหนเลย
        /// </summary>
        IEnumerator SitInBus(Rule6Bus bus) => _ride.Sit(bus, sitSettleSeconds, this);

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Endings
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// ช่วงที่ทุกบทจบใช้ร่วมกัน: รถออกจากป้ายพร้อมผู้เล่นที่นั่งอยู่ในนั้น แล้วหลับตาตอน
        /// ใกล้ถึงปลายทาง
        ///
        /// ไม่ตัดจอดำทันทีที่นั่ง เพราะวินาทีที่สำคัญที่สุดของกฎนี้คือวินาทีหลังตัดสินใจ —
        /// ผู้เล่นได้นั่งอยู่ในรถที่ตัวเองเลือก มองคนที่นั่งอยู่รอบตัว (หรือมองที่นั่งว่างทั้งคัน)
        /// ขณะที่รถเคลื่อนออกไปแล้วเปลี่ยนใจไม่ได้อีก
        ///
        /// onEyesClosed ยิงตอนจอดำสนิท ก่อนตาเปิดอีกครั้ง — แต่ละบทจบใช้จังหวะนั้นทำของตัวเอง
        /// </summary>
        IEnumerator RideAway(Rule6Bus bus, Action onEyesClosed)
        {
            AudioManager.instance.PlayRule6BusDepart(bus.transform.position);
            BeginCarry(bus.transform);

            bool blinkStarted = false;
            bool blackedOut   = false;

            // ห่อ callback เพื่อรู้ว่า "จอดำสนิทแล้ว" จริงๆ — ใช้ IsEyesOpen เป็นสัญญาณไม่ได้
            // เพราะ BlinkRoutine ตั้ง IsEyesOpen = false เป็นบรรทัดแรก คือตอนเปลือกตา
            // "เริ่ม" ปิด ไม่ใช่ปิดสนิท ถ้าเช็กค่านั้นรถจะหยุดขับทั้งที่จอยังสว่างอยู่
            void Wrapped()
            {
                blackedOut = true;
                onEyesClosed?.Invoke();
            }

            float elapsed = 0f;
            while (elapsed < rideSeconds)
            {
                elapsed += Time.deltaTime;

                float eased = Mathf.Clamp01(elapsed / rideSeconds);
                eased *= eased;                      // ออกตัวช้าแล้วค่อยเร่ง

                bus.transform.SetPositionAndRotation(
                    Vector3.Lerp(stopPoint.position, departPoint.position, eased),
                    Quaternion.Slerp(stopPoint.rotation, departPoint.rotation, eased));

                CarryStep();

                if (!blinkStarted && eased >= eyesCloseAtRide)
                {
                    blinkStarted = true;
                    GameModeController.instance.DirectBlinkToMode(GameMode.Relax, Wrapped);
                }

                // จอดำแล้ว หยุดขับทันที — และต้องหยุด เพราะ callback ของคันที่ 4 ย้ายรถไป
                // จุดเริ่มต้นแล้ว ขับต่ออีกเฟรมเดียวจะลากมันกลับมาที่ป้ายทันที
                if (blackedOut) yield break;

                yield return null;
            }

            // ขับครบทางแล้วแต่ยังไม่ถึงคิวหลับตา (eyesCloseAtRide ตั้งไว้ 1.0) — ปิดตาตรงนี้
            if (!blinkStarted)
                GameModeController.instance.DirectBlinkToMode(GameMode.Relax, Wrapped);

            yield return new WaitUntil(() => blackedOut);
        }

        /// <summary>
        /// ปลายทางของคันที่ 1-3 — ที่ควรเป็นคัทซีนเนื้อเรื่องตอนจบ ซึ่งยังไม่มี
        ///
        /// ระหว่างที่ยังไม่มี ตัวผู้เล่นต้องไปอยู่ที่ที่นิยามได้ ไม่ใช่ลอยค้างอยู่ปลายถนนตรงจุดที่
        /// รถหายไป เลยส่งกลับมานั่งที่ม้านั่งศาลา ซึ่งถูกสำหรับคันที่ 1 อยู่แล้วตามดีไซน์
        /// ("ผู้เล่นจะกลับมานั่งที่ศาลา แล้วจบด้วยการที่ผู้เล่นส่งข้อความให้คนต่อไป") และสำหรับ
        /// คันที่ 2-3 ก็มีจอบทจบคลุมอยู่ข้างหน้าอยู่ดี
        /// </summary>
        void ParkPlayerForCutscene(Rule6Bus bus)
        {
            EndCarry();
            bus.Hide();
            ReturnPlayerToSeat();
        }

        /// <summary>
        /// คันที่ 1 — กับดัก
        ///
        /// รถเหลือที่เดียว มีที่ให้เราแต่ไม่มีที่ให้แป้ง ขึ้นแล้วลืมตามาอยู่บนม้านั่งศาลาเหมือน
        /// ไม่เคยไปไหน — เพราะเราไม่ได้ไปไหนจริงๆ เราแค่เปลี่ยนที่นั่งกับคนส่งข้อความ
        /// </summary>
        IEnumerator TrapEnding(Rule6Bus bus)
        {
            yield return RideAway(bus, () => ParkPlayerForCutscene(bus));
            yield return new WaitUntil(PlayerEyesOpened);

            HoldClock();
            yield return ShowLines(trapLines);

            FinishEnding(Rule6BusKind.Trap, trapEndingObject);
        }

        /// <summary>
        /// คันที่ 2 — ทางรอด
        ///
        /// รถว่างทั้งคันจึงมีที่ให้แป้งด้วย ระหว่างรถเคลื่อนออกมีสายเข้าจากคนส่งข้อความด่าว่า
        /// ทำอะไรลงไป — เสียงเดียวในเกมที่มันเสียการควบคุม แล้ว loop นี้ก็จบลงตลอดกาล
        ///
        /// สายเข้าดังตั้งแต่รถเริ่มออก ไม่ใช่หลังจอดำ: ดีไซน์บอกว่า "ระหว่างขึ้นรถ" และช่วงนั้น
        /// เป็นช่วงเดียวที่ผู้เล่นยังเห็นในรถอยู่ — เสียงจะได้มีที่ให้ดังทับ
        /// </summary>
        IEnumerator EscapeEnding(Rule6Bus bus)
        {
            AudioManager.instance.PlayRule6PhoneRing();

            yield return RideAway(bus, () => ParkPlayerForCutscene(bus));
            yield return new WaitUntil(PlayerEyesOpened);

            HoldClock();
            yield return ShowLines(escapeLines);

            FinishEnding(Rule6BusKind.Escape, escapeEndingObject);
        }

        /// <summary>
        /// คันที่ 3 — ความตาย
        ///
        /// ไม่ใช้ PlayDeathFallRoutine เหมือนกฎอื่น เพราะท่าตายของเกมนี้คือกล้องล้มลงกับพื้น
        /// ซึ่งอ่านไม่ออกเลยตอนนั่งอยู่บนเบาะรถที่กำลังวิ่ง เสียงตายตอนจอกำลังดำทำงานแทน
        ///
        /// และไม่ rewind เวลาแล้วให้เล่นใหม่ — นี่คือบทจบ ไม่ใช่ game over คนอื่นยังติดอยู่ใน
        /// loop เดิม มีแค่เราที่ตาย
        /// </summary>
        IEnumerator DeathEnding(Rule6Bus bus)
        {
            yield return RideAway(bus, () =>
            {
                AudioManager.instance.PlayRule3Death();
                ParkPlayerForCutscene(bus);
            });

            yield return new WaitUntil(PlayerEyesOpened);

            HoldClock();
            yield return ShowLines(deathLines);

            FinishEnding(Rule6BusKind.Death, deathEndingObject);
        }

        /// <summary>
        /// คันที่ 4 — วนลูป
        ///
        /// บทจบเดียวที่เกมเดินต่อ และบทจบเดียวที่ต้องเล่นให้จบเป็นภาพ ไม่ใช่ตัดไปคัทซีน:
        ///
        ///   ① รถออกจากป้ายพร้อมเรา ขับไปจนใกล้ปลายทาง → หลับตา
        ///   ② จอดำ: รถ (ที่เรานั่งอยู่) ถูกย้ายกลับไปจุดเริ่มต้น + เวลาย้อนเป็น 18:40
        ///   ③ ลืมตา — เรานั่งอยู่ในรถที่กำลังวิ่งเข้ามา แล้วมันค่อยๆ จอดที่ศาลา
        ///   ④ กด E ลุกจากเบาะ เดินลงจากรถ
        ///   ⑤ รถค่อยๆ ขับออกไป เหลือเราคนเดียวที่ศาลาตอน 18:40 — คืนใหม่เริ่มแล้ว
        ///
        /// วงกลมปิดพอดี: คืนนี้เริ่มด้วยการที่เรามาถึงศาลา และจบด้วยการที่เรามาถึงศาลา
        /// ป้ายหน้ารถคันนี้เขียนว่า "ศาลา" มาตั้งแต่แรก มันไม่ได้โกหก
        /// </summary>
        IEnumerator LoopEnding(Rule6Bus bus)
        {
            // ① + ② ออกจากป้าย แล้วตอนจอดำย้ายทั้งคันกลับไปจุดเริ่มต้นพร้อมรีเซ็ตคืน
            yield return RideAway(bus, () =>
            {
                PlaceBusAt(bus, approachPoint);
                RestartNight();
            });

            yield return new WaitUntil(PlayerEyesOpened);

            NightLoopCount++;
            Debug.Log($"[Rule6] เริ่มคืนเดิมใหม่รอบที่ {NightLoopCount} ที่ " +
                      $"{loopRestartHour:00}:{loopRestartMinute:00}", this);

            // ปล่อยบทพูดให้เล่นทับช่วงรถวิ่งเข้ามา ไม่ใช่รอให้จบก่อนรถจะออกตัว — ไม่งั้นผู้เล่น
            // จะนั่งอ่านข้อความอยู่ในรถที่จอดนิ่งอยู่ปลายถนนตรงจุด Approach ซึ่งอ่านเป็นเกมค้าง
            if (loopLines != null && loopLines.Length > 0 && PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowSequence(loopLines);

            // ③ ค่อยๆ ขับเข้ามาจอดที่ศาลา — ช้ากว่ารถคันอื่นเพราะนี่คือฉากเปิดคืนใหม่ ไม่ใช่
            //    รถที่มารับ ผู้เล่นต้องได้เห็นศาลาค่อยๆ โตขึ้นในหน้าต่างจากฝั่งที่ไม่เคยเห็น
            yield return Drive(bus.transform, approachPoint, stopPoint,
                               loopArriveSeconds, decelerate: true, carryPlayer: true);

            // ④ ลงจากรถ
            yield return LeaveBus(bus);

            // ⑤ รถออกไปเงียบๆ — คันนี้ไม่มีเสียงเครื่องยนต์ตั้งแต่แรก
            yield return Drive(bus.transform, stopPoint, departPoint, driveOutSeconds,
                               decelerate: false);
            bus.Hide();

            // ไม่ HoldClock — คืนใหม่ต้องเดินต่อได้ (ถ้ามีข้อความกฎข้อ 1 ส่งถึงตอน 18:40
            // FriendListController จะหยุดนาฬิกาเองจนกว่าผู้เล่นจะอ่านแชทจบ)
            FinishEnding(Rule6BusKind.Loop, loopEndingObject, holdClock: false, warnIfMissing: false);

            // ⑥ คืนใหม่เริ่มเลย ไม่ต้องรอข้อความ
            yield return StartNightOverAgain();
        }

        /// <summary>
        /// เริ่มกฎ 1 ของคืนรอบสองด้วยตัวเอง
        ///
        /// คืนแรก กฎ 1 ถูกปลุกโดย FriendListController ตอนผู้เล่นอ่านแชทกฎจบ แต่คืนรอบสอง
        /// ไม่มีข้อความเด้งอีกแล้ว (ของเดิมยังอยู่ในโทรศัพท์ครบ) ถ้าไม่เรียกตรงนี้ ผู้เล่นจะยืน
        /// อยู่ที่ศาลาตอน 18:40 โดยไม่มีอะไรเกิดขึ้นเลยตลอดไป เพราะนาฬิกาก็ถูกยึดไว้ด้วย
        ///
        /// blink เข้า Tension ก่อนเหมือนที่ FriendListController ทำ — กฎ 1 ทำงานในโหมด Tension
        /// และ EndRule ของมัน blink กลับ Relax ตอนจบ ถ้าเริ่มจาก Relax บรรยากาศจะกลับด้าน
        /// ทั้งกฎ แล้วตอนจบจะ blink ไป Relax ทั้งที่อยู่ Relax อยู่แล้ว
        /// </summary>
        IEnumerator StartNightOverAgain()
        {
            if (rule1 == null) rule1 = FindAnyObjectByType<Rule1>(FindObjectsInactive.Include);

            if (rule1 == null)
            {
                Debug.LogError("[Rule6] ไม่เจอ Rule1 ในซีน — คืนรอบสองจะไม่มีกฎข้อไหนเริ่มเลย " +
                               "เพราะไม่มีข้อความเด้งมาปลุกให้แล้ว", this);
                yield break;
            }

            GameModeController.instance.BlinkToMode(GameMode.Tension);
            yield return null;
            yield return new WaitUntil(() => !PlayerEyesOpened());
            yield return new WaitUntil(PlayerEyesOpened);

            rule1.TriggerStartRule();

            if (rule1.ruleActive)
                Debug.Log($"[Rule6] คืนรอบที่ {NightLoopCount + 1} เริ่มแล้ว — กฎ 1 ทำงาน " +
                          "โดยไม่มีข้อความเด้ง (ของเดิมยังอยู่ในโทรศัพท์)", this);
            else
                Debug.LogError("[Rule6] เรียก Rule1.TriggerStartRule() แล้วแต่กฎ 1 ไม่เริ่ม — " +
                               "เช็กว่า RuleManager.RearmAllRules() ล้าง _hasCompleted ให้แล้วหรือยัง", this);
        }

        /// <summary>
        /// รอให้ผู้เล่นลุกจากเบาะแล้วเดินลงจากรถ
        ///
        /// ต้องรับปุ่ม E เองที่นี่ เพราะเบาะรถไม่มี SittingPoint — ตัวนั้นเป็นของม้านั่งศาลา
        /// ถ้าไม่มีใครรับ ผู้เล่นจะนั่งค้างอยู่ในรถตลอดไปโดยไม่มีอะไรบอกว่าต้องทำอะไร
        ///
        /// และต้องรอ "เดินออกจากรถ" ไม่ใช่แค่ "ลุก" ด้วย ไม่งั้นรถจะออกตัวตอนผู้เล่นยังยืนอยู่
        /// ในนั้น แล้วมันจะวิ่งหายไปทิ้งผู้เล่นยืนลอยอยู่กลางถนน
        ///
        /// ตัวจริงอยู่ใน BusRide — ฉากเปิดเกมใช้ตัวเดียวกันนี้ ผู้เล่นจึงลงจากรถด้วยท่าเดียวกัน
        /// ทั้งคืนแรกและคืนที่วนกลับมา
        /// </summary>
        IEnumerator LeaveBus(Rule6Bus bus)
            => _ride.LeaveBus(bus, inputHandler, getOffHint, offBusDistance, loopDepartDelay);

        /// <summary>
        /// รีเซ็ตคืนทั้งคืน — ยิงตอนตาปิดสนิท (จอดำ) ผู้เล่นจึงไม่เห็นอะไรกระโดด
        ///
        /// ไม่แตะตัวผู้เล่นและไม่เก็บรถ ต่างจากบทจบอื่น: คันที่ 4 ผู้เล่นยังนั่งอยู่ในรถคันนั้น
        /// และต้องนั่งอยู่ต่อไปจนรถพามาถึงศาลา
        ///
        /// ลำดับสำคัญ: ปลด ruleActive และปลดธงข้อความให้เสร็จ "ก่อน" SetTime เพราะ SetTime
        /// ยิง OnTimeChanged ทันทีแบบ synchronous ทั้ง RuleManager.CheckRules และ
        /// FriendListController.CheckNotifications จะวิ่งในบรรทัดนั้นเลย
        ///
        /// และไม่ปลดธงข้อความใดๆ เลย — คืนรอบสองไม่มีอะไรเด้งอีก ข้อความเก่า ไทม์ไลน์แชท
        /// และข้อมูลในแอปกฎยังอยู่ครบในโทรศัพท์ ผู้เล่นต้องเล่นจากของที่มีอยู่แล้ว
        /// ("คนส่งข้อความไม่ต้องหาใครมาแทนแล้ว เพราะผู้เล่นจะต้องทำตามกฎไปเรื่อยๆ")
        ///
        /// ผลข้างเคียงที่ต้องชดเชย: กฎ 1 เคยถูกปลุกด้วยการอ่านแชทจบ ไม่มีแชทใหม่ = ไม่มีใคร
        /// ปลุก กฎ 6 จึงต้องเรียก Rule1.TriggerStartRule() เองหลังผู้เล่นลงจากรถ
        /// </summary>
        void RestartNight()
        {
            // ปลด ruleActive เองไม่ผ่าน base.EndRule() — ตัวนั้นเรียก StandUpAfterRule ด้วย
            // ซึ่งจะดึงผู้เล่นลุกขึ้นทั้งที่กำลังนั่งอยู่บนรถที่วิ่งอยู่
            ruleActive      = false;
            _gameplayActive = false;

            if (ruleManager == null) ruleManager = FindAnyObjectByType<RuleManager>(FindObjectsInactive.Include);

            if (ruleManager != null) ruleManager.RearmAllRules();
            else Debug.LogWarning("[Rule6] ไม่เจอ RuleManager — คืนใหม่จะไม่มีกฎข้อไหนเริ่มเลย", this);

            // ยึดนาฬิกาไว้ก่อน แล้วกฎ 1 จะเป็นคนคุมต่อตอนมันเริ่ม (StartRule สั่ง IsPauseTime(true))
            // คืนแรกเป็นหน้าที่ของ Deliver() ที่หยุดนาฬิกาตอนส่งข้อความกฎ 1 — คืนนี้ไม่มีข้อความ
            // ถ้าปล่อยนาฬิกาเดินตรงนี้ เวลาจะไหลไปถึง 20:00 ตอนผู้เล่นยังนั่งอยู่บนรถ แล้วกฎ 2
            // จะ trigger ทับกฎ 1 ที่ยังไม่ได้เริ่ม
            TimeManager.instance.IsPauseTime(true);
            TimeManager.instance.SetTime(loopRestartHour, loopRestartMinute);
        }

        /// <summary>วางผู้เล่นกลับบนม้านั่งที่ศาลา — ใช้ตอนบทจบของคันที่ 1-3</summary>
        void ReturnPlayerToSeat()
        {
            var pc = PlayerController.Instance;
            if (pc == null) return;

            pc.isBlockStanding = true;

            if (pavilionSeat == null)
            {
                Debug.LogWarning("[Rule6] ยังไม่ได้ใส่ Pavilion Seat — ผู้เล่นจะค้างอยู่ปลายถนน" +
                                 "ตรงจุดที่รถหายไป", this);
                return;
            }

            if (pc.IsSitting())
            {
                // ย้ายที่นั่งของคนที่นั่งอยู่แล้ว — เขียน transform ตรงๆ ไม่ผ่าน TeleportTo
                // เพราะตัวนั้นบังคับเปิด CharacterController แล้ว SnapToGround ซึ่งจะทำให้
                // ท่านั่ง/ความสูงกล้องที่ SitRoutine ตั้งไว้หลุด ตอนนั่ง Update ไม่ใส่ gravity
                // อยู่แล้ว จึงไม่มีอะไรต้องให้เกาะพื้น
                pc.transform.SetPositionAndRotation(pavilionSeat.position, pavilionSeat.rotation);
                pc.ResetCameraAfterDeath();   // กล้องกลับระดับสายตา + ปลด look
                pc.SetMovement(false);
                return;
            }

            // ยืนอยู่ (เช่น SitInBus ไม่สำเร็จ) — ใช้ท่านั่งปกติ มันจัดกล้อง/physics/animator ให้ครบ
            pc.isBlockStanding = false;
            pc.StartSittingSequence(pavilionSeat);
            pc.isBlockStanding = true;
        }

        /// <summary>
        /// ปลายทางของทุกบทจบ — คืนนี้จบแล้ว
        ///
        /// ไม่เรียก base.EndRule() เพราะ RuleBase.EndRule จะสั่งผู้เล่นลุกจากที่นั่ง ซึ่งเป็น
        /// สิ่งเดียวที่ไม่ควรเกิดตอนจอกำลังขึ้นบทจบ
        /// </summary>
        void FinishEnding(Rule6BusKind kind, GameObject endingObject,
                          bool holdClock = true, bool warnIfMissing = true)
        {
            _isEnding       = true;
            _resolved       = true;
            _gameplayActive = false;
            Resolution      = kind;

            EndCarry();
            CleanupGameplay();

            if (holdClock) HoldClock();

            if (endingObject != null) endingObject.SetActive(true);
            else if (warnIfMissing)
                Debug.LogWarning($"[Rule6] บทจบ {kind} ยังไม่ได้ใส่ object — ที่นี่คือจุดที่คัทซีน" +
                                 $"เนื้อเรื่องตอนจบต้องมา กฎจบแล้วแต่จอจะไม่มีอะไรขึ้นมาบอกผู้เล่น", this);

            OnEnding?.Invoke(kind);
            Debug.Log($"[Rule6] บทจบของคืนนี้: {kind}", this);
        }

        /// <summary>คืนจบแล้ว — ยึดนาฬิกาไว้ ไม่ให้เวลาเดินต่อเข้าชั่วโมงที่ไม่มีกฎอะไรรออยู่</summary>
        static void HoldClock()
        {
            if (TimeManager.instance != null) TimeManager.instance.IsPauseTime(true);
        }

        IEnumerator ShowLines(DialogueLine[] lines)
        {
            if (lines == null || lines.Length == 0) yield break;
            if (PlayerDialogueUI.instance == null) yield break;

            bool done = false;
            void Handler() => done = true;

            PlayerDialogueUI.instance.OnSequenceComplete += Handler;
            PlayerDialogueUI.instance.ShowSequence(lines);

            yield return new WaitUntil(() => done);
            PlayerDialogueUI.instance.OnSequenceComplete -= Handler;
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Cleanup
        // ════════════════════════════════════════════════════════════════

        void CleanupGameplay()
        {
            if (_cycle != null)
            {
                StopCoroutine(_cycle);
                _cycle = null;
            }

            // RuleFlow อาจยังค้างรอ WaitUntil(นั่ง) อยู่ถ้ากฎถูกจบก่อนผู้เล่นจะไปนั่ง
            // ปล่อยไว้แล้วมันจะตื่นมาเรียก StartGameplay() ตอนผู้เล่นนั่งในคืนถัดไป
            if (_flow != null)
            {
                StopCoroutine(_flow);
                _flow = null;
            }

            if (buses != null)
                foreach (var bus in buses)
                    if (bus != null) bus.Hide();

            _currentBus = null;

            if (paeng != null) paeng.Disappear();

            if (AudioManager.instance != null) AudioManager.instance.StopAllRule6Sounds();
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        // กันเสียงเครื่องยนต์/เพลงค้างตอนกด Stop ใน Editor กลางคัน
        private void OnDisable()
        {
            if (!_gameplayActive) return;

            _gameplayActive = false;
            CleanupGameplay();
        }

        #endregion
    }
}
