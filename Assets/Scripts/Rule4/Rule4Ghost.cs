using UnityEngine;
using UnityEngine.AI;

namespace Rule4
{
    /// <summary>พฤติกรรมของผีในแต่ละช่วงของ Rule 4</summary>
    public enum GhostMode
    {
        Idle,   // ไม่ทำอะไร (เพิ่ง spawn / ถูกสั่งหยุด)
        Marker, // ยืนนิ่งข้างตุ๊กตาเป็นป้ายบอกตำแหน่ง — ไม่เดิน ไม่ฆ่า
        Chase   // ไล่ผู้เล่น — เข้าใกล้กว่า killDistance = ตายทันที
    }

    /// <summary>
    /// ผีของ Rule 4 — ทำหน้าที่ 2 อย่างตามช่วงของเกม
    ///
    ///   Marker : ตุ๊กตา 2 ตัวแรก ผีจะไป "ยืนนิ่ง" อยู่ข้างตุ๊กตาให้ผู้เล่นเห็นแต่ไกล
    ///            เป็นป้ายบอกตำแหน่ง ไม่เดินตาม ไม่ฆ่า หันหน้าตามผู้เล่นอย่างเดียว
    ///            พอผู้เล่นเก็บตุ๊กตา Rule4 จะซ่อนผีตัวนี้ แล้วให้โผล่ใหม่ข้างตุ๊กตาตัวถัดไป
    ///   Chase  : เริ่มหลังผู้เล่นวางตุ๊กตาตัวที่ 2 เสร็จ — Rule4 จะ spawn ผีตัวใหม่
    ///            ที่จุดไกลจากผู้เล่น แล้วไล่ยาวจนจบกฎ
    ///
    /// ความเร็วตอน Chase (เรียงตามลำดับความสำคัญ):
    ///   ผู้เล่นกลั้นหายใจ     → หยุดอยู่กับที่ รอจนหายใจออก
    ///   ผู้เล่นจ้องอยู่       → staredSpeed + เบรกทันที (แทบไม่ขยับ)
    ///   ผู้เล่นกลั้นหายใจ     → มองไม่เห็นผู้เล่น เดินเร่ร่อน (isWalk) ไม่ไล่ ไม่ฆ่า
    ///   ปล่อยกลั้น            → ท่าคอหัก + Bones Crack → หัวเราะ → วิ่งไล่ต่อ
    ///   หลังถูกบังคับหายใจ   → sprintSpeed ชั่วคราว (ผีวิ่งเข้ามา)
    ///   ปกติ                → chaseSpeed (+ speedGainPerDoll ต่อตุ๊กตาที่วางสำเร็จ)
    ///
    /// เสียงฝีเท้ายิงทีละก้าวตามระยะทางที่เดินได้จริง จังหวะจึงช้า/เร็วตามความเร็วผีเอง
    /// (ถูกจ้อง = ก้าวห่าง, วิ่ง = ก้าวถี่) และเงียบสนิทเมื่อผีหยุดหรือยืนเป็นป้าย
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Rule4Ghost : MonoBehaviour
    {
        [Header("Speed")]
        [Tooltip("ความเร็วไล่ปกติ")]
        [SerializeField] private float chaseSpeed = 1.6f;

        [Tooltip("ความเร็วตอนถูกผู้เล่นจ้อง — 0 = นิ่งสนิท")]
        [SerializeField] private float staredSpeed = 0.1f;

        [Tooltip("อัตราเบรกตอนถูกจ้อง — ยิ่งสูงยิ่งหยุดทันที ไม่ไถลต่อ\n" +
                 "(acceleration ปกติของ NavMeshAgent ~8 ทำให้ผีไถลไปอีกเกือบเมตรก่อนจะช้าลง)")]
        [SerializeField] private float staredBrake = 60f;

        [Tooltip("ความเร็วตอนวิ่งเข้าหาผู้เล่นหลังถูกบังคับหายใจออก")]
        [SerializeField] private float sprintSpeed = 3.2f;

        [Tooltip("วิ่งนานกี่วินาทีหลังผู้เล่นถูกบังคับหายใจออก")]
        [SerializeField] private float sprintDuration = 4f;

        [Tooltip("ความเร็วที่เพิ่มขึ้นทุกครั้งที่ผู้เล่นวางตุ๊กตาสำเร็จ (กดดันขึ้นเรื่อยๆ)")]
        [SerializeField] private float speedGainPerDoll = 0.15f;

        [Header("Before Chase (คอหักก่อนพุ่ง)")]
        [Tooltip("ก่อนออกวิ่งไล่ ผีจะยืนเล่นท่า BeforeChase + เสียงกระดูกหักนานเท่านี้ (วินาที)\n" +
                 "ควรเท่าความยาวคลิป BeforeChase (~3.97 วิ)")]
        [SerializeField] private float beforeChaseDuration = 3.97f;

        [Tooltip("เล่นท่าคอหักตอนเริ่มไล่ครั้งแรกด้วยไหม (ปิด = เฉพาะตอนผู้เล่นปล่อยกลั้นหายใจ)")]
        [SerializeField] private bool windupOnFirstChase = true;

        [Tooltip("เสียงหัวเราะ (GhostBreathRelease) ดังหลังเสียงกระดูกหักเริ่มไปแล้วกี่วินาที\n" +
                 "ตั้งเท่า beforeChaseDuration = หัวเราะพอดีตอนออกวิ่ง / น้อยกว่า = หัวเราะระหว่างคอหัก")]
        [SerializeField] private float laughDelayAfterCrack = 2.5f;

        [Tooltip("DEBUG: log ตำแหน่ง Y ของ root / ตัวโมเดล ทุก 0.5 วิ ระหว่างท่าคอหัก — ไว้ไล่ว่าอะไรลอย")]
        [SerializeField] private bool debugWindupHeight;

        [Header("Wander (ผู้เล่นกลั้นหายใจ)")]
        [Tooltip("ความเร็วเดินเร่ร่อนตอนมองไม่เห็นผู้เล่น")]
        [SerializeField] private float wanderSpeed = 0.8f;

        [Tooltip("สุ่มจุดเดินภายในรัศมีนี้รอบตัวผี")]
        [SerializeField] private float wanderRadius = 6f;

        [Tooltip("เปลี่ยนจุดเดินใหม่ทุกกี่วินาที (หรือเมื่อถึงจุดแล้ว)")]
        [SerializeField] private float wanderRepathInterval = 3f;

        [Tooltip("ห้ามเลือกจุดที่ 'เข้าหาผู้เล่น' — ค่ายิ่งต่ำยิ่งหันหนี (1 = ไม่สน, 0 = ห้ามมุ่งหน้าหาเลย, -0.3 = ต้องเบี่ยงออกชัดๆ)")]
        [Range(-1f, 1f)]
        [SerializeField] private float wanderMaxDotToPlayer = 0.2f;

        [Header("Marker (ยืนข้างตุ๊กตา)")]
        [Tooltip("ความเร็วในการหันหน้าตามผู้เล่นตอนยืนเป็นป้าย (0 = ไม่หัน)")]
        [SerializeField] private float markerTurnSpeed = 1.5f;

        [Header("Kill")]
        [Tooltip("ระยะที่ผีจับผู้เล่นได้ → ตายทันที (เฉพาะตอน Chase)")]
        [SerializeField] private float killDistance = 1.4f;

        [Header("Footsteps")]
        [Tooltip("เดินได้กี่เมตรถึงจะลงเท้า 1 ก้าว — ยิ่งน้อยยิ่งก้าวถี่\n" +
                 "จังหวะก้าวคำนวณจากระยะที่เดินได้จริง จึงช้า/เร็วตามความเร็วผีเองอัตโนมัติ")]
        [SerializeField] private float strideLength = 0.9f;

        [Tooltip("ความเร็วต่ำกว่านี้ถือว่าหยุดอยู่กับที่ ไม่ลงเสียงฝีเท้า")]
        [SerializeField] private float footstepMinSpeed = 0.05f;

        [Tooltip("เว้นระยะขั้นต่ำระหว่างก้าว (วินาที) — กันเสียงรัวเกินจริงถ้า strideLength ตั้งไว้สั้นไป\n" +
                 "ตั้ง 0 เพื่อปิดตัวกันนี้")]
        [SerializeField] private float minFootstepInterval = 0.18f;

        [Header("Repath")]
        [Tooltip("ความถี่ในการคำนวณเส้นทางใหม่ (วินาที)")]
        [SerializeField] private float repathInterval = 0.25f;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        [Tooltip("bool ใน Animator ที่พาเข้า state Run — เปิดค้างไว้ตลอดช่วงไล่ (มีท่าเดียวคือวิ่ง)")]
        [SerializeField] private string runBoolName = "isRun";

        [Tooltip("bool ใน Animator ที่พาเข้า state Walk — เปิดตอนผู้เล่นกลั้นหายใจแล้วผีเดินเร่ร่อน")]
        [SerializeField] private string walkBoolName = "isWalk";

        [Tooltip("float ใน Animator ที่ใช้เป็น Speed Multiplier ของ state Run\n" +
                 "ต้องไปติ๊ก Multiplier ที่ state Run แล้วเลือก parameter นี้ ไม่งั้นค่านี้ไม่มีผล")]
        [SerializeField] private string runSpeedParamName = "RunSpeed";

        [Tooltip("ความเร็ว (m/s) ที่คลิปวิ่งดู 'พอดี' ที่ multiplier = 1\n" +
                 "ผีวิ่งเร็วกว่านี้ → อนิเมชั่นเร็วขึ้นตามสัดส่วน ช้ากว่า → ช้าลง")]
        [SerializeField] private float runClipNaturalSpeed = 1.6f;

        [Tooltip("multiplier ต่ำสุดตอนผีแทบไม่ขยับ (ถูกจ้อง / กลั้นหายใจ)\n" +
                 "0 = ค้างท่าไปเลย, ~0.1 = ขยับช้าๆ ให้ดูยังมีชีวิต")]
        [SerializeField] private float minRunAnimSpeed = 0.08f;

        [Tooltip("multiplier สูงสุด — กันอนิเมชั่นวิ่งรัวจนดูพังตอน sprint")]
        [SerializeField] private float maxRunAnimSpeed = 2.5f;

        [Tooltip("float ใน Animator ที่ใช้เป็น Speed Multiplier ของ state Walk (ตอนเดินเร่ร่อน)")]
        [SerializeField] private string walkSpeedParamName = "WalkSpeed";

        [Tooltip("ความเร็ว (m/s) ที่คลิปเดินดู 'พอดี' ที่ multiplier = 1 — ควรใกล้ wanderSpeed")]
        [SerializeField] private float walkClipNaturalSpeed = 0.8f;

        [Tooltip("ช่วง multiplier ของท่าเดิน — min กันค้างท่าตอนเลี้ยว/ชนกำแพง")]
        [SerializeField] private float minWalkAnimSpeed = 0.2f;
        [SerializeField] private float maxWalkAnimSpeed = 1.6f;

        // ── Runtime ──
        private NavMeshAgent          _agent;
        private Transform             _player;
        private RuleSystem.Rule.Rule4 _rule;
        private GhostMode             _mode = GhostMode.Idle;
        private float                 _repathTimer;
        private float                 _baseSpeed;
        private float                 _baseAccel;
        private bool                  _wasStared;
        private float                 _sprintTimer;
        private bool                  _caught;
        private float                 _windupTimer;   // > 0 = กำลังเล่นท่าคอหัก ยังไม่ออกวิ่ง
        private float                 _laughTimer;    // > 0 = รอถึงเวลาหัวเราะหลังกระดูกหัก
        private bool                  _wasHidden;     // เฟรมก่อนผู้เล่นกลั้นหายใจอยู่ไหม — ไว้จับจังหวะ "ปล่อย"
        private float                 _wanderTimer;   // นับถอยหลังถึงจะสุ่มจุดเดินใหม่
        private float                 _strideAccum;
        private float                 _lastFootstepTime = -999f;

        /// <summary>true = ผู้เล่นกำลังจ้องผีอยู่ (StareSystem เป็นคนเซ็ต)</summary>
        public bool IsBeingStared { get; set; }

        /// <summary>true = ผู้เล่นกำลังกลั้นหายใจ ผีจะหยุดอยู่กับที่ (Rule4 เซ็ตให้จาก BreathSystem)</summary>
        public bool IsPlayerHidden { get; set; }

        /// <summary>โหมดปัจจุบันของผี</summary>
        public GhostMode Mode => _mode;

        // ─────────────────────────── Setup ───────────────────────────

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        public void Init(Transform player, RuleSystem.Rule.Rule4 rule)
        {
            _player    = player;
            _rule      = rule;
            _baseSpeed = chaseSpeed;
            _baseAccel = _agent.acceleration;
        }

        // ─────────────────────────── Mode ───────────────────────────

        /// <summary>
        /// วาร์ปไปยืนนิ่งข้างตุ๊กตาเป็นป้ายบอกตำแหน่ง (ไม่เดิน ไม่ฆ่า)
        /// </summary>
        /// <param name="doll">ตุ๊กตาที่จะไปยืนข้าง</param>
        /// <param name="sideOffset">ยืนห่างจากตุ๊กตากี่เมตร</param>
        public void StandBeside(Transform doll, float sideOffset)
        {
            if (doll == null)
            {
                _mode = GhostMode.Idle;
                return;
            }

            _mode        = GhostMode.Marker;
            _sprintTimer = 0f;
            IsBeingStared = false;

            Vector3 target = ResolveStandPosition(doll.position, sideOffset);

            // ต้องใช้ Warp ไม่ใช่ transform.position — NavMeshAgent เป็นคนคุมตำแหน่ง
            if (_agent != null && _agent.enabled) _agent.Warp(target);
            else                                  transform.position = target;

            SetMoving(false, false);
        }

        /// <summary>หาจุดยืนข้างตุ๊กตาที่อยู่บน NavMesh จริง กันผีไปโผล่ในกำแพง</summary>
        private Vector3 ResolveStandPosition(Vector3 dollPos, float sideOffset)
        {
            // ลองสุ่มรอบตุ๊กตาหลายทิศ เอาทิศแรกที่ลงบน NavMesh ได้
            for (int i = 0; i < 8; i++)
            {
                float   angle = Random.Range(0f, 360f);
                Vector3 dir   = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                if (NavMesh.SamplePosition(dollPos + dir * sideOffset, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    return hit.position;
            }

            // ไม่เจอเลย → ยืนทับตำแหน่งตุ๊กตาไปก่อน
            if (NavMesh.SamplePosition(dollPos, out NavMeshHit fallback, 3f, NavMesh.AllAreas))
                return fallback.position;

            return dollPos;
        }

        /// <summary>เข้าโหมดไล่ผู้เล่น</summary>
        public void BeginChase()
        {
            _mode        = GhostMode.Chase;
            _repathTimer = 0f;
            _wasHidden   = IsPlayerHidden;

            if (windupOnFirstChase) StartWindup();
        }

        /// <summary>
        /// ท่าคอหักก่อนพุ่ง — ผียืนนิ่ง เล่น BeforeChase + เสียงกระดูกหัก แล้วค่อยออกวิ่ง
        /// เรียกตอนเริ่มไล่ครั้งแรก และทุกครั้งที่ผู้เล่นปล่อยการกลั้นหายใจ
        /// </summary>
        private void StartWindup()
        {
            if (beforeChaseDuration <= 0f) return;

            _windupTimer = beforeChaseDuration;
            SetMoving(false, false);
            SetAnimState(run: false, walk: false);   // → BeforeChase

            AudioManager.instance.PlayBonesCrack(transform.position);

            // หัวเราะตามหลังกระดูกหัก — ไม่ยิงพร้อมกัน ไม่งั้นสองเสียงตีกันฟังไม่ออก
            _laughTimer = Mathf.Max(0.01f, laughDelayAfterCrack);
        }

        /// <summary>นับถอยหลังแล้วยิงเสียงหัวเราะ — ถูกยกเลิกถ้าผู้เล่นกลั้นหายใจซ้ำก่อนถึงเวลา</summary>
        private void UpdateLaugh()
        {
            if (_laughTimer <= 0f) return;

            _laughTimer -= Time.deltaTime;
            if (_laughTimer > 0f) return;

            AudioManager.instance.PlayGhostOnBreathRelease(transform.position);
        }

        /// <summary>เร่งความเร็วพื้นฐานหลังผู้เล่นวางตุ๊กตาได้ 1 ตัว</summary>
        public void OnDollDelivered()
        {
            _baseSpeed += speedGainPerDoll;
        }

        /// <summary>ผู้เล่นกลั้นหายใจไม่ไหวจนถูกบังคับหายใจออก → ผีวิ่งเข้ามาชั่วขณะ</summary>
        public void StartSprint()
        {
            if (_mode != GhostMode.Chase) return;
            _sprintTimer = sprintDuration;
        }

        /// <summary>หยุดผีถาวร (จบกฎ / ตาย / ถูกซ่อน) — ตัดเสียงและ AI ทั้งหมด</summary>
        public void Deactivate()
        {
            _mode        = GhostMode.Idle;
            _laughTimer  = 0f;
            _windupTimer = 0f;
            SetMoving(false, false);
        }

        // ─────────────────────────── Update ───────────────────────────

        private void Update()
        {
            if (_mode == GhostMode.Idle || _player == null) return;

            if (_mode == GhostMode.Marker)
            {
                UpdateMarker();
                return;
            }

            // ช่วงคอหักไม่นับเวลา sprint — ให้ไปพุ่งเต็มๆ หลังออกวิ่งจริง
            if (_sprintTimer > 0f && _windupTimer <= 0f) _sprintTimer -= Time.deltaTime;

            UpdateChase();
            UpdateLaugh();
            UpdateFootsteps();
            UpdateRunAnimation();
        }

        /// <summary>ยืนนิ่ง หันหน้าตามผู้เล่นช้าๆ — ไม่เดิน ไม่ตรวจ killDistance</summary>
        private void UpdateMarker() => FaceTowards(_player.position, markerTurnSpeed);

        private float _nextHeightLog;

        private void FaceTowards(Vector3 target, float turnSpeed)
        {
            if (turnSpeed <= 0f) return;

            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * turnSpeed
            );
        }

        private void UpdateChase()
        {
            // ผู้เล่นกลั้นหายใจ → ผีมองไม่เห็น เดินเร่ร่อนไปเรื่อย ไม่สนทิศผู้เล่น (ยกเลิกท่าคอหักที่ค้างอยู่ด้วย)
            if (IsPlayerHidden)
            {
                _windupTimer = 0f;
                _laughTimer  = 0f;   // กลั้นซ้ำก่อนถึงคิวหัวเราะ → ไม่หัวเราะ (ผีไม่เห็นเราแล้ว)

                if (!_wasHidden) _wanderTimer = 0f;   // เพิ่งเริ่มกลั้น → สุ่มจุดใหม่ทันที
                _wasHidden = true;

                UpdateWander();
                return;
            }

            // เพิ่งปล่อยกลั้นหายใจ → ไม่พุ่งทันที เล่นท่าคอหักก่อน
            if (_wasHidden)
            {
                _wasHidden = false;
                StartWindup();
            }

            // กำลังคอหัก → ยืนนิ่ง หันหน้าหาผู้เล่น รอจนจบท่า
            if (_windupTimer > 0f)
            {
                _windupTimer -= Time.deltaTime;
                SetMoving(false, false);
                FaceTowards(_player.position, markerTurnSpeed);
                return;
            }

            bool sprinting = _sprintTimer > 0f && !IsBeingStared;

            if (IsBeingStared)
            {
                _agent.speed        = staredSpeed;
                _agent.acceleration = staredBrake;

                
                if (!_wasStared) _agent.velocity = Vector3.zero;
            }
            else
            {
                _agent.acceleration = _baseAccel;
                _agent.speed        = sprinting ? sprintSpeed : _baseSpeed;
            }
            _wasStared = IsBeingStared;

            SetMoving(true, sprinting);
            Repath(_player.position);

            if (!_caught && Vector3.Distance(transform.position, _player.position) <= killDistance)
            {
                _caught = true;
                _mode   = GhostMode.Idle;
                if (_rule != null) _rule.OnGhostCaughtPlayer();
            }
        }

        /// <summary>
        /// เดินเร่ร่อนตอนมองไม่เห็นผู้เล่น — สุ่มจุดบน NavMesh รอบตัว โดยไม่เลือกจุดที่มุ่งหน้าหาผู้เล่น
        /// ให้ผู้เล่นรู้สึกว่า "มันหาเราอยู่" แต่ไม่ใช่การไล่ (ไม่ตรวจ killDistance ในโหมดนี้)
        /// </summary>
        private void UpdateWander()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            _agent.speed        = wanderSpeed;
            _agent.acceleration = _baseAccel;
            _agent.isStopped    = false;
            SetAnimState(run: false, walk: true);

            _wanderTimer -= Time.deltaTime;
            bool arrived = !_agent.pathPending && _agent.remainingDistance <= Mathf.Max(_agent.stoppingDistance, 0.3f);
            if (_wanderTimer > 0f && !arrived) return;

            _wanderTimer = wanderRepathInterval;

            if (TryPickWanderPoint(out Vector3 point))
                _agent.SetDestination(point);
        }

        private bool TryPickWanderPoint(out Vector3 point)
        {
            Vector3 toPlayer = _player.position - transform.position;
            toPlayer.y = 0f;
            toPlayer.Normalize();

            // ลองหลายครั้ง — ทิ้งจุดที่พาเข้าหาผู้เล่น และจุดที่ไม่อยู่บน NavMesh
            for (int i = 0; i < 10; i++)
            {
                Vector2 r   = Random.insideUnitCircle.normalized * Random.Range(wanderRadius * 0.4f, wanderRadius);
                Vector3 dir = new Vector3(r.x, 0f, r.y);

                if (Vector3.Dot(dir.normalized, toPlayer) > wanderMaxDotToPlayer) continue;

                if (NavMesh.SamplePosition(transform.position + dir, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }

            point = transform.position;
            return false;
        }

        private void Repath(Vector3 destination)
        {
            _repathTimer -= Time.deltaTime;
            if (_repathTimer > 0f) return;

            _repathTimer = repathInterval;
            if (_agent.enabled && _agent.isOnNavMesh) _agent.SetDestination(destination);
        }

        private void SetMoving(bool moving, bool running)
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = !moving;

            // ท่าไล่มีท่าเดียวคือวิ่ง — isRun เปิดค้างตลอดโหมดไล่ (ยกเว้นช่วงคอหัก)
            // ความเร็วอนิเมชั่นตามความเร็วจริงอยู่แล้ว วิ่ง sprint ก็เร็วขึ้นเอง
            SetAnimState(run: _mode == GhostMode.Chase && _windupTimer <= 0f, walk: false);
        }

        /// <summary>เซ็ต bool ของ Animator ทีเดียวทั้งคู่ — กัน isRun/isWalk เปิดพร้อมกันแล้ว state ตีกัน</summary>
        private void SetAnimState(bool run, bool walk)
        {
            if (animator == null) return;
            if (!string.IsNullOrEmpty(runBoolName))  animator.SetBool(runBoolName,  run);
            if (!string.IsNullOrEmpty(walkBoolName)) animator.SetBool(walkBoolName, walk);
        }

        /// <summary>
        /// ผูกความเร็วอนิเมชั่นวิ่งกับความเร็วที่ผีขยับได้จริง
        /// ใช้ velocity ไม่ใช่ agent.speed — เหตุผลเดียวกับฝีเท้า: ค่าจริงที่ขยับได้ ไม่ใช่ค่าที่ตั้งไว้
        /// ผลคือถูกจ้อง / กลั้นหายใจ → ผีชะลอ → ท่าวิ่งช้าลงตาม ไม่ต้องมี state แยก
        /// </summary>
        private void UpdateRunAnimation()
        {
            if (animator == null) return;
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            float speed = _agent.isStopped ? 0f : _agent.velocity.magnitude;

            // ท่าวิ่ง — ตอนกลั้นหายใจผีอยู่ state Walk ค่านี้ไม่มีผล แต่ดึงลงไว้ให้ตอนกลับมา Run ไม่กระตุก
            if (!string.IsNullOrEmpty(runSpeedParamName))
            {
                float runMult = runClipNaturalSpeed > 0f ? (IsPlayerHidden ? 0f : speed) / runClipNaturalSpeed : 1f;
                animator.SetFloat(runSpeedParamName, Mathf.Clamp(runMult, minRunAnimSpeed, maxRunAnimSpeed));
            }

            // ท่าเดิน — ผูกกับความเร็วจริงตอนเดินเร่ร่อน วิธีเดียวกับ Run
            if (!string.IsNullOrEmpty(walkSpeedParamName))
            {
                float walkMult = walkClipNaturalSpeed > 0f ? speed / walkClipNaturalSpeed : 1f;
                animator.SetFloat(walkSpeedParamName, Mathf.Clamp(walkMult, minWalkAnimSpeed, maxWalkAnimSpeed));
            }
        }

        /// <summary>
        /// ยิงเสียงฝีเท้าทีละก้าวตามระยะทางที่ "เดินได้จริง" ไม่ใช่ตามเวลา
        ///
        /// ใช้ _agent.velocity ไม่ใช่ _agent.speed เพราะ speed เป็นแค่ค่าที่ตั้งไว้
        /// ส่วน velocity คือความเร็วจริงที่ขยับได้ (ชนกำแพง / เลี้ยว / กำลังเบรก จะช้าลงเอง)
        ///
        /// ผลคือจังหวะก้าวสอดคล้องกับความเร็วทุกกรณีโดยอัตโนมัติ:
        ///   ถูกจ้อง 0.5 m/s  → ก้าวห่าง ~1.8 วิ/ก้าว
        ///   ไล่ปกติ 1.6 m/s  → ~0.56 วิ/ก้าว
        ///   วิ่ง 3.2 m/s     → ~0.28 วิ/ก้าว
        /// </summary>
        private void UpdateFootsteps()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
            if (strideLength <= 0f) return;

            // ต้องเช็คสถานะ "ถูกสั่งหยุด" ตรงๆ ห้ามพึ่ง velocity อย่างเดียว
            // NavMeshAgent.isStopped = true ไม่ได้ทำให้ velocity เป็น 0 ทันที มันค่อยๆ ชะลอ
            // และบางกรณีค้างค่าไว้ ทำให้ฝีเท้ายังลงต่อทั้งที่ผีหยุดแล้ว
            if (_agent.isStopped)
            {
                _strideAccum = 0f;
                return;
            }

            float speed = _agent.velocity.magnitude;

            if (speed < footstepMinSpeed)
            {
                _strideAccum = 0f;   // หยุดแล้ว เริ่มนับก้าวใหม่ กันลงเท้าทันทีที่ออกตัว
                return;
            }

            _strideAccum += speed * Time.deltaTime;
            if (_strideAccum < strideLength) return;

            _strideAccum -= strideLength;

            // กันรัว: ต่อให้ระยะครบแล้ว ก็ไม่ยิงถี่กว่า minFootstepInterval
            if (minFootstepInterval > 0f && Time.time - _lastFootstepTime < minFootstepInterval) return;

            _lastFootstepTime = Time.time;
            AudioManager.instance.PlayGhostFootstep(transform.position);
        }
    }
}
