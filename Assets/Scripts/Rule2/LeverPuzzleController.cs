using System.Collections;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace Rule2
{
    /// <summary>
    /// ควบคุม mini-game ตู้ไฟ 2 Phase, คันโยก 4 ตัว
    ///
    /// Input:
    ///   คลิกซ้ายที่คันโยก active  → Grab (จับ)
    ///   คลิกซ้ายอีกครั้ง          → Release (ปล่อย)
    ///   เลื่อนเมาส์ขึ้น/ลง         → ปรับคันโยกขณะจับ
    ///   กด E ที่ปุ่มเขียว          → ConfirmCurrentLever()
    ///   กด E ที่ปุ่มแดง            → ResetPhase()
    ///
    /// Order Display:
    ///   orderObjects[i] = object แสดงเลขของ lever i (i=0→ก, 1→ข, 2→ค, 3→ง)
    ///   orderSlots[s]   = ตำแหน่ง s จากซ้ายไปขวา (s=0=ซ้ายสุด)
    ///   เมื่อสุ่มลำดับแล้ว orderObjects[_order[s]] จะถูก teleport ไปที่ orderSlots[s]
    /// </summary>
    public class LeverPuzzleController : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Levers (ต้องมีพอดี 4 ตัว ลำดับ 0=ก 1=ข 2=ค 3=ง)")]
        [SerializeField] private LeverHandle[] levers;

        [Header("Order Display Objects")]
        [Tooltip("4 objects แสดงเลขของแต่ละ lever\nindex 0=lever ก, 1=lever ข, 2=lever ค, 3=lever ง")]
        [SerializeField] private Transform[] orderObjects;   // 4 ชิ้น index=lever index

        [Tooltip("4 ตำแหน่ง slot จากซ้ายไปขวา (slot 0=ซ้ายสุด)\nใช้ empty GameObject วางตำแหน่งใน scene")]
        [SerializeField] private Transform[] orderSlots;     // 4 ชิ้น index=step (0=ทำก่อนสุด)

        [Header("Phase Indicator Lights")]
        [SerializeField] private Renderer phase1Dot;
        [SerializeField] private Renderer phase2Dot;
        [SerializeField] private Color    dotOffColor = new Color(0.85f, 0.85f, 0.85f);
        [SerializeField] private Color    dotOnColor  = Color.green;

        [Header("Range Randomisation")]
        [Tooltip("ขอบล่างต่ำสุดของ zone สีเขียว (0–1, แนะนำ 0.20)")]
        [SerializeField] private float minRangeStart = 0.20f;
        [Tooltip("ความกว้างต่ำสุดของ zone (0–1) — ค่าที่มากขึ้น = ง่ายขึ้น, แนะนำ 0.20–0.25")]
        [SerializeField] private float minZoneWidth  = 0.22f;
        [Tooltip("ความกว้างสูงสุดของ zone (0–1)")]
        [SerializeField] private float maxZoneWidth  = 0.32f;

        [Header("Lever Grab (Raycast)")]
        [Tooltip("Camera ที่ใช้ raycast — ถ้าเว้นว่างจะใช้ Camera.main")]
        [SerializeField] private Camera playerCamera;
        [Tooltip("ระยะ raycast สำหรับ grab คันโยก (เมตร)")]
        [SerializeField] private float  leverGrabDistance = 3f;

        [Header("Mouse Input")]
        [Tooltip("ความไวเมาส์ต่อ pixel (แนะนำ 0.001–0.003)")]
        [SerializeField] private float mouseSensitivity = 0.002f;

        [Header("Fail Flash")]
        [SerializeField] private float failFlashDuration = 0.8f;

        [Header("Game Over")]
        [Tooltip("จำนวนครั้งที่ตอบผิดสูงสุดก่อนตาย (default 3)")]
        [SerializeField] private int maxFails = 3;

        // ─────────────────────────── Properties ───────────────────────────

        public bool IsPuzzleRunning { get; private set; }

        // ─────────────────────────── Runtime ───────────────────────────

        private int[]                   _order;
        private int                     _step;
        private int                     _phase;
        private bool                    _busy;
        private bool                    _isGrabbed;
        private int                     _failCount;
        private RuleSystem.Rule.Rule2   _rule;
        private LightPanel              _panel;   // ตู้ไฟที่ controller นี้อยู่ด้วย

        // ─────────────────────────── Setup ───────────────────────────

        public void Initialize(RuleSystem.Rule.Rule2 rule, LightPanel panel)
        {
            _rule  = rule;
            _panel = panel;
        }

        // ─────────────────────────── Public API ───────────────────────────

        public void StartPuzzle()
        {
            if (IsPuzzleRunning || _busy) return;
            _failCount = 0;

            SetDotColor(phase1Dot, false);
            SetDotColor(phase2Dot, false);

            // ล็อกการเดิน — ขยับได้แค่หัน
            PlayerController.Instance.SetMovement(false);

            IsPuzzleRunning = true;
            BeginPhase(1);
        }

        public void ExitPuzzle()
        {
            if (!IsPuzzleRunning) return;
            ReleaseGrab();
            IsPuzzleRunning = false;
            StopAllDrift();
            PlayerController.Instance.SetMovement(true);
        }

        public void ConfirmCurrentLever()
        {
            if (!IsPuzzleRunning || _busy || _step >= 4) return;

            int idx = _order[_step];

            if (levers[idx].IsInRange)
            {
                ReleaseGrab();
                levers[idx].StopDrift();
                levers[idx].SetCompletedVisual();
                _step++;

                if (_step >= 4)
                    OnAllLeversConfirmed();
                else
                    ActivateNextLever();
            }
            else
            {
                StartCoroutine(FailRoutine());
            }
        }

        public void ResetPhase()
        {
            if (!IsPuzzleRunning || _busy) return;
            ReleaseGrab();
            StopAllDrift();
            foreach (var lev in levers) { lev.ResetToZero(); lev.ResetVisual(); lev.SetHighlight(false); }
            BeginPhase(_phase);
        }

        // ─────────────────────────── Crosshair (OnGUI) ───────────────────────────

        private enum AimState { None, CanGrab, Grabbed }

        private AimState GetCurrentAimState()
        {
            if (!IsPuzzleRunning || _busy || _step >= 4) return AimState.None;
            if (_isGrabbed)                              return AimState.Grabbed;
            if (IsLookingAtLever(_order[_step]))         return AimState.CanGrab;
            return AimState.None;
        }

        private void OnGUI()
        {
            if (!IsPuzzleRunning) return;

            float cx = Screen.width  * 0.5f;
            float cy = Screen.height * 0.5f;

            AimState aim = GetCurrentAimState();

            Color crossColor = aim switch
            {
                AimState.CanGrab => Color.yellow,
                AimState.Grabbed => new Color(1f, 0.55f, 0f),   // orange
                _                => new Color(1f, 1f, 1f, 0.55f)
            };

            // ── วาด + crosshair ──────────────────────────────────────
            Color prev = GUI.color;
            GUI.color = crossColor;
            GUI.DrawTexture(new Rect(cx - 9, cy - 1, 18, 2), Texture2D.whiteTexture); // แนวนอน
            GUI.DrawTexture(new Rect(cx - 1, cy - 9, 2, 18), Texture2D.whiteTexture); // แนวตั้ง

            // ── ข้อความ hint ─────────────────────────────────────────
            if (aim != AimState.None)
            {
                string hint = aim == AimState.CanGrab
                    ? "[ คลิกซ้าย: จับ ]"
                    : "[ คลิกซ้าย: ปล่อย ]";

                GUIStyle style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize  = 14,
                    fontStyle = FontStyle.Bold
                };
                style.normal.textColor = crossColor;

                // เงาข้อความ
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.Label(new Rect(cx - 99, cy + 14, 200, 28), hint, style);
                GUI.color = crossColor;
                GUI.Label(new Rect(cx - 100, cy + 15, 200, 28), hint, style);
            }

            GUI.color = prev;
        }

        // ─────────────────────────── Update — Grab + Mouse Input ───────────────────────────

        /// <summary>
        /// PuzzleButton เรียกก่อน Update เพื่อบอกว่า click ถูก consume ไปแล้ว
        /// (ป้องกัน confirm click กระตุ้น grab ในเฟรมเดียวกัน)
        /// </summary>
        public void ConsumeClick() => _clickConsumedThisFrame = true;

        private bool _clickConsumedThisFrame;

        private void Update()
        {
            // ล้าง flag ทุกเฟรม (ต้อง set ก่อน Update runs — PuzzleButton ควร call ก่อน Update)
            bool clickConsumed    = _clickConsumedThisFrame;
            _clickConsumedThisFrame = false;

            if (!IsPuzzleRunning || _busy || _step >= 4) return;
            if (Mouse.current == null) return;

            int idx = _order[_step];

            // ── Toggle Grab (ข้ามถ้า click ถูก consume โดย PuzzleButton แล้ว) ──
            if (!clickConsumed && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (!_isGrabbed)
                {
                    if (IsLookingAtLever(idx))
                    {
                        _isGrabbed = true;
                        levers[idx].Grab();
                    }
                }
                else
                {
                    // ปล่อย
                    _isGrabbed = false;
                    levers[idx].Release();
                    if (_phase == 2)
                        levers[idx].StartDrift();   // Phase 2: drift เริ่มหลังปล่อยทันที
                }
            }

            // ── Move Lever (เฉพาะขณะจับ) ────────────────────────────
            if (_isGrabbed)
            {
                float deltaY = Mouse.current.delta.ReadValue().y;
                if (Mathf.Abs(deltaY) > 0.5f)
                    levers[idx].SetValue(levers[idx].Value + deltaY * mouseSensitivity);
            }

            // ── Drive Heartbeat lerp ──────────────────────────────────
            AudioManager.instance?.UpdateHeartbeat();
        }

        // ─────────────────────────── Raycast ───────────────────────────

        private bool IsLookingAtLever(int leverIndex)
        {
            Camera cam = playerCamera != null ? playerCamera : Camera.main;
            if (cam == null) return false;
            if (levers[leverIndex].LeverCollider == null) return false;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            return Physics.Raycast(ray, out RaycastHit hit, leverGrabDistance)
                   && hit.collider == levers[leverIndex].LeverCollider;
        }

        // ─────────────────────────── Phase Logic ───────────────────────────

        void BeginPhase(int phase)
        {
            _phase     = phase;
            _step      = 0;
            _isGrabbed = false;

            // ── Fisher-Yates shuffle ──
            _order = new int[] { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }

            // ── สุ่ม range + reset lever ──
            for (int i = 0; i < levers.Length; i++)
            {
                float start = Random.Range(minRangeStart, 1f - minZoneWidth);
                float width = Random.Range(minZoneWidth, maxZoneWidth);
                levers[i].SetRange(start, Mathf.Min(start + width, 1f));
                levers[i].ResetToZero();
                levers[i].ResetVisual();
                levers[i].SetHighlight(false);
            }

            // ── จัด order objects ซ้ายไปขวา ──
            RepositionOrderObjects();

            ActivateNextLever();
        }

        void ActivateNextLever()
        {
            _isGrabbed = false;
            foreach (var lev in levers) lev.SetHighlight(false);

            if (_step < 4)
            {
                levers[_order[_step]].SetHighlight(true);

                // Phase 2: drift เริ่มทันทีก่อนผู้เล่นจะ grab
                if (_phase == 2)
                    levers[_order[_step]].StartDrift();
            }
        }

        void OnAllLeversConfirmed()
        {
            foreach (var lev in levers) lev.SetHighlight(false);

            if (_phase == 1)
            {
                SetDotColor(phase1Dot, true);
                BeginPhase(2);
            }
            else
            {
                SetDotColor(phase2Dot, true);
                CompletePuzzle();
            }
        }

        void CompletePuzzle()
        {
            IsPuzzleRunning = false;
            _isGrabbed      = false;
            StopAllDrift();
            HideAllOrderObjects();
            PlayerController.Instance.SetMovement(true);
            _rule?.OnPanelFixed(_panel);   // ส่ง panel ที่ซ่อมเสร็จไปตรงๆ
        }

        // ─────────────────────────── Fail Routine ───────────────────────────

        IEnumerator FailRoutine()
        {
            _busy           = true;
            _isGrabbed      = false;
            IsPuzzleRunning = false;
            StopAllDrift();

            _failCount++;
            _rule?.OnPuzzleFailed();

            foreach (var lev in levers) lev.SetFailVisual();
            yield return new WaitForSeconds(failFlashDuration);

            foreach (var lev in levers)
            {
                lev.ResetToZero();
                lev.ResetVisual();
                lev.SetHighlight(false);
            }
            HideAllOrderObjects();

            // ── Game Over (ผิดครบ maxFails ครั้ง) ──────────────────────
            if (_failCount >= maxFails)
            {
                _busy = false;
                PlayerController.Instance.SetMovement(true);
                _rule?.OnGameOver();
                yield break;   // หยุด — Rule2 จัดการ death sequence ต่อ
            }

            // ── ยังเหลือโอกาส — รีสตาร์ท ──────────────────────────────
            yield return new WaitForSeconds(0.2f);

            _busy           = false;
            IsPuzzleRunning = true;

            SetDotColor(phase1Dot, false);
            SetDotColor(phase2Dot, false);
            BeginPhase(1);
        }

        // ─────────────────────────── Order Objects ───────────────────────────

        /// <summary>
        /// เรียงลำดับ orderObjects ตามที่สุ่มได้:
        /// orderObjects[_order[s]] → ย้ายไปที่ orderSlots[s].position
        /// ซ้ายสุด = ขั้น 0 (ทำก่อนสุด)
        /// </summary>
        void RepositionOrderObjects()
        {
            if (orderObjects == null || orderSlots == null) return;

            // ซ่อนทั้งหมดก่อน แล้วเปิดใหม่ตาม slot
            foreach (var obj in orderObjects)
                if (obj != null) obj.gameObject.SetActive(false);

            for (int s = 0; s < 4 && s < orderSlots.Length && s < _order.Length; s++)
            {
                if (orderSlots[s] == null) continue;
                int leverIdx = _order[s];
                if (leverIdx >= orderObjects.Length || orderObjects[leverIdx] == null) continue;

                orderObjects[leverIdx].position = orderSlots[s].position;
                orderObjects[leverIdx].rotation = orderSlots[s].rotation;   // ตาม slot rotation ด้วย
                orderObjects[leverIdx].gameObject.SetActive(true);
            }
        }

        void HideAllOrderObjects()
        {
            if (orderObjects == null) return;
            foreach (var obj in orderObjects)
                if (obj != null) obj.gameObject.SetActive(false);
        }

        // ─────────────────────────── Helpers ───────────────────────────

        void ReleaseGrab()
        {
            if (!_isGrabbed) return;
            _isGrabbed = false;
            if (_step < 4 && _order != null)
                levers[_order[_step]].Release();
        }

        void StopAllDrift()
        {
            if (levers == null) return;
            foreach (var lev in levers) lev.StopDrift();
        }

        void SetDotColor(Renderer dot, bool on)
        {
            if (dot == null) return;
            dot.material.color = on ? dotOnColor : dotOffColor;
        }
    }
}
