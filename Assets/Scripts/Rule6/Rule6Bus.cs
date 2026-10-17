using System.Collections;
using Enum;
using FMOD.Studio;
using FMODUnity;
using InputSystem;
using Player;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace Rule6
{
    /// <summary>
    /// รถประจำทาง 1 คันของกฎข้อ 6
    ///
    /// ตัวนี้รู้แค่สามอย่าง: คันนี้พาไปบทจบไหน, ไฟของตัวเองทำตัวยังไง, และผู้เล่นเดินมาถึง
    /// ที่นั่งในรถหรือยัง ส่วนการขับเข้า-ออกจุดจอดและตัวบทจบเป็นของ Rule6
    ///
    /// สิ่งที่ "ไม่" อยู่ในโค้ดนี้โดยตั้งใจ — มันมาพร้อมโมเดลรถแล้ว:
    ///   ป้ายหน้ารถ      เป็นเท็กซ์เจอร์ติดตัวรถ ไม่ใช่ TMP ที่ต้องเซ็ตข้อความตอนรันไทม์
    ///   ที่นั่ง + คนนั่ง  วางมาในโมเดลแล้ว รวมถึงที่ว่างที่เหลืออยู่ที่เดียวของคันที่ 1
    ///   ประตู           รถไม่มีประตู เดินขึ้นได้เลย
    ///   รอยเลือด/คราบน้ำ อยู่ในวัสดุของโมเดลคันนั้นๆ
    ///
    /// เหลือในโค้ดเฉพาะของที่เป็น "พฤติกรรม" จริงๆ: ไฟติดๆ ดับๆ ของคันที่ 3, ไฟหน้าดับของ
    /// คันที่ 4, และความเงียบของเครื่องยนต์คันที่ 4 — สามอย่างนี้เป็นเบาะแสที่ขยับตามเวลา
    /// จะ bake ลงโมเดลไม่ได้
    ///
    /// การขึ้นรถ: รถไม่มีประตู เดินเข้าไปในรถได้เลยไม่ต้องกดอะไร แต่การ "เลือกคันนี้" คือการ
    /// กด E นั่งลงที่เบาะ เหมือนม้านั่งที่ศาลา — เดินผ่านเบาะเฉยๆ ไม่นับ ผู้เล่นจึงเดินสำรวจ
    /// ในรถได้เต็มที่ (ดูว่าคนนั่งกี่คน เหลือที่ว่างกี่ที่ ใครนั่งริมหน้าต่าง) โดยไม่ต้องกลัวว่า
    /// เดินลึกเกินไปแล้วติดบทจบ
    /// </summary>
    public class Rule6Bus : MonoBehaviour
    {
        // ─────────────────────────── Identity ───────────────────────────

        [Header("คันนี้คือคันไหน")]
        [Tooltip("ตัวตัดสินบทจบตอนผู้เล่นขึ้นรถ — Rule6 อ่านค่านี้ ไม่ได้อ่านลำดับในลิสต์\n" +
                 "ลำดับที่รถวิ่งมาจอดมาจากลำดับในช่อง Buses ของ Rule6 แยกกันคนละเรื่อง")]
        [SerializeField] private Rule6BusKind kind = Rule6BusKind.Trap;

        // ─────────────────────────── ที่นั่ง ───────────────────────────

        [Header("ที่นั่งในรถ")]
        [Tooltip("ที่นั่งที่ผู้เล่นจะไปนั่ง = จุดที่ตัดสินว่าเลือกคันนี้\n" +
                 "ใส่ Transform ของที่นั่งในโมเดล หันหน้าไปทางที่ผู้เล่นควรหันตอนนั่ง\n" +
                 "คันที่ 1 ให้ใช้ที่ว่างที่เหลืออยู่ที่เดียว (ที่ไม่มีให้แป้ง)")]
        [SerializeField] private Transform seat;

        [Tooltip("ท่านั่งจริง — จุดที่ \"ราก\" ของตัวละครจะไปอยู่ตอนนั่ง ไม่ใช่สะโพก\n" +
                 "เว้นว่าง = ใช้ Seat เอง แต่ปกติต้องแยก เพราะ Seat ควรอยู่บนเบาะ (ไว้คุมระยะ\n" +
                 "กด E) ส่วนท่านั่งต้องอยู่ที่พื้นรถหน้าเบาะ ไม่งั้นตัวจะลอยสูงเท่าความหนาเบาะ\n\n" +
                 "ลากตัวนี้ใน Scene view แล้วดู gizmo: ลูกกลมล่างคือจุดที่เท้าจะอยู่ ลูกกลมบนคือ\n" +
                 "ระดับตา และเส้นคือทิศที่จะมอง — เลื่อนจนลูกกลมล่างแตะพื้นรถพอดี")]
        [SerializeField] private Transform sitPose;

        [Tooltip("จุดที่ไปยืนตอนลุกจากเบาะ — ต้องอยู่ที่ \"ทางเดินในรถ\" ข้างเบาะ ไม่ใช่ที่เบาะ\n\n" +
                 "ที่นั่งมีเบาะและพนักล้อมอยู่ ถ้าลุกตรงจุดที่นั่งพอดี แคปซูลของตัวละคร (สูง 2.5)\n" +
                 "จะไปซ้อนกับเบาะแล้วผู้เล่นจะขยับไม่ได้เลย\n\n" +
                 "จำเป็นเฉพาะรถที่ผู้เล่นต้องลุกขึ้นในนั้น = คันที่ 4 (วนลูป) กับรถของฉากเปิด\n" +
                 "คันที่ 1-3 ไม่ต้องใส่ ผู้เล่นขึ้นแล้วไม่ได้ลุกอีก\n" +
                 "เว้นว่าง = ลุกตรงจุดที่นั่ง (ติดเบาะ)")]
        [SerializeField] private Transform standPose;

        [Tooltip("ระยะจากเบาะที่กด E นั่งได้ (เมตร)")]
        [SerializeField] private float seatRadius = 1.5f;

        [Tooltip("มุมสูงสุดระหว่างทิศที่กล้องมองกับเบาะ ถึงจะนับว่ากำลังมองเบาะอยู่ (องศา)\n" +
                 "กันการกด E ตอนยืนหลังเบาะแล้วมองออกหน้าต่าง")]
        [SerializeField] private float lookAngle = 70f;

        [Tooltip("ข้อความ hint ตอนยืนอยู่ข้างเบาะ")]
        [SerializeField] private string sitHint = "E   นั่ง";

        [Tooltip("ส่วนต่างความสูงที่ยอมได้ (เมตร) — กันผู้เล่นที่ยืนอยู่ข้างรถใต้หน้าต่างบานนั้น\n" +
                 "พอดีแล้วถูกนับว่าขึ้นรถทั้งที่ยังอยู่บนถนน")]
        [SerializeField] private float seatHeightTolerance = 1.5f;

        // ─────────────────────────── ไฟ ───────────────────────────

        [Header("ไฟ (เว้นว่างได้ถ้าไฟอยู่ในโมเดลและไม่ต้องขยับ)")]
        [Tooltip("ไฟหน้ารถ — คันที่ 4 ต้องดับ ติ๊ก Head Lights Off ไว้")]
        [SerializeField] private Light[] headLights;

        [Tooltip("✓ = ไฟหน้าดับตลอด (คันที่ 4)")]
        [SerializeField] private bool headLightsOff;

        [Tooltip("ไฟในห้องโดยสาร — ต้องใส่เฉพาะคันที่ 3 ที่ไฟต้องติดๆ ดับๆ\n" +
                 "คันอื่นปล่อยไฟในโมเดลทำงานเองได้ ไม่ต้องใส่")]
        [SerializeField] private Light[] interiorLights;

        [Tooltip("✓ = ไฟในติดๆ ดับๆ (คันที่ 3)")]
        [SerializeField] private bool flickerInterior;

        [Tooltip("ช่วงเวลาที่ไฟติด / ดับ ตอน Flicker Interior เปิดอยู่ (วินาที)")]
        [SerializeField] private Vector2 flickerOnRange  = new Vector2(0.15f, 1.2f);
        [SerializeField] private Vector2 flickerOffRange = new Vector2(0.05f, 0.35f);

        // ─────────────────────────── เสียง ───────────────────────────

        [Header("เสียง")]
        [Tooltip("event เครื่องยนต์ของรถคันนี้ (3D loop เกาะที่ตัวรถ) — เว้นว่าง = ไม่มีเสียงเครื่อง\n" +
                 "คันที่ 4 ต้องเว้นว่าง ดีไซน์บอกว่าเครื่องยนต์ไม่มีเสียง นั่นคือเบาะแสของมัน")]
        [SerializeField] private string engineEvent = "event:/Rule6/BusEngine";

        // ─────────────────────────── Runtime ───────────────────────────

        private RuleSystem.Rule.Rule6 _rule;
        private InputHandler          _inputHandler;
        private Camera                _playerCam;
        private EventInstance         _engine;
        private bool                  _engineStarted;
        private bool                  _subscribed;
        private bool                  _boardable;
        private bool                  _boarded;
        private Coroutine             _flickerRoutine;

        // hint ต้องค้างจนผู้เล่นเดินออกจากระยะ — PlayerDialogueUI จะ fade เองเมื่อ hold ครบ
        // ส่งค่ายาวๆ ไปให้มันค้าง แล้วเรียก Hide() เองตอนออกนอกระยะ (เหมือน Rule4/Doll)
        private const float PersistentHold = 3600f;

        /// <summary>บทจบที่รถคันนี้พาไป</summary>
        public Rule6BusKind Kind => kind;

        /// <summary>เบาะ — จุดอ้างอิงของระยะกด E และของการเช็กว่าผู้เล่นลงจากรถไปแล้ว</summary>
        public Transform Seat => seat;

        /// <summary>
        /// ท่านั่งจริง — จุดที่รากของตัวละครไปอยู่
        ///
        /// แยกจาก Seat เพราะสองอย่างนี้วัดกันละที่: ระยะกด E ต้องวัดจากเบาะ (ของที่ผู้เล่นเดินไปหา)
        /// แต่ท่านั่งต้องวางที่พื้นรถ ถ้าใช้จุดเดียวกันจะต้องเลือกว่าจะให้ตัวลอย หรือจะให้ hint
        /// ขึ้นตอนยืนห่างจากเบาะเกินไป
        /// </summary>
        public Transform SitPose => sitPose != null ? sitPose : seat;

        /// <summary>
        /// จุดที่ผู้เล่นไปยืนตอนลุกจากเบาะ (null = ยืนตรงจุดที่นั่ง)
        ///
        /// แยกจาก SitPose เพราะท่านั่งต้องอยู่ "บนเบาะ" แต่ท่ายืนต้องอยู่ "ข้างเบาะ" — จุดเดียวกัน
        /// ใช้ไม่ได้ ตอนนั่งตัวละครไม่มี collision กับเบาะ (SetSittingPhysics เปลี่ยน layer ให้)
        /// แต่ตอนยืนมันกลับมาชนทุกอย่างตามปกติ
        /// </summary>
        public Transform StandPose => standPose;

        // ─────────────────────────── Setup ───────────────────────────

        /// <summary>เรียกครั้งเดียวตอนกฎเริ่ม — เก็บรถให้พ้นสายตาแล้วรอคิว</summary>
        public void Setup(RuleSystem.Rule.Rule6 rule, InputHandler inputHandler)
        {
            _rule         = rule;
            _inputHandler = inputHandler;
            _boarded      = false;

            if (seat == null)
                Debug.LogError($"[Rule6] รถ '{name}' ยังไม่ได้ใส่ Seat — ผู้เล่นขึ้นคันนี้ไม่ได้เลย", this);
            else if (sitPose == null)
                Debug.LogWarning($"[Rule6] รถ '{name}' ยังไม่ได้ใส่ Sit Pose — จะใช้ Seat เป็นท่านั่ง " +
                                 "ซึ่งมักทำให้ตัวลอยสูงเท่าความหนาเบาะ", this);

            Hide();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            StopEngine();
        }

        // ปิดวัตถุทิ้งกลางคัน เสียงเครื่องต้องไม่ดังต่อ และ hint ต้องไม่ค้างกลางจอ
        private void OnDisable()
        {
            Unsubscribe();
            StopEngine();
        }

        // ─────────────────────────── โผล่ / หาย ───────────────────────────

        /// <summary>รถคันนี้กำลังวิ่งเข้ามา</summary>
        public void Appear()
        {
            gameObject.SetActive(true);

            foreach (var light in headLights)
                if (light != null) light.enabled = !headLightsOff;

            if (flickerInterior && _flickerRoutine == null)
                _flickerRoutine = StartCoroutine(FlickerRoutine());

            StartEngine();
        }

        /// <summary>รถออกไปแล้ว — เก็บทั้งคัน</summary>
        public void Hide()
        {
            SetBoardable(false);

            if (_flickerRoutine != null)
            {
                StopCoroutine(_flickerRoutine);
                _flickerRoutine = null;
            }

            StopEngine();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// เปิด/ปิดว่าตอนนี้ขึ้นรถได้ไหม — Rule6 เปิดเฉพาะช่วงที่รถหยุดอยู่ที่ป้าย
        ///
        /// ไม่ผูกกับ "รถ active อยู่" เพราะรถ active ตั้งแต่เริ่มวิ่งเข้ามา ถ้าเปิดให้ขึ้นด้วย
        /// ผู้เล่นที่ยืนรอบนถนนจะถูกรถคว้าไปนั่งตอนมันวิ่งผ่านตัว แล้วบทจบจะเล่นทับอนิเมชั่น
        /// รถเข้าจอด
        /// </summary>
        public void SetBoardable(bool value)
        {
            if (_boardable == value) return;

            _boardable = value;
            if (!_boardable) Unsubscribe();
        }

        // ─────────────────────────── ไฟติดๆ ดับๆ ───────────────────────────

        /// <summary>ไฟในของคันที่ 3 — สุ่มช่วงติด/ดับแยกกันเพื่อให้จังหวะไม่เป็นลูปที่จำได้</summary>
        private IEnumerator FlickerRoutine()
        {
            while (true)
            {
                SetInteriorEnabled(true);
                yield return new WaitForSeconds(Random.Range(flickerOnRange.x, flickerOnRange.y));

                SetInteriorEnabled(false);
                yield return new WaitForSeconds(Random.Range(flickerOffRange.x, flickerOffRange.y));
            }
        }

        private void SetInteriorEnabled(bool value)
        {
            foreach (var light in interiorLights)
                if (light != null) light.enabled = value;
        }

        // ─────────────────────────── เสียงเครื่องยนต์ ───────────────────────────

        private void StartEngine()
        {
            if (_engineStarted || string.IsNullOrWhiteSpace(engineEvent)) return;

            // CreateInstance จะ throw ถ้ายังไม่มี event ใน FMOD — ห้ามให้หลุดออกไป
            // ไม่งั้น Rule6 จะตายกลางคิวรถ แล้วไม่มีรถคันไหนมาอีกเลย
            try
            {
                _engine = RuntimeManager.CreateInstance(engineEvent);
            }
            catch (EventNotFoundException)
            {
                Debug.LogWarning($"[Rule6] ยังไม่มี event '{engineEvent}' ใน FMOD — " +
                                 $"รถ '{name}' จะไม่มีเสียงเครื่อง (ยังขึ้นได้ปกติ)", this);
                return;
            }

            _engine.set3DAttributes(RuntimeUtils.To3DAttributes(transform));
            _engine.start();
            _engineStarted = true;
        }

        private void StopEngine()
        {
            if (!_engineStarted) return;

            _engine.stop(STOP_MODE.ALLOWFADEOUT);
            _engine.release();
            _engineStarted = false;
        }

        // ─────────────────────────── ขึ้นรถ ───────────────────────────

        private void Update()
        {
            // ตำแหน่ง 3D ต้องตามตัวรถ — Rule6 ขับรถด้วยการขยับ transform ทุกเฟรม
            if (_engineStarted)
                _engine.set3DAttributes(RuntimeUtils.To3DAttributes(transform));

            if (_boarded || !_boardable || seat == null || _rule == null) return;

            bool inRange = PlayerAtSeat();

            if (inRange && !_subscribed)
            {
                _subscribed = true;
                if (_inputHandler != null) _inputHandler.OnInteractPressed += TrySit;
                if (PlayerDialogueUI.instance != null)
                    PlayerDialogueUI.instance.ShowLine(sitHint, PersistentHold);
            }
            else if (!inRange && _subscribed)
            {
                Unsubscribe();
            }
        }

        /// <summary>
        /// กด E ข้างเบาะ = เลือกคันนี้
        ///
        /// การนั่งเป็นการตัดสินใจ ไม่ใช่การเดิน — ถ้าแค่เดินถึงเบาะก็นับว่าเลือกแล้ว ผู้เล่นจะ
        /// เดินสำรวจในรถไม่ได้เลย ซึ่งเป็นสิ่งที่กฎนี้บังคับให้ทำ (ต้องอ่านภายในรถให้ออก)
        /// </summary>
        private void TrySit()
        {
            if (_boarded || !_boardable || _rule == null) return;

            // Rule6 ปฏิเสธได้ถ้ามีบทจบเล่นอยู่แล้ว — กันเคสรถสองคันจอดทับกันตอนกดปุ่มเทส
            if (!_rule.TryBoardBus(this)) return;

            _boarded = true;
            Unsubscribe();
            SetBoardable(false);
        }

        /// <summary>
        /// ผู้เล่นยืนอยู่ข้างเบาะและมองเบาะอยู่หรือเปล่า
        ///
        /// ระยะวัดจากตัวผู้เล่นไม่ใช่จากกล้อง: กล้องยื่นไปข้างหน้าหัวได้ ถ้าวัดจากกล้องผู้เล่นจะ
        /// นั่งได้จากการยืนข้างรถแล้วหันหน้าเข้าหน้าต่าง ซึ่งอ่านเป็นบั๊กเต็มๆ
        /// ส่วนมุมมองวัดจากกล้อง เพราะ "กำลังมองเบาะนั้น" เป็นเรื่องของกล้องอย่างเดียว
        ///
        /// แยกแกน Y ออกมาเช็กต่างหากด้วย เพราะพื้นรถสูงกว่าถนน คนที่ยืนบนถนนใต้หน้าต่าง
        /// บานที่ตรงกับเบาะนั้นจะอยู่ห่างในระนาบแค่ไม่ถึงเมตร
        /// </summary>
        private bool PlayerAtSeat()
        {
            var pc = PlayerController.Instance;
            if (pc == null) return false;

            Vector3 delta = pc.transform.position - seat.position;

            if (Mathf.Abs(delta.y) > seatHeightTolerance) return false;

            delta.y = 0f;
            if (delta.sqrMagnitude > seatRadius * seatRadius) return false;

            if (_playerCam == null) _playerCam = Camera.main;
            if (_playerCam == null) return true;   // ไม่มีกล้องก็ยังให้นั่งได้ ดีกว่านั่งไม่ได้เลย

            Vector3 toSeat = seat.position - _playerCam.transform.position;
            return Vector3.Angle(_playerCam.transform.forward, toSeat.normalized) < lookAngle;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (_inputHandler != null) _inputHandler.OnInteractPressed -= TrySit;
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        // ─────────────────────────── Gizmo ───────────────────────────

        /// <summary>
        /// วงรัศมีที่กด E ได้ ใน Scene view — ระยะนี้มองไม่เห็นตอนจัดฉาก กว้างเกินไปผู้เล่นจะนั่ง
        /// ได้จากนอกรถ แคบเกินไปจะหา hint ไม่เจอแล้วยืนกด E เปล่าอยู่ข้างเบาะ
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (seat != null)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                Gizmos.DrawWireSphere(seat.position, seatRadius);

                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.25f);
                Gizmos.DrawLine(seat.position + Vector3.up * seatHeightTolerance,
                                seat.position - Vector3.up * seatHeightTolerance);
            }

            DrawSeatedPlayer();
            DrawStandingPlayer();
        }

        /// <summary>
        /// วาดตัวผู้เล่นตอนนั่งที่ Sit Pose
        ///
        /// มีเพราะอาการ "นั่งแล้วตัวลอย" มองไม่ออกเลยจากการลาก Transform เปล่าๆ — SitRoutine
        /// เขียน transform.position = target.position เป๊ะๆ แปลว่าจุดที่วางคือจุดที่ "เท้า"
        /// จะไปอยู่ ไม่ใช่สะโพก ซึ่งเป็นเรื่องที่ไม่มีอะไรใน Inspector บอก
        ///
        /// อ่านความสูงจาก PlayerController ในซีนจริง ไม่ฮาร์ดโค้ด เพราะ sittingCameraHeight
        /// ถูก override ไว้ใน scene (2.0 ไม่ใช่ 1.3 ตาม prefab) ถ้าวาดจากค่า prefab จะหลงกันครึ่งเมตร
        /// </summary>
        private void DrawSeatedPlayer()
        {
            Transform pose = SitPose;
            if (pose == null) return;

            float eyeHeight  = 2f;
            float bodyHeight = 2.5f;

            var pc = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (pc != null)
            {
                eyeHeight = pc.SittingCameraHeight;

                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) bodyHeight = cc.height;
            }

            Vector3 feet = pose.position;
            Vector3 eyes = feet + Vector3.up * eyeHeight;

            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.95f);
            Gizmos.DrawWireSphere(feet, 0.18f);                       // เท้า — ต้องแตะพื้นรถพอดี
            Gizmos.DrawLine(feet, feet + Vector3.up * bodyHeight);
            Gizmos.DrawWireSphere(eyes, 0.14f);                       // ระดับตา
            Gizmos.DrawRay(eyes, pose.forward * 1.5f);                // ทิศที่จะมองตอนนั่ง
        }

        /// <summary>
        /// วาดตัวผู้เล่นตอนลุกขึ้นยืนที่ Stand Pose — แคปซูลจริงที่จะชนของในรถ
        ///
        /// วาดเป็นแคปซูล (สองลูกกลม + เส้น) ไม่ใช่จุด เพราะปัญหาที่ต้องเห็นคือ "ตัวซ้อนกับเบาะ
        /// ไหม" ไม่ใช่ "จุดอยู่ตรงไหน" — รัศมีกับความสูงอ่านจาก CharacterController ในซีนจริง
        /// ถ้าวงกลมล่างกินเข้าไปในเบาะหรือพนัก ลุกแล้วจะติด เลื่อนออกมาที่ทางเดิน
        /// </summary>
        private void DrawStandingPlayer()
        {
            if (standPose == null) return;

            float radius     = 0.4f;
            float bodyHeight = 2.5f;

            var pc = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (pc != null)
            {
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) { radius = cc.radius; bodyHeight = cc.height; }
            }

            Vector3 feet = standPose.position;
            Vector3 head = feet + Vector3.up * bodyHeight;

            Gizmos.color = new Color(0.45f, 1f, 0.5f, 0.95f);
            Gizmos.DrawWireSphere(feet + Vector3.up * radius, radius);
            Gizmos.DrawWireSphere(head - Vector3.up * radius, radius);
            Gizmos.DrawLine(feet, head);
            Gizmos.DrawLine(standPose.position, SitPose != null ? SitPose.position : feet);
        }
    }
}
