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
    ///   ผู้เล่นจ้องอยู่       → staredSpeed (ช้าลง แต่ไม่หยุด)
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

        [Tooltip("ความเร็วตอนถูกผู้เล่นจ้อง — ช้าลงแต่ไม่หยุด")]
        [SerializeField] private float staredSpeed = 0.5f;

        [Tooltip("ความเร็วตอนวิ่งเข้าหาผู้เล่นหลังถูกบังคับหายใจออก")]
        [SerializeField] private float sprintSpeed = 3.2f;

        [Tooltip("วิ่งนานกี่วินาทีหลังผู้เล่นถูกบังคับหายใจออก")]
        [SerializeField] private float sprintDuration = 4f;

        [Tooltip("ความเร็วที่เพิ่มขึ้นทุกครั้งที่ผู้เล่นวางตุ๊กตาสำเร็จ (กดดันขึ้นเรื่อยๆ)")]
        [SerializeField] private float speedGainPerDoll = 0.15f;

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
        [Tooltip("ชื่อ bool parameter ใน Animator สำหรับท่าเดิน")]
        [SerializeField] private string walkBoolName = "isWalk";
        [Tooltip("ชื่อ bool parameter ใน Animator สำหรับท่าวิ่ง (เว้นว่างได้ถ้าไม่มี)")]
        [SerializeField] private string runBoolName = "isRun";

        // ── Runtime ──
        private NavMeshAgent          _agent;
        private Transform             _player;
        private RuleSystem.Rule.Rule4 _rule;
        private GhostMode             _mode = GhostMode.Idle;
        private float                 _repathTimer;
        private float                 _baseSpeed;
        private float                 _sprintTimer;
        private bool                  _caught;
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
            _mode = GhostMode.Idle;
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

            if (_sprintTimer > 0f) _sprintTimer -= Time.deltaTime;

            UpdateChase();
            UpdateFootsteps();
        }

        /// <summary>ยืนนิ่ง หันหน้าตามผู้เล่นช้าๆ — ไม่เดิน ไม่ตรวจ killDistance</summary>
        private void UpdateMarker()
        {
            if (markerTurnSpeed <= 0f) return;

            Vector3 dir = _player.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * markerTurnSpeed
            );
        }

        private void UpdateChase()
        {
            // ผู้เล่นกลั้นหายใจ → ผีมองไม่เห็น หยุดรออยู่กับที่
            if (IsPlayerHidden)
            {
                SetMoving(false, false);
                return;
            }

            bool sprinting = _sprintTimer > 0f && !IsBeingStared;

            if (IsBeingStared)  _agent.speed = staredSpeed;
            else if (sprinting) _agent.speed = sprintSpeed;
            else                _agent.speed = _baseSpeed;

            SetMoving(true, sprinting);
            Repath(_player.position);

            if (!_caught && Vector3.Distance(transform.position, _player.position) <= killDistance)
            {
                _caught = true;
                _mode   = GhostMode.Idle;
                if (_rule != null) _rule.OnGhostCaughtPlayer();
            }
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

            if (animator == null) return;

            animator.SetBool(walkBoolName, moving);
            if (!string.IsNullOrEmpty(runBoolName)) animator.SetBool(runBoolName, moving && running);
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
            if (IsPlayerHidden || _agent.isStopped)
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
