using System;
using InputSystem;
using Player;
using UnityEngine;

namespace Rule5
{
    /// <summary>ทำไมมือถึงหลุดจากสาย — Rule5 ใช้ตัดสินว่าจะให้ผีไล่ / ให้ hint อะไร</summary>
    public enum ReleaseReason { Manual, Obstacle, EndOfThread, Forced }

    /// <summary>
    /// ระบบ "จับสายสิญจน์แล้วเดินตาม" ของ Rule 5
    ///
    ///   E ใกล้ผ้าแดง   → จับ: ล็อกตัวผู้เล่นไว้ข้างสาย เดินได้แค่ W (ตามทิศสาย) มองได้แค่ 180° ข้างหน้า
    ///   E ขณะจับ        → ปล่อย: ผ้าแดง "จุดปล่อย" ไปรอตรงนั้น จะกลับมาจับใหม่ต้องมาที่นี่เท่านั้น
    ///   ชนสิ่งกีดขวาง   → Detour: มือหลุดเอง ผ้าแดงไปรออีกฝั่ง / Untangle: หยุด กด E ค้างแก้ให้หลุด
    ///
    /// ตัวนี้ดูแลแค่การเคลื่อนที่ + สถานะจับ/ปล่อย — ระฆัง / ฉิ่ง / หัวใจ / ผี อยู่ที่ Rule5.cs
    /// </summary>
    public class SacredThreadWalker : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SacredThreadPath thread;
        [SerializeField] private InputHandler     inputHandler;

        [Tooltip("มือที่โผล่มาจับสาย (GameObject ที่ปิดไว้) — เปิดตอนจับ ปิดตอนปล่อย เว้นว่างได้")]
        [SerializeField] private GameObject holdHandVisual;

        [Header("Grab")]
        [Tooltip("ต้องอยู่ห่างผ้าแดงไม่เกินกี่เมตรถึงกด E จับได้")]
        [SerializeField] private float grabRadius = 2f;

        [Tooltip("ผู้เล่นยืนห่างจากเส้นไปทางขวาของทิศเดินกี่เมตร (ไม่ให้ยืนทับสาย)")]
        [SerializeField] private float playerSideOffset = 0.5f;

        [Tooltip("ตอนเพิ่งจับ ตัวจะถูกดูดเข้าหาตำแหน่งบนสายด้วยความเร็วนี้ (m/s) — ไม่วาร์ป")]
        [SerializeField] private float snapSpeed = 6f;

        [Header("Walk")]
        [Tooltip("ความเร็วเดินตามสาย (m/s)")]
        [SerializeField] private float walkSpeed = 1.6f;

        [Tooltip("มองซ้าย/ขวาได้ข้างละกี่องศาจากทิศสาย (90 = รวม 180°)")]
        [SerializeField] private float lookHalfAngle = 90f;

        [Header("Hints")]
        [SerializeField] private string grabHint        = "E   จับสายสิญจน์";
        [SerializeField] private string wrongSpotHint   = "ต้องกลับไปจับตรงผ้าแดงที่ปล่อยไว้";
        [SerializeField] private string untangleHint    = "สายพันกัน… กด E ค้างเพื่อแก้";
        [SerializeField] private string endOfThreadHint = "สุดสาย…";

        // ── Runtime ──
        private PlayerController _player;
        private ThreadObstacle[] _obstacles = Array.Empty<ThreadObstacle>();

        private bool  _active;
        private bool  _holding;
        private bool  _walking;
        private bool  _everGrabbed;
        private float _distance;          // ตำแหน่งผู้เล่นบนเส้น
        private float _regrabDistance;    // ผ้าแดงที่ต้องกลับมาจับ (หลังปล่อย)
        private bool  _hintShown;

        private ThreadObstacle _blockedBy;       // Untangle ที่ขวางอยู่ตอนนี้
        private ThreadObstacle _pendingDetour;   // Detour ที่เพิ่งทำให้มือหลุด — จับใหม่ได้แล้วค่อยนับว่าผ่าน
        private float          _untangleTimer;   // > 0 = กำลังกด E ค้างแก้อยู่
        private bool           _untangling;

