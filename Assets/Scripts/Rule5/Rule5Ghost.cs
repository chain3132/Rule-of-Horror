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
        Escort,     // เดินตามหลังผู้เล่นตามสาย (ตอนจับสายอยู่)
        Approach,   // ผู้เล่นไม่ได้จับสาย → เดินเข้าหาช้าๆ ใกล้เกิน = ตาย
        Chase,      // ระฆังครบแล้วปล่อยมือ → วิ่งไล่
        Jumpscare   // โผล่หน้าแล้วพุ่งเข้าหา (ตาย)
    }

    /// <summary>ผีเดินตามหลังฝั่งไหนของสาย — ผู้เล่นเดินอยู่ฝั่งขวาของสาย ปกติจึงให้ผีอยู่ฝั่งซ้าย</summary>
    public enum EscortSide { Left, Right }

    /// <summary>
    /// ผีของ Rule 5 — เดินไปพร้อมกับผู้เล่นตามสายสิญจน์
    ///
    ///   Wait     : เริ่มกฎ ยืนรออยู่ข้างจุดเริ่มสาย
    ///   Escort   : ผู้เล่นจับสาย → เดินตามหลังตรงๆ (escortAngle ~170°)
    ///              ปกติมองไม่เห็น ต้องชะเง้อจนสุดขอบที่ lookHalfAngle อนุญาตถึงจะเห็นที่หางตา
    ///              เดินทั้งที่ฉิ่งดัง → PressEscort() ทำให้มันขยับเข้ามาใกล้เรื่อยๆ จนฆ่า
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

        [Header("Escort (walking behind the player)")]
        [Tooltip("Angle between the direction of travel and the ghost, in degrees. 0 = straight ahead, 180 = directly behind. " +
                 "Keep it near 180 so the ghost really is behind the player: what decides whether they can glance at it is the " +
                 "look limit on SacredThreadWalker, not this. A few degrees off 180 keeps it out of the player's own footsteps.")]
        [Range(90f, 180f)]
        [SerializeField] private float escortAngle = 170f;

        [Tooltip("Side of the thread the ghost trails on. The player walks on the right of the thread, so Left keeps it clear of them.")]
        [SerializeField] private EscortSide escortSide = EscortSide.Left;

        [Tooltip("Distance the ghost keeps from the player while nothing is wrong, in metres.")]
        [SerializeField] private float escortDistance = 3f;

        [Tooltip("If the ghost drifts further than this from where it should be, it is warped back. Stops it getting stuck on a wall and lost.")]
        [SerializeField] private float escortTeleportDistance = 8f;

        [Header("Escort - closing in (walking while the ching rings)")]
        [Tooltip("Metres the ghost closes in for every second the player keeps walking while the ching rings.")]
        [SerializeField] private float escortCloseInSpeed = 0.35f;

        [Tooltip("Metres the ghost drops back per second once the player stops breaking the rule. Set to 0 to make the distance lost permanent.")]
        [SerializeField] private float escortRecoverSpeed = 0.15f;

        [Tooltip("The ghost gets this close behind the player while escorting and the player dies, in metres.")]
        [SerializeField] private float escortKillDistance = 0.8f;

        [Header("Approach / Chase")]
        [Tooltip("Seconds the ghost waits after the player lets go before walking towards them.")]
        [SerializeField] private float approachDelay = 2f;

        [Tooltip("Distance at which the ghost catches the player and kills them (Approach / Chase only).")]
        [SerializeField] private float killDistance = 1.3f;

        [Tooltip("How fast the ghost turns to face the player while standing and waiting.")]
        [SerializeField] private float turnSpeed = 2f;

        [Header("Jumpscare")]
        [Tooltip("How far in front of the player the ghost appears, in metres.")]
        [SerializeField] private float jumpscareStartDistance = 3.5f;

        [Tooltip("Distance the ghost rushes in to, in metres.")]
        [SerializeField] private float jumpscareEndDistance = 0.45f;

        [Tooltip("Seconds the ghost stands there showing its face before rushing in.")]
        [SerializeField] private float jumpscareHold = 0.6f;

        [Tooltip("Duration of the rush, in seconds.")]
        [SerializeField] private float jumpscareRush = 0.35f;

        [Tooltip("Seconds the face lingers after the rush lands, before the screen cuts away.")]
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

        [Header("No NavMesh fallback")]
        [Tooltip("Keep the ghost moving by driving its Transform when the NavMeshAgent cannot be used " +
                 "(nothing baked here, or it spawned off the mesh). Untick to have it stand still instead.")]
        [SerializeField] private bool moveWithoutNavMesh = true;

        [Tooltip("Search radius used to snap the ghost onto the NavMesh, in metres. " +
                 "Raised well above the usual 2-3 m because spawn points often sit off the baked area.")]
        [SerializeField] private float navMeshSnapRadius = 12f;

        [Tooltip("Fallback movement only: raycast down to stay on the ground instead of floating.")]
        [SerializeField] private bool fallbackStickToGround = true;

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
        private Vector3            _lastPosition;
        private bool               _warnedNoNavMesh;
        private float              _escortDistance;
        private float              _pressedUntil;     // เวลาล่าสุดที่โดน PressEscort — กันลำดับ Update สลับกัน

        public Rule5GhostMode Mode => _mode;

        /// <summary>ระยะที่ผีตามหลังอยู่ตอนนี้ (เมตร) — หดลงทุกครั้งที่ผู้เล่นเดินตอนฉิ่งดัง</summary>
        public float EscortDistance => _escortDistance;

        /// <summary>0 = ตามหลังห่างปกติ, 1 = ประชิดจนฆ่า — Rule5 เอาไปทำเสียงหัวใจ / HUD</summary>
        public float EscortCloseness01 =>
            Mathf.Clamp01(Mathf.InverseLerp(escortDistance, escortKillDistance, _escortDistance));

        /// <summary>
        /// ผู้เล่นทำผิด (เดินทั้งที่ฉิ่งดัง) → ผีที่ตามหลังขยับเข้ามาใกล้ขึ้นตามเวลาที่ยังฝืนเดิน
        /// เรียกทุกเฟรมที่ยังผิดอยู่ พอหยุดเรียกมันจะค่อยๆ ถอยกลับไปเองด้วย escortRecoverSpeed
        /// </summary>
        public void PressEscort(float deltaTime)
        {
            if (_mode != Rule5GhostMode.Escort || _caught) return;
            _pressedUntil   = Time.time + 0.1f;
            _escortDistance = Mathf.Max(escortKillDistance, _escortDistance - escortCloseInSpeed * deltaTime);
        }

        /// <summary>คืนระยะตามหลังเป็นค่าปกติ (เริ่มกฎใหม่ / ให้อภัยผู้เล่น)</summary>
        public void ResetEscortDistance() => _escortDistance = escortDistance;

        // ═══════════════════════════════════════════════════════════════
        #region Setup / Mode
        // ═══════════════════════════════════════════════════════════════

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _lastPosition = transform.position;
            ResetEscortDistance();

            // spawn ห่าง NavMesh → Unity สร้าง agent ไม่ได้ ("no valid NavMesh") แล้วทุก property
            // ของ agent จะ throw ทันที ลองดึงเข้าหา NavMesh ที่ใกล้สุดก่อนตั้งแต่เฟรมแรก
            TryPlaceOnNavMesh(transform.position);
        }

        /// <summary>
        /// ย้ายตัวไปจุดที่อยู่บน NavMesh จริงแล้วเปิด agent ใหม่ (toggle enabled = สั่งให้ Unity สร้าง agent อีกรอบ)
        /// คืน true ถ้าสุดท้ายยืนอยู่บน NavMesh ได้ — ถ้า false ผีจะเดินด้วย Transform แทน
        /// </summary>
        private bool TryPlaceOnNavMesh(Vector3 near)
        {
            if (_agent == null) return false;

            if (_agent.enabled && _agent.isOnNavMesh) return true;

            // ไม่มี NavMesh แถวนี้ → ปิด agent ทิ้งไว้เลย
            // ถ้าปล่อยให้ enabled ค้าง Unity จะ log "Failed to create agent..." ซ้ำทุกครั้งที่แตะมัน
            if (!NavMesh.SamplePosition(near, out NavMeshHit hit, navMeshSnapRadius, NavMesh.AllAreas))
            {
                if (_agent.enabled) _agent.enabled = false;
                return false;
            }

            _agent.enabled = false;
            transform.position = hit.position;
            _agent.enabled = true;   // เปิดใหม่ = สั่งให้ Unity สร้าง agent อีกรอบ ครั้งนี้อยู่บน NavMesh แล้ว
            return _agent.isOnNavMesh;
        }

        /// <summary>true = ใช้ NavMeshAgent ได้จริง — ต้องเช็คก่อนแตะ property ใดๆ ของ agent</summary>
        private bool AgentUsable => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        /// <summary>เตือนครั้งเดียว ไม่ใช่ทุกเฟรม — ไม่งั้น Console ตายเหมือนเดิม</summary>
        private void WarnNoNavMesh()
        {
            if (_warnedNoNavMesh) return;
            _warnedNoNavMesh = true;

            Debug.LogWarning($"[Rule5] ผี '{name}' ไม่ได้อยู่บน NavMesh — " +
                             (moveWithoutNavMesh
                                ? "เดินด้วย Transform ไปก่อน (เดินทะลุของได้ ไม่หลบสิ่งกีดขวาง) " +
                                  "ถ้าอยากให้เดินหลบจริง ต้อง bake NavMesh คลุมเส้นสายสิญจน์"
                                : "และปิด moveWithoutNavMesh ไว้ → ผีจะยืนนิ่ง"), this);
        }

        public void Init(Transform player, SacredThreadWalker walker, Action onCaught)
        {
            _player   = player;
            _walker   = walker;
            _onCaught = onCaught;
            _caught   = false;
            ResetEscortDistance();
        }

        /// <summary>ยืนรอที่จุด (ข้างจุดเริ่มสาย)</summary>
        public void EnterWait(Vector3 position)
        {
            _mode = Rule5GhostMode.Wait;
            ResetEscortDistance();
            Warp(position);
            SetMoving(false);
            SetAnim(walk: false, run: false);
        }

        public void EnterEscort()
        {
            _mode        = Rule5GhostMode.Escort;
            _repathTimer = 0f;
            SetAgentSpeed(escortSpeed);
        }

        /// <summary>ผู้เล่นไม่ได้จับสาย → รอ approachDelay แล้วเดินเข้าหา</summary>
        public void EnterApproach()
        {
            if (_mode == Rule5GhostMode.Chase) return;   // ไล่อยู่แล้ว ไม่ลดระดับ
            _mode          = Rule5GhostMode.Approach;
            _approachTimer = approachDelay;
            _repathTimer   = 0f;
            SetAgentSpeed(approachSpeed);
            SetMoving(false);
            SetAnim(walk: false, run: false);
        }

        /// <summary>
        /// ยืนนิ่งอยู่ตรงที่เดิม หันหน้าตามผู้เล่น — ใช้ตอนผู้เล่นปล่อยมือแต่ยังไม่ได้สวดมนต์
        /// ต่างจาก EnterWait() ตรงที่ไม่ย้ายตำแหน่ง และไม่รีเซ็ตระยะที่ถูกบีบมาจากตอนฉิ่ง
        /// </summary>
        public void EnterStandby()
        {
            if (_mode == Rule5GhostMode.Chase || _mode == Rule5GhostMode.Jumpscare) return;
            _mode = Rule5GhostMode.Wait;
            SetMoving(false);
            SetAnim(walk: false, run: false);
        }

        public void BeginChase()
        {
            _mode        = Rule5GhostMode.Chase;
            _repathTimer = 0f;
            SetAgentSpeed(chaseSpeed);
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

            _lastPosition = transform.position;
        }

        private void UpdateEscort()
        {
            if (_walker == null || _walker.Thread == null) return;

            UpdateEscortDistance();
            if (_caught) return;

            Vector3 target = EscortTarget();

            // หลุดไกลเกิน → ดึงกลับ (โหมด fallback ไม่ต้องดึง เพราะมันเดินเข้าหาเป้าด้วย transform อยู่แล้ว)
            if (AgentUsable && Vector3.Distance(transform.position, target) > escortTeleportDistance)
                Warp(target);

            bool playerMoving = _walker.IsWalking;

            if (!AgentUsable)
            {
                // ไม่มี NavMesh → เดินด้วย Transform "ถึงแล้ว" วัดจากระยะตรงๆ ไม่ใช่ remainingDistance
                bool closeEnough = Vector3.Distance(transform.position, target) <= 0.35f;
                bool moved = !closeEnough && FallbackMoveTowards(target, escortSpeed);

                SetAnim(walk: moved, run: false);
                if (!moved) FaceTowards(_player.position);
                return;
            }

            SetAgentSpeed(escortSpeed);
            SetMoving(true);
            Repath(target);

            // ผู้เล่นหยุด → ผีก็หยุดตามหลัง หันมามอง
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

        /// <summary>
        /// จุดที่ผีควรยืน — วัดจากตัวผู้เล่น ไม่ใช่จากเส้นสาย เพราะสิ่งที่ต้องคุมคือ "มุมที่ผู้เล่นจะเห็น"
        /// ใช้ทิศของสายตรงจุดที่ผู้เล่นยืนเป็นแกน (หน้า/ขวา) แล้วกาง escortAngle ไปด้านหลังฝั่ง escortSide
        /// </summary>
        private Vector3 EscortTarget()
        {
            var     thread = _walker.Thread;
            float   d      = thread.WrapDistance(_walker.Distance);
            Vector3 fwd    = thread.GetForward(d);
            Vector3 side   = thread.GetRight(d) * (escortSide == EscortSide.Left ? -1f : 1f);

            float   rad = escortAngle * Mathf.Deg2Rad;
            Vector3 dir = fwd * Mathf.Cos(rad) + side * Mathf.Sin(rad);
            if (dir.sqrMagnitude < 0.0001f) dir = -fwd;

            return GroundAt(_player.position + dir.normalized * _escortDistance);
        }

        /// <summary>
        /// ระยะตามหลังของเฟรมนี้ — เฟรมไหนไม่โดน PressEscort ก็ถอยกลับไปหาค่าปกติ
        /// (เช็คด้วยเวลา ไม่ใช่ flag เพราะ Rule5.UpdateRule กับ Update ของตัวนี้ไม่การันตีลำดับ)
        /// </summary>
        private void UpdateEscortDistance()
        {
            if (Time.time > _pressedUntil && _escortDistance < escortDistance)
                _escortDistance = Mathf.Min(escortDistance, _escortDistance + escortRecoverSpeed * Time.deltaTime);

            if (_escortDistance <= escortKillDistance + 0.0001f) Caught();
        }

        private void UpdateApproach()
        {
            if (_approachTimer > 0f)
            {
                _approachTimer -= Time.deltaTime;
                FaceTowards(_player.position);
                return;
            }

            if (!AgentUsable)
            {
                SetAnim(walk: FallbackMoveTowards(_player.position, approachSpeed), run: false);
                CheckKill();
                return;
            }

            SetAgentSpeed(approachSpeed);
            SetMoving(true);
            SetAnim(walk: true, run: false);
            Repath(_player.position);
            CheckKill();
        }

        private void UpdateChase()
        {
            if (!AgentUsable)
            {
                bool moved = FallbackMoveTowards(_player.position, chaseSpeed);
                SetAnim(walk: false, run: moved);
                CheckKill();
                return;
            }

            SetAgentSpeed(chaseSpeed);
            SetMoving(true);
            SetAnim(walk: false, run: true);
            Repath(_player.position);
            CheckKill();
        }

        private void CheckKill()
        {
            if (_caught) return;
            if (Vector3.Distance(transform.position, _player.position) > killDistance) return;
            Caught();
        }

        private void Caught()
        {
            if (_caught) return;
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

        /// <summary>
        /// ย้ายตัวไปจุดหนึ่ง — ถ้ามี NavMesh ใกล้ๆ ให้ดึงเข้า NavMesh ก่อนเสมอ
        /// NavMeshAgent.Warp() กับ agent ที่สร้างไม่สำเร็จจะ error ("no valid NavMesh") ทุกครั้งที่เรียก
        /// จึงต้องเช็คก่อน แล้วถ้ายังไม่ได้ก็เขียน transform ตรงๆ ไปเลย
        /// </summary>
        private void Warp(Vector3 pos)
        {
            bool onMesh = NavMesh.SamplePosition(pos, out NavMeshHit hit, navMeshSnapRadius, NavMesh.AllAreas);
            if (onMesh) pos = hit.position;

            if (AgentUsable && onMesh)
            {
                _agent.Warp(pos);
                return;
            }

            // agent ใช้ไม่ได้ — ย้ายตัวด้วย transform ก่อน แล้วค่อยลองปลุก agent ที่จุดใหม่
            // (TryPlaceOnNavMesh ปิด agent ให้เองถ้าจุดนั้นยังไม่มี NavMesh — กัน log ซ้ำทุกเฟรม)
            if (_agent != null && _agent.enabled) _agent.enabled = false;
            transform.position = pos;

            if (!TryPlaceOnNavMesh(pos)) WarnNoNavMesh();
        }

        private void SetAgentSpeed(float speed)
        {
            if (AgentUsable) _agent.speed = speed;
        }

        /// <summary>
        /// เดินเข้าหาเป้าหมายด้วย Transform ตรงๆ ตอนที่ NavMeshAgent ใช้ไม่ได้
        /// เดินทะลุของได้ (ไม่มี pathfinding) แต่ดีกว่าผียืนแข็งอยู่ที่เดิมทั้งกฎ
        /// คืน true ถ้าเฟรมนี้ขยับจริง
        /// </summary>
        private bool FallbackMoveTowards(Vector3 target, float speed)
        {
            WarnNoNavMesh();
            if (!moveWithoutNavMesh) return false;

            // ระหว่างทางอาจเดินเข้าเขตที่มี NavMesh — ถ้าเข้าได้ก็กลับไปใช้ agent ตามปกติ
            if (TryPlaceOnNavMesh(transform.position)) return true;

            Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
            Vector3 next = Vector3.MoveTowards(transform.position, flatTarget, speed * Time.deltaTime);

            if (fallbackStickToGround &&
                Physics.Raycast(next + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f))
                next.y = ground.point.y;

            bool moved = (next - transform.position).sqrMagnitude > 0.000001f;
            transform.position = next;
            FaceTowards(target);
            return moved;
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

        /// <summary>
        /// ฝีเท้าตามระยะที่ขยับได้จริง — วัดจาก "ตำแหน่งที่เปลี่ยนไปจริง" ไม่ใช่ agent.velocity
        /// เพราะโหมด fallback (ไม่มี NavMesh) ไม่มี velocity ให้อ่าน แต่ตัวขยับอยู่
        /// </summary>
        private void UpdateFootsteps()
        {
            if (strideLength <= 0f) return;
            if (AgentUsable && _agent.isStopped) { _strideAccum = 0f; return; }

            float moved = Vector3.Distance(transform.position, _lastPosition);
            float speed = Time.deltaTime > 0f ? moved / Time.deltaTime : 0f;
            if (speed < footstepMinSpeed) { _strideAccum = 0f; return; }

            _strideAccum += moved;
            if (_strideAccum < strideLength) return;
            _strideAccum -= strideLength;
            AudioManager.instance.PlayGhostFootstep(transform.position);
        }

        #endregion
    }
}
