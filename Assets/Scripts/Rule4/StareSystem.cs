using InputSystem;
using Player;
using UnityEngine;

namespace Rule4
{
    /// <summary>
    /// ระบบจ้องผีของ Rule 4 (กดคลิกขวาค้าง)
    ///
    /// - ต้องมีผีอยู่ และผีต้องอยู่ในมุมมองถึงจะเริ่มจ้องได้
    /// - ระหว่างจ้อง: กล้อง zoom ไปที่ผีเพื่อยืนยันว่ากำลังมอง, ตัด input การเดิน,
    ///   ผีเดินช้าลง (หยุดไม่ได้ แค่ถ่วงเวลา)
    /// - จ้องได้นานเท่าไรก็ได้ ไม่มีโทษ — ราคาที่จ่ายคือขยับตัวไม่ได้ระหว่างนั้น
    /// - จ้องแล้วต้องค้างอย่างน้อย minStareDuration และเลิกแล้วต้องรอ restareCooldown ถึงจ้องใหม่ได้
    ///   ไม่งั้นแตะคลิกขวารัวๆ ผีจะเบรกหยุดทุกครั้ง (แทบไม่ขยับ) แต่ผู้เล่นได้เดินระหว่างแตะ
    /// </summary>
    public class StareSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputHandler inputHandler;

        [Header("Aim")]
        [Tooltip("มุมสูงสุดระหว่างทิศที่กล้องมองกับผี ถึงจะเริ่มจ้องได้ (องศา)")]
        [SerializeField] private float aimAngle = 35f;

        [Header("กันคลิกรัว")]
        [Tooltip("จ้องแล้วต้องค้างอย่างน้อยเท่านี้ (วินาที) ถึงจะเลิกได้ — ปล่อยคลิกเร็วกว่านี้ก็ยังจ้องต่อจนครบ\n" +
                 "กันการแตะคลิกขวารัวๆ: ทุกครั้งที่เริ่มจ้อง ผีเบรกจนหยุดทันที ถ้าแตะแล้วปล่อยได้ทันที\n" +
                 "ผีจะแทบไม่ได้ขยับเลย ขณะที่ผู้เล่นได้เดินระหว่างแตะแต่ละครั้ง")]
        [SerializeField] private float minStareDuration = 0.6f;

        [Tooltip("เลิกจ้องแล้วต้องรอเท่านี้ (วินาที) ถึงจะจ้องใหม่ได้ — ช่วงนี้ผีเดินตามปกติ")]
        [SerializeField] private float restareCooldown = 1.0f;

        // ── Runtime ──
        private Rule4Ghost _ghost;
        private Camera     _cam;
        private bool       _ruleActive;
        private bool       _staring;
        private float      _stareStartTime;
        private float      _nextStareTime;

        /// <summary>true = กำลังจ้องผีอยู่</summary>
        public bool IsStaring => _staring;

        // ─────────────────────────── Lifecycle ───────────────────────────

        public void BeginRule()
        {
            _ruleActive = true;
            _cam        = Camera.main;
        }

        /// <summary>ผูกผีที่เพิ่ง spawn — จ้องได้เฉพาะตัวนี้</summary>
        public void SetGhost(Rule4Ghost ghost) => _ghost = ghost;

        /// <summary>ปิดระบบ + คืนสถานะกล้อง/การขยับ (จบกฎ / ตาย)</summary>
        public void EndRuleCleanup()
        {
            _ruleActive = false;
            if (_staring) StopStaring();
            _ghost = null;
        }

        private void OnDisable()
        {
            if (_staring) StopStaring();
        }

        // ─────────────────────────── Update ───────────────────────────

        private void Update()
        {
            if (!_ruleActive) return;

            bool wantStare = inputHandler != null && inputHandler.IsRightClickHeld();

            if (_staring)
            {
                // ผีหายไประหว่างจ้อง (ถูกซ่อน/จบกฎ/ตาย) → เลิกจ้องทันที
                if (_ghost == null || !_ghost.gameObject.activeInHierarchy) StopStaring();
                // ปล่อยคลิกขวา → เลิกจ้อง แต่ต้องจ้องครบเวลาขั้นต่ำก่อน (กันแตะรัวแล้วผีไม่ได้ขยับ)
                else if (!wantStare && Time.time - _stareStartTime >= minStareDuration) StopStaring();
            }
            else if (wantStare && Time.time >= _nextStareTime && CanStartStaring())
            {
                StartStaring();
            }
        }

        private bool CanStartStaring()
        {
            // ช่วงป้ายบอกตำแหน่ง Rule4 จะ SetActive(false) ผีไว้ ซึ่งไม่เท่ากับ null
            // ถ้าไม่กันตรงนี้ ผู้เล่นจะจ้อง "ผีที่มองไม่เห็น" แล้วโดนล็อกตัวค้าง
            if (_ghost == null || !_ghost.gameObject.activeInHierarchy) return false;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return false;

            Vector3 toGhost = _ghost.transform.position - _cam.transform.position;
            return Vector3.Angle(_cam.transform.forward, toGhost.normalized) < aimAngle;
        }

        // ─────────────────────────── Stare on / off ───────────────────────────

        private void StartStaring()
        {
            _staring        = true;
            _stareStartTime = Time.time;

            if (PlayerController.Instance != null) PlayerController.Instance.SetMovement(false);

            // StartFocus จัดการ SetLook(false) + หมุนกล้องเข้าหาผี + zoom ให้แล้ว
            if (LookDistortionSystem.Instance != null)
                LookDistortionSystem.Instance.StartFocus(_ghost.transform);

            _ghost.IsBeingStared = true;
        }

        private void StopStaring()
        {
            _staring       = false;
            _nextStareTime = Time.time + restareCooldown;

            if (_ghost != null) _ghost.IsBeingStared = false;

            if (LookDistortionSystem.Instance != null)
            {
                // ซิงก์มุมก่อนปลด SetLook ไม่งั้นกล้องจะดีดกลับมุมก่อนเริ่มจ้อง
                if (PlayerController.Instance != null) PlayerController.Instance.SyncLookToCameraDirection();
                LookDistortionSystem.Instance.StopFocus();
            }

            if (PlayerController.Instance != null) PlayerController.Instance.SetMovement(true);
        }
    }
}
