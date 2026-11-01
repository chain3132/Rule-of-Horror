using System.Collections;
using System.Collections.Generic;
using InputSystem;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Book
{
    /// <summary>
    /// พลิกหน้าหนังสือด้วย A / D — ใส่ที่ตัวหนังสือ (ตัวเดียวกับ Animator และ BookVideoController)
    ///
    ///   D = หน้าถัดไป (หนังสือปิดอยู่ = เปิดหนังสือ)
    ///   A = หน้าก่อนหน้า (อยู่หน้าแรก = ปิดหนังสือ ถ้าติ๊ก Can Close)
    ///
    /// รับปุ่มเฉพาะตอนผู้เล่นยืนใกล้และมองหนังสืออยู่ — A/D เป็นปุ่มเดินด้วย ถ้ารับตลอดเวลา
    /// เดินซ้ายขวาที่ไหนหนังสือก็พลิกตาม
    ///
    /// พอกด A/D ครั้งแรกถือว่า "เริ่มอ่าน": ล็อกการเดินไว้ (ยังหันได้) กดต่อจะได้ไม่เดินเฉไปทุกครั้งที่พลิก
    /// เลิกอ่าน = หันออกจากหนังสือ หรือกด W/S → เดินได้ตามปกติ
    ///
    /// เล่นแอนิเมชันเองโดยเลื่อนเวลาของ state ทีละเฟรม (Animator.speed = 0) ไม่ใช้ transition:
    ///   - พลิกย้อนหลังได้ด้วยคลิปเดียวกัน (เลื่อนเวลาจาก 1 → 0) ไม่ต้องทำคลิปถอยหลังเพิ่ม
    ///   - ไม่ต้องแก้ NewBookController — ใช้แค่ชื่อ state ที่มีอยู่แล้ว
    /// และบอก BookVideoController ตรงๆ ว่าพลิกเริ่ม/จบที่หน้าไหน (ปิด Follow Animator ของมันให้)
    /// </summary>
    [DisallowMultipleComponent]
    public class BookPageTurner : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("หนังสือ")]
        [Tooltip("Animator ของหนังสือ — เว้นว่าง = หาในตัวนี้/ลูกเอง")]
        [SerializeField] private Animator animator;

        [Tooltip("จำนวนหน้าที่พลิกได้ (TryNewBook มี Page1 - Page7)")]
        [SerializeField] private int pageCount = 7;

        [Tooltip("ชื่อ state เปิดหนังสือใน Animator Controller")]
        [SerializeField] private string openState = "Armature|OpenBook";

        [Tooltip("ชื่อ state พลิกหน้า — {0} = เลขหน้า")]
        [SerializeField] private string pageStateFormat = "Armature|Page{0}";

        [Tooltip("✓ = เริ่มเกมมาหนังสือเปิดเองเลย (เหมือนตอนที่ OpenBook เป็น default state)\n" +
                 "✗ = เริ่มแบบปิด ผู้เล่นกด D เพื่อเปิด")]
        [SerializeField] private bool openOnStart = true;

        [Tooltip("✓ = กด A ที่หน้าแรกแล้วปิดหนังสือได้")]
        [SerializeField] private bool canClose = true;

        [Tooltip("ความเร็วพลิกหน้า (1 = ตามความยาวคลิป)")]
        [SerializeField] private float turnSpeed = 1f;

        [Header("การอ่าน")]
        [SerializeField] private InputHandler inputHandler;

        [Tooltip("จุดที่ผู้เล่นต้องมอง — เว้นว่าง = กลางตัวหนังสือ")]
        [SerializeField] private Transform focusPoint;

        [Tooltip("ระยะจากกล้องถึงหนังสือที่ยังพลิกได้ (เมตร)")]
        [SerializeField] private float readDistance = 3f;

        [Tooltip("มุมสูงสุดระหว่างทิศที่มองกับหนังสือ (องศา)")]
        [SerializeField] private float lookAngle = 35f;

        [Tooltip("✓ = กด A/D แล้วล็อกการเดินจนกว่าจะหันออกหรือกด W/S")]
        [SerializeField] private bool lockMovementWhileReading = true;

        [Tooltip("ข้อความ hint ตอนมองหนังสืออยู่ (แปลผ่าน L10n) — เว้นว่าง = ไม่ขึ้น hint")]
        [SerializeField] private string hint = "A / D   Turn page";

        // ─────────────────────────── Runtime ───────────────────────────

        private const int   Closed         = -1;
        private const float PersistentHold = 3600f;

        private BookVideoController _video;
        private Renderer            _bookRenderer;
        private Camera              _cam;
        private Coroutine           _turnRoutine;
        private int                 _page = Closed;   // หน้าคู่ที่เปิดอยู่ (-1 = ปิด)
        private int                 _queued;          // กดระหว่างพลิก — จำไว้หนึ่งครั้ง (+1 / -1)
        private bool                _reading;
        private bool                _lockedMovement;
        private bool                _hintShown;

        private readonly Dictionary<string, float> _clipLengths = new Dictionary<string, float>();

        /// <summary>หน้าคู่ที่เปิดอยู่ (-1 = ปิด)</summary>
        public int CurrentPage => _page;

        public bool IsTurning => _turnRoutine != null;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (inputHandler == null) inputHandler = FindAnyObjectByType<InputHandler>(FindObjectsInactive.Include);

            _video = GetComponentInChildren<BookVideoController>(true);
            if (_video != null) _video.FollowAnimator = false;

            _bookRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (_bookRenderer == null) _bookRenderer = GetComponentInChildren<Renderer>(true);
        }

        private void Start()
        {
            if (animator == null)
            {
                Debug.LogWarning($"[Book] '{name}' ไม่มี Animator — พลิกหน้าไม่ได้", this);
                enabled = false;
                return;
            }

            // เลื่อนเวลาเอง — Animator ต้องไม่เดินเวลาเอง ไม่งั้น default state (OpenBook) จะเล่นซ้อน
            animator.speed = 0f;

            if (openOnStart) Step(+1);
            else             Pose(openState, 0f);
        }

        private void OnDisable()
        {
            if (_turnRoutine != null)
            {
                StopCoroutine(_turnRoutine);
                _turnRoutine = null;
            }
            _queued = 0;
            EndReading();
            HideHint();
        }

        private void Update()
        {
            if (!PlayerLookingAtBook(out string reason))
            {
                EndReading();
                HideHint();

#if UNITY_EDITOR
                // กด A/D แล้วไม่มีอะไรเกิดขึ้น ดูไม่ออกเลยว่าเพราะอะไร — บอกเหตุผลใน Console
                if (PrevPressed() || NextPressed())
                    Debug.Log($"[Book] A/D ยังพลิก '{name}' ไม่ได้: {reason}", this);
#endif
                return;
            }

            ShowHint();

            // อยากเดินต่อ (W/S) = เลิกอ่าน ปลดล็อกการเดินให้ทันที
            if (_reading && inputHandler != null && Mathf.Abs(inputHandler.GetMoveInput().y) > 0.5f)
            {
                EndReading();
                return;
            }

            bool prev = PrevPressed();
            bool next = NextPressed();
            if (!prev && !next) return;

            BeginReading();
            Step(next ? +1 : -1);
        }

        // ─────────────────────────── พลิก ───────────────────────────

        /// <summary>พลิกไปหน้าถัดไป (+1) หรือย้อนกลับ (-1)</summary>
        public void Step(int direction)
        {
            if (_turnRoutine != null)
            {
                _queued = direction > 0 ? +1 : -1;
                return;
            }

            if (direction > 0)
            {
                if (_page == Closed)        Turn(openState, forward: true, resultPage: 0);
                else if (_page < pageCount) Turn(PageState(_page + 1), forward: true, resultPage: _page + 1);
            }
            else
            {
                if (_page > 0)                   Turn(PageState(_page), forward: false, resultPage: _page - 1);
                else if (_page == 0 && canClose) Turn(openState, forward: false, resultPage: Closed);
            }
        }

        private void Turn(string state, bool forward, int resultPage)
        {
            if (!animator.HasState(0, Animator.StringToHash(state)))
            {
                Debug.LogWarning($"[Book] ไม่มี state '{state}' ใน Animator Controller ของ '{name}'", this);
                return;
            }

#if UNITY_EDITOR
            Debug.Log($"[Book] เล่น '{state}' {(forward ? "ไปข้างหน้า" : "ถอยหลัง")} → " +
                      (resultPage == Closed ? "ปิดหนังสือ" : $"หน้าคู่ {resultPage}"), this);
#endif
            _turnRoutine = StartCoroutine(TurnRoutine(state, forward, resultPage));
        }

        private IEnumerator TurnRoutine(string state, bool forward, int resultPage)
        {
            if (_video != null) _video.BeginTurn();

            float length = Mathf.Max(0.05f, ClipLength(state));
            float t = 0f;

            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime * Mathf.Max(0.01f, turnSpeed) / length);
                Pose(state, forward ? t : 1f - t);
                yield return null;
            }

            _page = resultPage;
            if (_video != null)
            {
                if (_page == Closed) _video.Close();
                else                 _video.ShowSpread(_page);
            }

            _turnRoutine = null;

            // กดค้างไว้ระหว่างพลิก — พลิกต่อทันที ไม่ต้องรอผู้เล่นกดใหม่
            if (_queued != 0)
            {
                int queued = _queued;
                _queued = 0;
                Step(queued);
            }
        }

        private void Pose(string state, float normalizedTime) =>
            animator.Play(state, 0, Mathf.Clamp01(normalizedTime));

        private string PageState(int page) => string.Format(pageStateFormat, page);

        /// <summary>ความยาวคลิปของ state — ชื่อ state กับชื่อคลิปจาก FBX ตรงกัน ("Armature|Page3")</summary>
        private float ClipLength(string state)
        {
            if (_clipLengths.TryGetValue(state, out float length)) return length;

            length = 1f;
            var controller = animator.runtimeAnimatorController;
            if (controller != null)
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip != null && clip.name == state)
                    {
                        length = clip.length;
                        break;
                    }
                }
            }

            _clipLengths[state] = length;
            return length;
        }

        // ─────────────────────────── อ่าน ───────────────────────────

        /// <summary>
        /// ผู้เล่นอยู่ใกล้และมองหนังสืออยู่ไหม
        ///
        /// ผู้เล่นหันไม่ได้ (เปิดโทรศัพท์ / คัทซีน / ฉากตาย) = ไม่นับ — A/D ตอนนั้นเป็นของระบบอื่น
        /// (แอปกฎในโทรศัพท์ก็ใช้ A/D พลิกกฎ)
        /// </summary>
        private bool PlayerLookingAtBook(out string reason)
        {
            var pc = PlayerController.Instance;
            if (pc == null) { reason = "ไม่เจอ PlayerController"; return false; }
            if (!pc.CanLook) { reason = "ผู้เล่นถูกล็อกการหันอยู่ (โทรศัพท์ / คัทซีน / ระบบอื่น)"; return false; }

            Camera cam = PlayerCamera(pc);
            if (cam == null) { reason = "ไม่เจอกล้องของผู้เล่น"; return false; }

            Vector3 eye     = cam.transform.position;
            Vector3 forward = cam.transform.forward;

            // วัดระยะถึง "ขอบ" ของหนังสือ ไม่ใช่จุดกลาง — หนังสือที่กางอยู่กว้างเกือบเมตร
            // ยืนชิดขอบโต๊ะแต่จุดกลางยังห่างเกินระยะได้
            if (focusPoint == null && _bookRenderer != null)
            {
                Bounds bounds = _bookRenderer.bounds;
                float distance = Mathf.Sqrt(bounds.SqrDistance(eye));
                if (distance > readDistance)
                {
                    reason = $"อยู่ไกลเกินไป ({distance:0.0} ม. > Read Distance {readDistance:0.0})";
                    return false;
                }

                // เป้ากลางจอชี้โดนกล่องของหนังสือ = มองอยู่ ไม่ต้องเล็งจุดกลางเป๊ะ
                Bounds padded = bounds;
                padded.Expand(0.2f);
                if (padded.IntersectRay(new Ray(eye, forward))) { reason = null; return true; }

                float angle = Vector3.Angle(forward, bounds.center - eye);
                if (angle < lookAngle) { reason = null; return true; }

                reason = $"ไม่ได้มองหนังสือ (หันห่าง {angle:0}° > Look Angle {lookAngle:0})";
                return false;
            }

            Vector3 target = focusPoint != null ? focusPoint.position : transform.position;
            Vector3 toBook = target - eye;

            if (toBook.magnitude > readDistance)
            {
                reason = $"อยู่ไกลเกินไป ({toBook.magnitude:0.0} ม. > Read Distance {readDistance:0.0})";
                return false;
            }

            float a = Vector3.Angle(forward, toBook);
            if (a >= lookAngle)
            {
                reason = $"ไม่ได้มองหนังสือ (หันห่าง {a:0}° > Look Angle {lookAngle:0})";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// กล้องที่ผู้เล่นมองอยู่จริง
        ///
        /// ไม่ใช้ Camera.main อย่างเดียว — ฉากนี้มีกล้องหลายตัว (คัทซีน, โทรศัพท์ที่เรนเดอร์ลง texture)
        /// ถ้ากล้องผู้เล่นไม่ได้ติด tag MainCamera ค่า Camera.main จะเป็น null หรือเป็นกล้องตัวอื่น
        /// </summary>
        private Camera PlayerCamera(PlayerController pc)
        {
            if (_cam != null && _cam.isActiveAndEnabled) return _cam;

            _cam = null;
            foreach (var cam in pc.GetComponentsInChildren<Camera>())
            {
                if (cam.isActiveAndEnabled && cam.targetTexture == null) { _cam = cam; break; }
            }

            if (_cam == null) _cam = Camera.main;
            return _cam;
        }

        private void BeginReading()
        {
            _reading = true;
            if (!lockMovementWhileReading || _lockedMovement) return;

            // ล็อกเฉพาะตอนเดินได้อยู่ — ถ้ามีระบบอื่นล็อกไว้ก่อน ห้ามไปปลดแทนเจ้าของตอนเลิกอ่าน
            var pc = PlayerController.Instance;
            if (pc != null && pc.CanMove)
            {
                pc.SetMovement(false);
                _lockedMovement = true;
            }
        }

        private void EndReading()
        {
            _reading = false;
            if (!_lockedMovement) return;

            _lockedMovement = false;
            var pc = PlayerController.Instance;
            if (pc != null) pc.SetMovement(true);
        }

        private bool PrevPressed()
        {
            if (inputHandler != null) return inputHandler.WasRulePrevPressed();
            return Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame;
        }

        private bool NextPressed()
        {
            if (inputHandler != null) return inputHandler.WasRuleNextPressed();
            return Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame;
        }

        private void ShowHint()
        {
            if (_hintShown || string.IsNullOrWhiteSpace(hint) || PlayerDialogueUI.instance == null) return;
            PlayerDialogueUI.instance.ShowLine(hint, PersistentHold);
            _hintShown = true;
        }

        private void HideHint()
        {
            if (!_hintShown) return;
            _hintShown = false;
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }
    }
}