        /// <summary>true = กำลังจับสายอยู่</summary>
        public bool IsHolding => _holding;

        /// <summary>true = จับอยู่และกำลังเดินอยู่ (เฟรมนี้ขยับ)</summary>
        public bool IsWalking => _holding && _walking;

        /// <summary>เคยจับสายแล้วอย่างน้อย 1 ครั้ง</summary>
        public bool EverGrabbed => _everGrabbed;

        /// <summary>ตำแหน่งผู้เล่นบนเส้น (เมตรจากจุดเริ่ม)</summary>
        public float Distance => _distance;

        public SacredThreadPath Thread => thread;

        public event Action                OnGrabbed;
        public event Action<ReleaseReason> OnReleased;

        // ═══════════════════════════════════════════════════════════════
        #region Begin / End
        // ═══════════════════════════════════════════════════════════════

        /// <summary>เปิดระบบ — เรียกตอน Rule 5 เริ่มเล่นจริง</summary>
        public void BeginRule()
        {
            _player = PlayerController.Instance;
            if (thread == null)
            {
                Debug.LogError("[Rule5] SacredThreadWalker ยังไม่ได้ใส่ thread", this);
                return;
            }

            _active         = true;
            _holding        = false;
            _walking        = false;
            _everGrabbed    = false;
            _distance       = 0f;
            _regrabDistance = 0f;
            _blockedBy      = null;
            _untangling     = false;
            _hintShown      = false;

            thread.ShowEndpointMarkers();
            thread.HideReleaseMarker();

            // รวมตัวที่ถูกปิดไปตอนรอบก่อน (clearAfterPass) ด้วย — Bind จะเปิดกลับให้
            _obstacles = FindObjectsByType<ThreadObstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var o in _obstacles) o.Bind(thread);

