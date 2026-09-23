using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Rule5
{
    public enum Rule5GhostMode
    {
        Idle,       // ไม่ทำอะไร
        Wait,       // ยืนรอที่จุดเริ่มสาย หันหน้าตามผู้เล่น
        Escort,     // เดินเคียงข้างผู้เล่นตามสาย (ตอนจับสายอยู่)
        Approach,   // ผู้เล่นไม่ได้จับสาย → เดินเข้าหาช้าๆ ใกล้เกิน = ตาย
        Chase,      // ระฆังครบแล้วปล่อยมือ → วิ่งไล่
        Jumpscare   // โผล่หน้าแล้วพุ่งเข้าหา (ตาย)
    }

    /// <summary>
    /// ผีของ Rule 5 — เดินไปพร้อมกับผู้เล่นตามสายสิญจน์
    ///
    ///   Wait     : เริ่มกฎ ยืนรออยู่ข้างจุดเริ่มสาย
    ///   Escort   : ผู้เล่นจับสาย → เดินเคียงข้างอีกฝั่งของสาย นำหน้าเล็กน้อย
    ///   Approach : ผู้เล่นไม่ได้จับ → ค่อยๆ เดินเข้าหา เข้าใกล้กว่า killDistance = ตาย
    ///   Chase    : ระฆังครบ 3 แล้วผู้เล่นปล่อยมือ → วิ่งไล่ทันที
    ///   Jumpscare: ตายแบบผิดเงื่อนไข → โผล่ตรงหน้า พุ่งเข้ามา (New Animation)
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Rule5Ghost : MonoBehaviour
    {
        [Header("Speed")]
        [SerializeField] private float escortSpeed   = 2.2f;
        [SerializeField] private float approachSpeed = 0.9f;
        [SerializeField] private float chaseSpeed    = 3.6f;

        [Header("Escort (เดินเคียงข้าง)")]
        [Tooltip("ยืนห่างจากเส้นไปอีกฝั่ง (ตรงข้ามผู้เล่น) กี่เมตร")]
        [SerializeField] private float escortSideOffset = 1.2f;

        [Tooltip("นำหน้าผู้เล่นบนเส้นกี่เมตร (ติดลบ = ตามหลัง)")]
        [SerializeField] private float escortLead = 0.8f;

        [Tooltip("ถ้าหลุดไกลจากจุดที่ควรอยู่เกินนี้ → วาร์ปกลับมา (กันติดกำแพงแล้วหายไปเลย)")]
        [SerializeField] private float escortTeleportDistance = 8f;

        [Header("Approach / Chase")]
        [Tooltip("ผู้เล่นปล่อยมือแล้วผีรอกี่วินาทีก่อนเริ่มเดินเข้าหา")]
        [SerializeField] private float approachDelay = 2f;

        [Tooltip("ระยะที่ผีจับได้ → ตาย (Approach / Chase)")]
        [SerializeField] private float killDistance = 1.3f;

        [Tooltip("หันหน้าตามผู้เล่นเร็วแค่ไหนตอนยืนรอ")]
        [SerializeField] private float turnSpeed = 2f;

        [Header("Jumpscare")]
        [Tooltip("โผล่ห่างจากหน้าผู้เล่นกี่เมตร")]
        [SerializeField] private float jumpscareStartDistance = 3.5f;

        [Tooltip("พุ่งเข้ามาจนเหลือระยะเท่านี้")]
        [SerializeField] private float jumpscareEndDistance = 0.45f;

        [Tooltip("ยืนโชว์หน้ากี่วินาทีก่อนพุ่ง")]
        [SerializeField] private float jumpscareHold = 0.6f;

        [Tooltip("พุ่งใช้เวลากี่วินาที")]
        [SerializeField] private float jumpscareRush = 0.35f;

        [Tooltip("ค้างหน้าอยู่กี่วินาทีหลังพุ่งถึง ก่อนตัดจอ")]
        [SerializeField] private float jumpscareLinger = 0.8f;

        [Header("Footsteps")]
        [SerializeField] private float strideLength      = 0.9f;
        [SerializeField] private float footstepMinSpeed  = 0.05f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string   walkBoolName      = "isWalk";
        [SerializeField] private string   runBoolName       = "isRun";
        [SerializeField] private string   jumpscareTrigger  = "jumpscare";

        [Header("Repath")]
        [SerializeField] private float repathInterval = 0.2f;

        // ── Runtime ──
        private NavMeshAgent       _agent;
        private Transform          _player;
        private SacredThreadWalker _walker;
        private Action             _onCaught;
        private Rule5GhostMode     _mode = Rule5GhostMode.Idle;
        private float              _repathTimer;
        private float              _approachTimer;
        private float              _strideAccum;
        private bool               _caught;

        public Rule5GhostMode Mode => _mode;

        // ═══════════════════════════════════════════════════════════════
        #region Setup / Mode
        // ═══════════════════════════════════════════════════════════════

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        public void Init(Transform player, SacredThreadWalker walker, Action onCaught)
        {
            _player   = player;
            _walker   = walker;
            _onCaught = onCaught;
            _caught   = false;
        }

        /// <summary>ยืนรอที่จุด (ข้างจุดเริ่มสาย)</summary>
        public void EnterWait(Vector3 position)
        {
            _mode = Rule5GhostMode.Wait;
            Warp(position);
            SetMoving(false);
            SetAnim(walk: false, run: false);
        }

        public void EnterEscort()
        {
            _mode        = Rule5GhostMode.Escort;
            _repathTimer = 0f;
            _agent.speed = escortSpeed;
        }

        /// <summary>ผู้เล่นไม่ได้จับสาย → รอ approachDelay แล้วเดินเข้าหา</summary>
        public void EnterApproach()
        {
            if (_mode == Rule5GhostMode.Chase) return;   // ไล่อยู่แล้ว ไม่ลดระดับ
            _mode          = Rule5GhostMode.Approach;
            _approachTimer = approachDelay;
            _repathTimer   = 0f;
            SetMoving(false);
            SetAnim(walk: false, run: false);
        }

        public void BeginChase()
        {
            _mode        = Rule5GhostMode.Chase;
            _repathTimer = 0f;
            _agent.speed = chaseSpeed;
            AudioManager.instance.PlayRule5GhostChaseStart(transform.position);
        }

        public void Deactivate()
        {
            _mode = Rule5GhostMode.Idle;
            SetMoving(false);
            SetAnim(walk: false, run: false);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Update
        // ═══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (_player == null) return;

            switch (_mode)
            {
                case Rule5GhostMode.Wait:     FaceTowards(_player.position); break;
                case Rule5GhostMode.Escort:   UpdateEscort();   break;
                case Rule5GhostMode.Approach: UpdateApproach(); break;
                case Rule5GhostMode.Chase:    UpdateChase();    break;
            }

            if (_mode != Rule5GhostMode.Idle && _mode != Rule5GhostMode.Jumpscare) UpdateFootsteps();
        }

        private void UpdateEscort()
        {
            if (_walker == null || _walker.Thread == null) return;

            var   thread = _walker.Thread;
            float d      = thread.WrapDistance(_walker.Distance + escortLead);
            // อีกฝั่งของสาย = ทางซ้ายของทิศเดิน (ผู้เล่นอยู่ขวา)
            Vector3 target = thread.GetGroundPoint(d) - thread.GetRight(d) * escortSideOffset;

            if (Vector3.Distance(transform.position, target) > escortTeleportDistance)
                Warp(target);

            bool playerMoving = _walker.IsWalking;
            _agent.speed = escortSpeed;
            SetMoving(true);
            Repath(target);

            // ผู้เล่นหยุด → ผีก็หยุดข้างๆ หันมามอง
            bool arrived = !_agent.pathPending && _agent.remainingDistance <= Mathf.Max(_agent.stoppingDistance, 0.25f);
            if (!playerMoving && arrived)
            {
                SetMoving(false);
                SetAnim(walk: false, run: false);
                FaceTowards(_player.position);
            }
            else
            {
                SetAnim(walk: true, run: false);
            }
        }

        private void UpdateApproach()
        {
            if (_approachTimer > 0f)
            {
                _approachTimer -= Time.deltaTime;
                FaceTowards(_player.position);
                return;
            }

            _agent.speed = approachSpeed;
            SetMoving(true);
            SetAnim(walk: true, run: false);
            Repath(_player.position);
            CheckKill();
        }

        private void UpdateChase()
        {
            _agent.speed = chaseSpeed;
            SetMoving(true);
            SetAnim(walk: false, run: true);
            Repath(_player.position);
            CheckKill();
        }

        private void CheckKill()
        {
            if (_caught) return;
            if (Vector3.Distance(transform.position, _player.position) > killDistance) return;

            _caught = true;
            _mode   = Rule5GhostMode.Idle;
            SetMoving(false);
            _onCaught?.Invoke();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Jumpscare
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// โผล่ตรงหน้าผู้เล่นแล้วพุ่งเข้ามา — คนเรียกต้องล็อกกล้องผู้เล่น (SetLook(false) + LookAtWorldPoint) เอง
        /// </summary>
        public IEnumerator JumpscareRoutine(Transform playerCamera)
        {
            _mode = Rule5GhostMode.Jumpscare;
            if (_agent != null && _agent.enabled) _agent.enabled = false;

            Vector3 camFwd = playerCamera.forward; camFwd.y = 0f;
            if (camFwd.sqrMagnitude < 0.001f) camFwd = _player.forward;
            camFwd.Normalize();

            Vector3 basePos = _player.position;
            Vector3 start   = GroundAt(basePos + camFwd * jumpscareStartDistance);
            Vector3 end     = GroundAt(basePos + camFwd * jumpscareEndDistance);

            transform.position = start;
            transform.rotation = Quaternion.LookRotation(-camFwd, Vector3.up);
            SetAnim(walk: false, run: false);
            if (animator != null && !string.IsNullOrEmpty(jumpscareTrigger)) animator.SetTrigger(jumpscareTrigger);
            AudioManager.instance.PlayRule5Jumpscare(transform.position);

            yield return new WaitForSeconds(jumpscareHold);

            float t = 0f;
            while (t < jumpscareRush)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / jumpscareRush);
                transform.position = Vector3.Lerp(start, end, k);
                yield return null;
            }
            transform.position = end;

            yield return new WaitForSeconds(jumpscareLinger);
        }

        private static Vector3 GroundAt(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f))
                return hit.point;
            return p;
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Helpers
        // ═══════════════════════════════════════════════════════════════

        private void Warp(Vector3 pos)
        {
            if (_agent != null && _agent.enabled)
            {
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) pos = hit.position;
                _agent.Warp(pos);
            }
            else transform.position = pos;
        }

        private void Repath(Vector3 destination)
        {
            _repathTimer -= Time.deltaTime;
            if (_repathTimer > 0f) return;
            _repathTimer = repathInterval;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.SetDestination(destination);
        }

        private void SetMoving(bool moving)
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = !moving;
        }

        private void SetAnim(bool walk, bool run)
        {
            if (animator == null) return;
            if (!string.IsNullOrEmpty(walkBoolName)) animator.SetBool(walkBoolName, walk);
            if (!string.IsNullOrEmpty(runBoolName))  animator.SetBool(runBoolName,  run);
        }

        private void FaceTowards(Vector3 target)
        {
            Vector3 dir = target - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * turnSpeed);
        }

        /// <summary>ฝีเท้าตามระยะที่เดินได้จริง (วิธีเดียวกับ Rule4Ghost)</summary>
        private void UpdateFootsteps()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh || strideLength <= 0f) return;
            if (_agent.isStopped) { _strideAccum = 0f; return; }

            float speed = _agent.velocity.magnitude;
            if (speed < footstepMinSpeed) { _strideAccum = 0f; return; }

            _strideAccum += speed * Time.deltaTime;
            if (_strideAccum < strideLength) return;
            _strideAccum -= strideLength;
            AudioManager.instance.PlayGhostFootstep(transform.position);
        }

        #endregion
    }
}
