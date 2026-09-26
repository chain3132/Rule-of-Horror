using System;
using System.Collections.Generic;
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
    ///   ชนสิ่งกีดขวาง   → หยุดตรงขอบทั้งคู่ มือไม่หลุดเอง ผู้เล่นเป็นคนตัดสินใจ:
    ///                      Detour: กด E ปล่อยมือ ผ้าแดงไปรออีกฝั่ง / Untangle: กด E ค้างแก้ให้หลุด
    ///
    /// ตัวนี้ดูแลแค่การเคลื่อนที่ + สถานะจับ/ปล่อย — ระฆัง / ฉิ่ง / หัวใจ / ผี อยู่ที่ Rule5.cs
    /// </summary>
    public class SacredThreadWalker : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SacredThreadPath thread;
        [SerializeField] private InputHandler     inputHandler;

        [Tooltip("Hand visual shown while holding the thread (a GameObject left disabled). Enabled on grab, disabled on release. Optional.")]
        [SerializeField] private GameObject holdHandVisual;

        [Header("Grab")]
        [Tooltip("Maximum distance from the red cloth, in metres, at which E can grab the thread.")]
        [SerializeField] private float grabRadius = 2f;

        [Tooltip("How far the player stands to the right of the thread, in metres, so they do not stand on top of it.")]
        [SerializeField] private float playerSideOffset = 0.5f;

        [Tooltip("Speed (m/s) the player is pulled to their spot on the thread after grabbing. Not a teleport.")]
        [SerializeField] private float snapSpeed = 6f;

        [Header("Walk")]
        [Tooltip("Walking speed along the thread (m/s).")]
        [SerializeField] private float walkSpeed = 1.6f;

        [Tooltip("Look limit to each side of the thread direction, in degrees. 90 would be a flat 180 degrees of vision; " +
                 "this is deliberately wider so the player can crane round far enough to catch the ghost walking behind them. " +
                 "Raise it to make the ghost easier to see, lower it to hide the ghost completely.")]
        [Range(45f, 180f)]
        [SerializeField] private float lookHalfAngle = 130f;

        [Header("Hints")]
        [SerializeField] private string grabHint        = "E   จับสายสิญจน์";
        [SerializeField] private string wrongSpotHint   = "ต้องกลับไปจับตรงผ้าแดงที่ปล่อยไว้";
        [SerializeField] private string untangleHint    = "สายพันกัน… กด E ค้างเพื่อแก้";
        [SerializeField] private string detourHint      = "มีอะไรขวางอยู่… กด E ปล่อยมือแล้วเดินอ้อม";
        [SerializeField] private string endOfThreadHint = "สุดสาย…";

        // ── Runtime ──
        private PlayerController _player;

        // สิ่งกีดขวางที่ "โผล่มาแล้ว" เท่านั้น — ThreadObstacleSpawner ทยอยเติมเข้ามาระหว่างเล่น
        private readonly List<ThreadObstacle> _obstacles = new List<ThreadObstacle>();

        private bool  _active;
        private bool  _holding;
        private bool  _walking;
        private bool  _everGrabbed;
        private float _distance;          // ตำแหน่งผู้เล่นบนเส้น
        private float _regrabDistance;    // ผ้าแดงที่ต้องกลับมาจับ (หลังปล่อย)
        private bool  _hintShown;
        private bool  _grabHintEnabled = true;

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

        /// <summary>ระยะบนเส้นของ "จุดที่ต้องไปกด E จับ" ตอนนี้ (ต้นสาย หรือผ้าแดงที่ปล่อยไว้)</summary>
        public float GrabPointDistance => _everGrabbed ? _regrabDistance : 0f;

        public SacredThreadPath Thread => thread;

        /// <summary>true = เฟรมนี้ผู้เล่นเพิ่งกดปุ่มสวดมนต์ (Space) — Rule5 เป็นคนตัดสินว่าจะให้มีผลไหม</summary>
        public bool PrayPressedThisFrame => inputHandler != null && inputHandler.WasPrayPressed();

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
            _untangling      = false;
            _hintShown       = false;
            _grabHintEnabled = true;

            thread.ShowEndpointMarkers();

            // ไม่ไปกวาดหา ThreadObstacle เองแล้ว — ThreadObstacleSpawner เป็นคนตัดสินว่าตัวไหนโผล่เมื่อไร
            // (ตัวที่ผู้ทำฉากอยาก "มีตั้งแต่แรก" ให้ใส่ใน spawner ช่อง revealAtStart)
            _obstacles.Clear();

            if (inputHandler != null)
            {
                inputHandler.OnInteractPressed -= HandleInteract;
                inputHandler.OnInteractPressed += HandleInteract;
            }
            else Debug.LogError("[Rule5] SacredThreadWalker ยังไม่ได้ใส่ inputHandler — " +
                                "จับสาย (E) / เดิน (W) / สวดมนต์ (Space) จะไม่ทำงานเลย", this);
            if (holdHandVisual != null) holdHandVisual.SetActive(false);
        }

        /// <summary>
        /// เพิ่มสิ่งกีดขวางที่เพิ่งโผล่เข้าสู่ระบบกั้นทาง — ThreadObstacleSpawner เรียก
        /// (ถ้ายังไม่ได้ Bind ให้ Bind ที่นี่ จะได้ไม่มีตัวที่ IsValid = false หลุดเข้ามา)
        /// </summary>
        public void RegisterObstacle(ThreadObstacle obstacle)
        {
            if (obstacle == null || thread == null || _obstacles.Contains(obstacle)) return;
            if (!obstacle.IsValid && !obstacle.Bind(thread)) return;
            _obstacles.Add(obstacle);
        }

        /// <summary>จำนวนสิ่งกีดขวางที่ยังกั้นทางอยู่ (debug HUD)</summary>
        public int BlockingObstacleCount
        {
            get
            {
                int n = 0;
                foreach (var o in _obstacles) if (o != null && o.IsBlocking) n++;
                return n;
            }
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
            if (!_grabHintEnabled) return;

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

            // ผ้าแดงเลื่อนตามมือไปด้วย — ผู้เล่นที่ถูกปิดตาจะได้รู้ว่าเดินมาถึงไหนแล้วตอนแอบมอง
            thread.SetGripMarker(_distance, releaseLook: false);
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

        /// <summary>
        /// ล็อกทิศที่หันได้ให้อยู่ในช่วง ±lookHalfAngle รอบทิศของสายตรงจุดที่ยืน
        /// กว้างกว่าครึ่งวงหน้านิดหน่อย — พอให้ชะเง้อกลับไปเห็นผีที่เดินตามหลังได้แวบหนึ่ง แต่ยังหันหลังกลับไม่ได้
        /// </summary>
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
        /// ก่อนก้าว: ถ้าก้าวนี้จะเข้าไปในช่วงที่สิ่งกีดขวางกั้น → หยุดตรงขอบ (ทั้ง 2 ชนิด)
        ///
        /// มือไม่หลุดเอง เพราะการโดนกระชากมือหลุดโดยไม่ได้สั่งเองมันงงและรู้สึกเหมือนบั๊ก
        /// จากตรงนี้ผู้เล่นเป็นคนกด E เอง — HandleInteract ดูชนิดของตัวที่ขวางแล้วแยกทางให้:
        ///   Detour   → ปล่อยมือ ผ้าแดงไปรออีกฝั่ง เดินอ้อมเอา
        ///   Untangle → กดค้างแก้สายพัน ไม่ต้องปล่อยมือ
        /// </summary>
        private float ClampStepByObstacles(float step)
        {
            foreach (var o in _obstacles)
            {
                if (o == null || !o.IsBlocking) continue;

                // อยู่ในช่วงกั้นอยู่แล้ว (เช่น หยุดตรงขอบพอดี แล้ว float เลื่อนเข้าไปนิดเดียว) → ห้ามก้าว
                float blockLen = o.BlockEnd - o.BlockStart;
                float inside   = thread.ForwardGap(o.BlockStart, _distance);
                bool  isInside = inside >= -0.001f && inside <= blockLen + 0.001f;

                float gapToStart = isInside ? 0f : thread.ForwardGap(_distance, o.BlockStart);
                if (!isInside && (gapToStart < 0f || gapToStart > step)) continue;   // เลยไปแล้ว / ยังไม่ถึง

                if (_blockedBy != o)   // เพิ่งเดินมาชน — โชว์ hint ครั้งเดียว ไม่ยิงซ้ำทุกเฟรม
                {
                    _blockedBy = o;

                    string hint = !string.IsNullOrEmpty(o.Hint) ? o.Hint
                                : o.Kind == ObstacleKind.Untangle ? untangleHint
                                : detourHint;
                    if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.ShowLine(hint, 3600f);
                }
                return Mathf.Max(0f, gapToStart);
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
                if (_untangling) return;

                if (_blockedBy != null)
                {
                    // ติดสายพันอยู่ → E = เริ่มแก้ ไม่ใช่ปล่อยมือ
                    if (_blockedBy.Kind == ObstacleKind.Untangle)
                    {
                        _untangling    = true;
                        _untangleTimer = _blockedBy.UntangleDuration;
                        AudioManager.instance.StartRule5Untangle();
                        return;
                    }

                    // ติดของที่ต้องเดินอ้อม → E = ปล่อยมือตรงนี้ ผ้าแดงไปรออีกฝั่ง
                    ReleaseForDetour(_blockedBy);
                    return;
                }

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
            Vector3 p   = thread.GetGroundPoint(GrabPointDistance);
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
            thread.SetGripMarker(_distance, releaseLook: false);
            if (holdHandVisual != null) holdHandVisual.SetActive(true);
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            AudioManager.instance.PlayRule5GrabThread();
            OnGrabbed?.Invoke();
        }

        /// <summary>ปล่อยมือเพราะต้องเดินอ้อมสิ่งกีดขวาง — ผ้าแดงจุดจับใหม่ไปรออีกฝั่งของมัน</summary>
        private void ReleaseForDetour(ThreadObstacle o)
        {
            _regrabDistance = thread.WrapDistance(o.RegrabPoint);
            _pendingDetour  = o;
            Release(ReleaseReason.Obstacle);
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
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

            // ปล่อยมือ → ผ้าแดงไปรอที่จุดที่ต้องกลับมาจับ
            // ชนสิ่งกีดขวาง = อีกฝั่งของมัน ผ้าแดงจะไถลตามเส้นข้ามไปรอให้เห็นว่าต้องไปต่อตรงไหน
            if (reason != ReleaseReason.Forced) thread.SetGripMarker(_regrabDistance, releaseLook: true);

            _player.SetMovement(true);
            _player.ClearYawLimit();
            if (_player != null) _player.MoveExternal(Vector3.zero, false);   // ปิด anim เดิน
            if (holdHandVisual != null) holdHandVisual.SetActive(false);

            if (reason != ReleaseReason.Forced) AudioManager.instance.PlayRule5ReleaseThread();
            OnReleased?.Invoke(reason);
        }

        /// <summary>
        /// เปิด/ปิดข้อความ "E จับสายสิญจน์" — Rule5 ปิดหลังผู้เล่นสวดมนต์ลา
        /// (ตอนนั้นเป้าหมายคือวิ่งกลับไปนั่ง ไม่ใช่กลับมาจับสายอีก ข้อความจะค้างเกะกะเปล่าๆ)
        /// </summary>
        public void SetGrabHintEnabled(bool on)
        {
            _grabHintEnabled = on;
            if (on || !_hintShown) return;

            _hintShown = false;
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        /// <summary>
        /// ปล่อยมือแทนการกด E — Rule5 เรียกตอนผู้เล่นสวดมนต์ลา จะได้ไม่ต้องกด E ซ้ำอีกที
        /// ถ้ากำลังติดสิ่งกีดขวางแบบต้องอ้อมอยู่ ก็ปล่อยแบบ Detour ให้ ผ้าแดงจะได้ไปรอถูกฝั่ง
        /// </summary>
        public void ReleaseByPlayer()
        {
            if (!_holding) return;

            if (_blockedBy != null && _blockedBy.Kind == ObstacleKind.Detour) { ReleaseForDetour(_blockedBy); return; }
            Release(ReleaseReason.Manual);
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