            if (inputHandler != null)
            {
                inputHandler.OnInteractPressed -= HandleInteract;
                inputHandler.OnInteractPressed += HandleInteract;
            }
            if (holdHandVisual != null) holdHandVisual.SetActive(false);
        }

        /// <summary>ปิดระบบ + คืนสภาพผู้เล่น — เรียกตอนจบกฎ / ตาย</summary>
        public void EndRuleCleanup()
        {
            if (inputHandler != null) inputHandler.OnInteractPressed -= HandleInteract;

            if (_holding) ForceRelease(silent: true);
            _active = false;

            if (thread != null) thread.ClearMarkers();
            if (holdHandVisual != null) holdHandVisual.SetActive(false);
            if (_player != null) _player.ClearYawLimit();
        }

        private void OnDisable() => EndRuleCleanup();

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Update
        // ═══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!_active || _player == null) return;

            if (!_holding)
            {
                UpdateGrabHint();
                return;
            }

            UpdateUntangle();
            UpdateWalk();
            UpdateLookLimit();
        }

        /// <summary>โชว์ "E จับสาย" ตอนเข้าใกล้ผ้าแดงที่จับได้</summary>
        private void UpdateGrabHint()
        {
            bool near = IsNearGrabPoint();
            if (near == _hintShown) return;
            _hintShown = near;

            if (PlayerDialogueUI.instance == null) return;
            if (near) PlayerDialogueUI.instance.ShowLine(grabHint, 3600f);
            else      PlayerDialogueUI.instance.Hide();
        }

        private void UpdateWalk()
        {
            float input = inputHandler != null ? inputHandler.GetMoveInput().y : 0f;
            bool  wantWalk = input > 0.1f && !_untangling;

            float step = wantWalk ? walkSpeed * Time.deltaTime : 0f;
            if (step > 0f) step = ClampStepByObstacles(step);

            _walking = step > 0.0001f;

            if (_walking)
            {
                float next = _distance + step;

                // สายไม่ loop → ถึงปลายแล้วมือหลุดเอง
                if (!thread.Loop && next >= thread.TotalLength)
                {
                    _distance = thread.TotalLength;
                    MovePlayerToward(TargetPosition(), 999f);
                    Release(ReleaseReason.EndOfThread);
                    if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.ShowLine(endOfThreadHint, 3f);
                    return;
                }

                _distance = thread.WrapDistance(next);
            }

            // ดูดตัวเข้าหาจุดบนสาย (ตอนเพิ่งจับจะค่อยๆ เข้าไป ตอนเดินอยู่แล้วก็ตามพอดี)
            MovePlayerToward(TargetPosition(), snapSpeed * Time.deltaTime + step);
        }

        private Vector3 TargetPosition()
            => thread.GetGroundPoint(_distance) + thread.GetRight(_distance) * playerSideOffset;

        private void MovePlayerToward(Vector3 target, float maxStep)
        {
            Vector3 delta = target - _player.transform.position;
            delta.y = 0f;
            if (delta.magnitude > maxStep) delta = delta.normalized * maxStep;
            _player.MoveExternal(delta, _walking);
        }

        /// <summary>ล็อกให้หันได้แค่ครึ่งวงหน้าตามทิศสายตรงจุดที่ยืน</summary>
        private void UpdateLookLimit()
        {
            Vector3 fwd = thread.GetForward(_distance);
            float   yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            _player.SetYawLimit(yaw, lookHalfAngle);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Obstacles
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// ก่อนก้าว: ถ้าก้าวนี้จะเข้าไปในช่วงที่สิ่งกีดขวางกั้น
        ///   Detour   → มือหลุดตรงขอบ ผ้าแดงไปรออีกฝั่ง (คืน 0 = ไม่ก้าว)
        ///   Untangle → หยุดตรงขอบ รอกด E ค้าง
        /// </summary>
        private float ClampStepByObstacles(float step)
        {
            foreach (var o in _obstacles)
            {
                if (o == null || !o.IsValid || o.Cleared) continue;

                // อยู่ในช่วงกั้นอยู่แล้ว (เช่น หยุดตรงขอบพอดี แล้ว float เลื่อนเข้าไปนิดเดียว) → ห้ามก้าว
                float blockLen = o.BlockEnd - o.BlockStart;
                float inside   = thread.ForwardGap(o.BlockStart, _distance);
                bool  isInside = inside >= -0.001f && inside <= blockLen + 0.001f;

                float gapToStart = isInside ? 0f : thread.ForwardGap(_distance, o.BlockStart);
                if (!isInside && (gapToStart < 0f || gapToStart > step)) continue;   // เลยไปแล้ว / ยังไม่ถึง

                if (o.Kind == ObstacleKind.Untangle)
                {
                    if (_blockedBy != o)   // เพิ่งเดินมาชน — โชว์ hint ครั้งเดียว ไม่ยิงซ้ำทุกเฟรม
                    {
                        _blockedBy = o;
                        if (PlayerDialogueUI.instance != null)
                            PlayerDialogueUI.instance.ShowLine(string.IsNullOrEmpty(o.Hint) ? untangleHint : o.Hint, 3600f);
                    }
                    return Mathf.Max(0f, gapToStart);
                }

                // Detour
                _distance       = thread.WrapDistance(_distance + gapToStart);
                _regrabDistance = thread.WrapDistance(o.RegrabPoint);
                _pendingDetour  = o;
                Release(ReleaseReason.Obstacle);
                if (PlayerDialogueUI.instance != null && !string.IsNullOrEmpty(o.Hint))
                    PlayerDialogueUI.instance.ShowLine(o.Hint, 4f);
                return 0f;
            }
            return step;
        }

        private void UpdateUntangle()
        {
            if (_blockedBy == null || !_untangling) return;

            // ปล่อย E กลางคัน → เริ่มนับใหม่ (ต้องกดค้างจนครบ)
            if (inputHandler != null && !inputHandler.IsInteractHeld())
            {
                _untangling = false;
                AudioManager.instance.StopRule5Untangle();
                return;
            }

            _untangleTimer -= Time.deltaTime;
            if (_untangleTimer > 0f) return;

            _untangling = false;
            _blockedBy.MarkPassed();
            _blockedBy = null;
            AudioManager.instance.StopRule5Untangle();
            AudioManager.instance.PlayRule5UntangleDone();
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        #region Grab / Release
        // ═══════════════════════════════════════════════════════════════

        private void HandleInteract()
        {
            if (!_active) return;

            if (_holding)
            {
                // ติดสายพันอยู่ → E = เริ่มแก้ ไม่ใช่ปล่อยมือ
                if (_blockedBy != null && !_untangling)
                {
                    _untangling    = true;
                    _untangleTimer = _blockedBy.UntangleDuration;
                    AudioManager.instance.StartRule5Untangle();
                    return;
                }
                if (_untangling) return;

                Release(ReleaseReason.Manual);
                return;
            }

            if (!IsNearGrabPoint())
            {
                // ยืนใกล้สายแต่ผิดจุด → บอกให้กลับไปที่ผ้าแดง
                thread.ClosestDistanceAlong(_player.transform.position, out float off);
                if (_everGrabbed && off <= grabRadius * 1.5f && PlayerDialogueUI.instance != null)
                    PlayerDialogueUI.instance.ShowLine(wrongSpotHint, 3f);
                return;
            }

            Grab(_everGrabbed ? _regrabDistance : 0f);
        }

        private bool IsNearGrabPoint()
        {
            float   d   = _everGrabbed ? _regrabDistance : 0f;
            Vector3 p   = thread.GetGroundPoint(d);
            Vector3 dif = _player.transform.position - p; dif.y = 0f;
            return dif.magnitude <= grabRadius;
        }

        private void Grab(float atDistance)
        {
            _holding     = true;
            _everGrabbed = true;
            _distance    = thread.WrapDistance(atDistance);
            _walking     = false;
            _hintShown   = false;

            // จับต่อหลังอ้อมสิ่งกีดขวางสำเร็จ → นับว่าผ่าน (เก็บออกถ้าตั้ง clearAfterPass)
            if (_pendingDetour != null)
            {
                if (_pendingDetour.ClearAfterPass) _pendingDetour.MarkPassed();
                _pendingDetour = null;
            }

            _player.SetMovement(false);
            thread.HideReleaseMarker();
            if (holdHandVisual != null) holdHandVisual.SetActive(true);
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            AudioManager.instance.PlayRule5GrabThread();
            OnGrabbed?.Invoke();
        }

        private void Release(ReleaseReason reason)
        {
            if (!_holding) return;

            if (_untangling) AudioManager.instance.StopRule5Untangle();
            _holding    = false;
            _walking    = false;
            _blockedBy  = null;
            _untangling = false;

            // ผ้าแดงจุดปล่อย: ปล่อยเอง = ตรงที่ยืน / ชนสิ่งกีดขวาง = อีกฝั่งของมัน (เซ็ตไว้แล้ว)
            if (reason != ReleaseReason.Obstacle) _regrabDistance = _distance;

            if (reason != ReleaseReason.Forced) thread.ShowReleaseMarker(_regrabDistance);

            _player.SetMovement(true);
            _player.ClearYawLimit();
            if (_player != null) _player.MoveExternal(Vector3.zero, false);   // ปิด anim เดิน
            if (holdHandVisual != null) holdHandVisual.SetActive(false);

            if (reason != ReleaseReason.Forced) AudioManager.instance.PlayRule5ReleaseThread();
            OnReleased?.Invoke(reason);
        }

        /// <summary>ปล่อยมือโดยระบบ (ตาย / จบกฎ) — ไม่วางผ้าแดง ไม่มีเสียง</summary>
        public void ForceRelease(bool silent)
        {
            Release(ReleaseReason.Forced);
            if (!silent && PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        #endregion
    }
}
