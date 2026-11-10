using System.Collections;
using System.Collections.Generic;
using InputSystem;
using Player;
using RuleSystem;
using UnityEngine;

namespace Book
{
    /// <summary>
    /// สมุดโน้ต — ปลดล็อก / หยิบมาอ่าน / พลิกหน้า / วางคืน ใส่ที่ตัวหนังสือ (ตัวเดียวกับ Animator และ BookVideoController)
    ///
    /// ปลดล็อก: โน้ตเลข N เปิดอ่านได้หลังผ่านกฎข้อ N (ตาย/ย้อนเวลาไม่นับ)
    ///   พอผ่านกฎแล้วกลับมาโหมด Relax (ลืมตา) สมุดบนโต๊ะเปิดไปหน้าโน้ตที่เพิ่งได้เองทันที แล้ววิดีโอเล่นเลย
    ///
    /// หยิบ: เดินไปใกล้ มองสมุด แล้วกด E — สมุดลอยเข้ามาตรงหน้า โน้ตที่เล่นจบแล้วค้างอยู่ ไม่เล่นใหม่
    ///   ระหว่างถือ: เดินไม่ได้ หันไม่ได้ · A / D พลิกได้เฉพาะหน้าที่ปลดล็อกแล้ว · E วางคืน
    ///   วางคืน: สมุดกลับไปที่เดิม ผู้เล่นขยับได้ แล้วสมุดค่อยปิดเอง
    ///
    /// ยังไม่ปลดล็อกอะไรเลย = กด E ไม่ได้ (ไม่มี hint) — สมุดเปล่าไม่มีอะไรให้อ่าน
    /// A / D ไม่มีผลถ้าไม่ได้ถือสมุดอยู่ (เดิมแค่ยืนใกล้ก็พลิกได้ ซึ่งชนกับปุ่มเดิน)
    ///
    /// เล่นแอนิเมชันเองโดยเลื่อนเวลาของ state ทีละเฟรม (Animator.speed = 0) ไม่ใช้ transition:
    ///   - พลิกย้อนหลังได้ด้วยคลิปเดียวกัน (เลื่อนเวลาจาก 1 → 0)
    ///   - สมุดอยู่ในท่าปิดตั้งแต่เริ่ม ไม่มีทางเปิดเองก่อนผู้เล่นได้โน้ต
    /// และบอก BookVideoController ตรงๆ ว่าพลิกเริ่ม/จบที่หน้าไหน (ปิด Follow Animator ของมันให้)
    /// </summary>
    [DisallowMultipleComponent]
    public class BookPageTurner : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("หนังสือ")]
        [Tooltip("Animator ของหนังสือ — เว้นว่าง = หาในตัวนี้/ลูกเอง")]
        [SerializeField] private Animator animator;

        [Tooltip("จำนวนหน้าที่พลิกได้ — TryNewBook พลิกได้ 6 หน้า (กระดูก Page1-Page6 มีคลิป, Page7 ไม่มี)")]
        [SerializeField] private int pageCount = 6;

        [Tooltip("ชื่อ state เปิดหนังสือใน Animator Controller")]
        [SerializeField] private string openState = "Armature|OpenBook";

        [Tooltip("ชื่อ state พลิกหน้า — {0} = เลขคลิป")]
        [SerializeField] private string pageStateFormat = "Armature|Page{0}";

        [Tooltip("เลขคลิปมากกว่าเลขกระดูกหน้าที่มันพลิกเท่านี้\n\n" +
                 "TryNewBook: คลิป Page1 ว่างเปล่า (OpenBook เปิดมาถึงหน้าแรกแล้ว) และคลิป PageN\n" +
                 "พลิกกระดูก Page(N-1) — พลิกหน้า 1 จึงต้องเล่นคลิป Page2 = ต่างกัน 1\n" +
                 "ถ้าแก้ชื่อคลิปใน Blender ให้ตรงกับกระดูกแล้ว ตั้งเป็น 0")]
        [SerializeField] private int clipNumberOffset = 1;

        [Tooltip("ความเร็วพลิกหน้า (1 = ตามความยาวคลิป)")]
        [SerializeField] private float turnSpeed = 1f;

        [Tooltip("ความเร็วตอนปิดสมุดหลังวางคืน — เร็วกว่าตอนพลิกอ่าน ปิดทีละหน้าช้าๆ น่าเบื่อ")]
        [SerializeField] private float closeSpeed = 2.5f;

        [Header("หยิบมาอ่าน")]
        [SerializeField] private InputHandler inputHandler;

        [Tooltip("จุดที่ผู้เล่นต้องมอง — เว้นว่าง = ตัวหนังสือ")]
        [SerializeField] private Transform focusPoint;

        [Tooltip("ระยะจากกล้องถึงหนังสือที่กด E ได้ (เมตร)")]
        [SerializeField] private float readDistance = 3f;

        [Tooltip("มุมสูงสุดระหว่างทิศที่มองกับหนังสือ (องศา)")]
        [SerializeField] private float lookAngle = 35f;

        [Tooltip("ระยะจากกล้องตอนถือ (เมตร) — 0 = คำนวณให้หน้าคู่พอดีจอ")]
        [SerializeField] private float holdDistance;

        [Tooltip("เผื่อขอบตอนคำนวณระยะถืออัตโนมัติ — 1.1 = เหลือที่ว่างรอบสมุด 10%")]
        [SerializeField] private float holdMargin = 1.1f;

        [Tooltip("เวลาที่สมุดลอยเข้ามา / กลับไปที่เดิม (วินาที)")]
        [SerializeField] private float moveSeconds = 0.6f;

        [Tooltip("hint ตอนมองสมุด (แปลผ่าน L10n)")]
        [SerializeField] private string pickUpHint = "E   Read";

        [Tooltip("hint ตอนถือสมุด (แปลผ่าน L10n)")]
        [SerializeField] private string readingHint = "A / D   Turn page      E   Put back";

        [Header("โผล่เฉพาะโหมด Relax")]
        [Tooltip("✓ = สมุดมองไม่เห็นและหยิบไม่ได้ตอนโหมด Tension (สลับตอนตาปิดสนิท ผู้เล่นไม่เห็นมันหายไปต่อหน้า)\n" +
                 "ซ่อนด้วยการปิดการวาด ไม่ใช่ SetActive(false) — สมุดต้องทำงานอยู่ตลอด เพราะกฎจบ (ปลดล็อกโน้ต)\n" +
                 "ตอนยังอยู่ใน Tension และวิดีโอที่เล่นค้าง/ภาพนิ่งที่เก็บไว้จะหายถ้าปิดทั้ง object")]
        [SerializeField] private bool relaxOnly = true;

        // ─────────────────────────── Runtime ───────────────────────────

        private const int   Closed         = -1;
        private const float PersistentHold = 3600f;

        // โน้ตที่ปลดล็อกแล้ว — อยู่ข้ามการวนคืน (ขึ้นรถคันที่ 4) ความจำไม่ได้หายไปกับเวลาที่ย้อน
        private static readonly HashSet<int> _unlocked = new HashSet<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            _unlocked.Clear();
            IsReading = false;
        }

        /// <summary>
        /// ผู้เล่นกำลังเล็งสมุดอยู่ หรือถือสมุดอยู่ — ปุ่ม E ตอนนี้เป็นของสมุด
        /// SittingPoint เช็กตัวนี้ ไม่งั้นกด E ที่สมุดบนโต๊ะศาลาจะไปนั่ง/ลุกจากม้านั่งพร้อมกัน
        /// </summary>
        public static bool ClaimsInteract { get; private set; }

        /// <summary>
        /// ถือสมุดอยู่ (ตั้งแต่กด E หยิบจนวางคืนเสร็จ) — PhoneSystem เช็กตัวนี้แล้วไม่ยอมเปิดโทรศัพท์
        /// เพราะปิดโทรศัพท์ = ปลดล็อกเดิน/หัน ผู้เล่นจะเดินได้ทั้งที่สมุดยังลอยค้างอยู่ตรงหน้า
        /// </summary>
        public static bool IsReading { get; private set; }

        [Tooltip("เว้นว่าง = หาในซีนเอง — หยิบสมุดตอนโทรศัพท์เปิดอยู่จะปิดโทรศัพท์ให้ก่อน")]
        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;

        private BookVideoController _video;
        private Camera              _cam;
        private int                 _page = Closed;   // หน้าคู่ที่เปิดอยู่ (-1 = ปิด)
        private bool                _turning;
        private bool                _moving;
        private bool                _holding;
        private bool                _aiming;
        private bool                _hintShown;
        private int                 _pendingReveal;   // เลขโน้ตที่รอเปิดให้ดูตอนกลับมา Relax
        private Coroutine           _revealRoutine;

        private Vector3    _homePosition;
        private Quaternion _homeRotation;
        private bool       _lockedMove, _lockedLook;

        private readonly Dictionary<string, float> _clipLengths = new Dictionary<string, float>();

        private bool                 _hidden;
        private Renderer[]           _renderers;
        private Collider[]           _colliders;
        private readonly List<Collider> _collidersTurnedOff = new List<Collider>();

        public int  CurrentPage => _page;
        public bool IsHolding   => _holding;

        public static bool IsUnlocked(int note) => _unlocked.Contains(note);

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (inputHandler == null) inputHandler = FindAnyObjectByType<InputHandler>(FindObjectsInactive.Include);
            if (phoneSystem == null) phoneSystem = FindAnyObjectByType<PhoneSystem.PhoneSystem>(FindObjectsInactive.Include);

            _video = GetComponentInChildren<BookVideoController>(true);
            if (_video != null) _video.FollowAnimator = false;

            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            RuleBase.OnRuleCompleted += OnRuleCompleted;
            if (inputHandler != null) inputHandler.OnInteractPressed += OnInteract;
        }

        private void OnDisable()
        {
            RuleBase.OnRuleCompleted -= OnRuleCompleted;
            if (inputHandler != null) inputHandler.OnInteractPressed -= OnInteract;

            StopAllCoroutines();
            _turning = _moving = false;
            _revealRoutine = null;

            if (_holding)
            {
                transform.SetPositionAndRotation(_homePosition, _homeRotation);
                _holding = false;
            }
            ReleasePlayer();
            ClaimsInteract = false;
            IsReading = false;
            HideHint();
            SetHidden(false);   // เปิดกลับมาแล้ว Update ตัดสินใหม่ตามโหมดตอนนั้น
        }

        private void Start()
        {
            if (animator == null)
            {
                Debug.LogWarning($"[Book] '{name}' ไม่มี Animator — สมุดเปิดไม่ได้", this);
                enabled = false;
                return;
            }

            // เลื่อนเวลาเอง — Animator ห้ามเดินเวลาเอง ไม่งั้น default state (OpenBook) จะเปิดสมุดเอง
            animator.speed = 0f;
            Pose(openState, 0f);   // ท่าปิด
            _page = Closed;

            // หน้าที่ไม่มีคลิปพลิกให้ (เช่นตั้ง Page Count = 7 แต่คลิปมีถึง Page7 = พลิกได้ 6) ตัดทิ้ง
            while (pageCount > 0 && !animator.HasState(0, Animator.StringToHash(PageState(pageCount))))
                pageCount--;
        }

        private void Update()
        {
            UpdateVisibility();
            if (_hidden)
            {
                _aiming = false;
                ClaimsInteract = false;
                if (_video != null) _video.Watching = false;
                HideHint();
                return;
            }

            // ถืออยู่ หรือยืนมองสมุดบนโต๊ะ = ได้ดูวิดีโอจริง (ใช้ _aiming ของเฟรมก่อน — ช้าไปเฟรมเดียวไม่เป็นไร)
            if (_video != null) _video.Watching = _holding || _aiming;

            if (_holding)
            {
                ClaimsInteract = true;
                if (_moving || _turning) return;

                if (PrevPressed()) TryTurn(-1);
                if (NextPressed()) TryTurn(+1);
                return;
            }

            _aiming = _unlocked.Count > 0 && !_moving && PlayerLookingAtBook();
            if (_video != null) _video.Watching = _aiming;
            ClaimsInteract = _aiming;

            if (_aiming) ShowHint(pickUpHint);
            else         HideHint();
        }

        // ─────────────────────────── โผล่เฉพาะ Relax ───────────────────────────

        /// <summary>
        /// โหมดเปลี่ยนตอนตาปิดสนิท (GameModeController.ApplyMode) — ดูทุกเฟรมแล้วซ่อน/โชว์ตาม
        /// จึงสลับตอนจอดำพอดี ไม่ต้องเกาะ event (GameModeController เป็น DontDestroyOnLoad)
        /// </summary>
        private void UpdateVisibility()
        {
            var mode = GameModeController.instance;
            bool hide = relaxOnly && mode != null && mode.CurrentMode != GameMode.Relax;
            if (hide == _hidden) return;

            SetHidden(hide);
            if (hide && _holding) ForcePutBack();
        }

        /// <summary>
        /// ปิดการวาดด้วย forceRenderingOff ไม่ใช่ renderer.enabled — quad ของโน้ตเปิด/ปิด enabled
        /// ของตัวเองตอนพลิกหน้า (BookNoteSurface.Show/Hide) ถ้าใช้ enabled จะตีกันแล้วโน้ตโผล่ลอยกลางอากาศ
        /// </summary>
        private void SetHidden(bool hide)
        {
            if (hide == _hidden) return;
            _hidden = hide;

            if (_renderers != null)
                foreach (var r in _renderers)
                    if (r != null) r.forceRenderingOff = hide;

            if (hide)
            {
                _collidersTurnedOff.Clear();
                if (_colliders != null)
                    foreach (var c in _colliders)
                        if (c != null && c.enabled) { c.enabled = false; _collidersTurnedOff.Add(c); }
            }
            else
            {
                foreach (var c in _collidersTurnedOff)
                    if (c != null) c.enabled = true;
                _collidersTurnedOff.Clear();
            }
        }

        /// <summary>
        /// เข้า Tension ตอนยังถือสมุดอยู่ (นั่งอ่านอยู่ตอนกฎเริ่ม) — วางคืนที่เดิมทันทีตอนจอดำ
        /// ปลดล็อกตัวผู้เล่นคืน แล้วปิดสมุดใต้ความมืด
        /// </summary>
        private void ForcePutBack()
        {
            StopAllCoroutines();
            _turning = _moving = false;
            _revealRoutine = null;

            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _holding  = false;
            IsReading = false;
            ReleasePlayer();
            HideHint();

            StartCoroutine(TurnTo(Closed, closeSpeed));
            if (_pendingReveal != 0) _revealRoutine = StartCoroutine(RevealWhenRelaxed());
        }

        // ─────────────────────────── ปลดล็อก ───────────────────────────

        private void OnRuleCompleted(RuleBase rule)
        {
            int note = rule.Number;
            if (note <= 0 || SpreadOfNote(note) < 0) return;   // ไม่มีโน้ตของข้อนี้ในสมุด
            if (!_unlocked.Add(note)) return;                  // ปลดไปแล้ว

            _pendingReveal = note;
            if (_revealRoutine == null) _revealRoutine = StartCoroutine(RevealWhenRelaxed());
        }

        /// <summary>
        /// รอจนกลับมาโหมด Relax และลืมตาแล้ว — ทุกกฎสั่ง blink กลับ Relax ก่อน/หลังเรียก EndRule
        /// ไม่แน่นอน จึงรอสถานะจริงแทนการฟัง event (event อาจยิงไปก่อนเราได้ข่าวว่าผ่านกฎ)
        /// แล้วเปิดสมุดบนโต๊ะไปหน้าโน้ตใหม่ วิดีโอเริ่มเองตอนพลิกถึง
        /// </summary>
        private IEnumerator RevealWhenRelaxed()
        {
            yield return new WaitUntil(() =>
            {
                var mode = GameModeController.instance;
                return mode == null || (mode.CurrentMode == GameMode.Relax && mode.IsEyesOpen);
            });
            yield return new WaitUntil(() => !_holding && !_moving && !_turning && !_hidden);

            int note = _pendingReveal;
            _pendingReveal = 0;
            _revealRoutine = null;

            int spread = SpreadOfNote(note);
            if (spread >= 0) yield return TurnTo(spread, turnSpeed);
        }

        /// <summary>หน้าคู่ที่โน้ตเลขนี้อยู่ (-1 = ไม่มีโน้ตนี้)</summary>
        private int SpreadOfNote(int note)
        {
            if (_video == null) return -1;
            foreach (var s in _video.Surfaces)
                if (s != null && s.NoteNumber == note) return s.Spread;
            return -1;
        }

        /// <summary>หน้าคู่สุดท้ายที่เปิดอ่านได้ (-1 = ยังไม่ได้โน้ตเลย)</summary>
        private int MaxUnlockedSpread()
        {
            int max = -1;
            if (_video == null) return max;
            foreach (var s in _video.Surfaces)
                if (s != null && _unlocked.Contains(s.NoteNumber)) max = Mathf.Max(max, s.Spread);
            return max;
        }

        // ─────────────────────────── หยิบ / วาง ───────────────────────────

        private void OnInteract()
        {
            if (_hidden || _moving || _turning) return;

            if (_holding) StartCoroutine(PutBackRoutine());
            else if (_aiming) StartCoroutine(PickUpRoutine());
        }

        private IEnumerator PickUpRoutine()
        {
            _holding = true;
            IsReading = true;
            ClaimsInteract = true;
            HideHint();

            // ปิดโทรศัพท์ก่อนล็อก — LowerPhone ปลดล็อกเดิน/หันเสมอ ถ้าปิดทีหลังมันจะไปปลดล็อกของเรา
            // และถ้าผู้เล่นเปิดแอปอยู่ PhoneSystem เป็นคนล็อกไว้ ซึ่งสมุดไม่ได้จำว่าต้องปลดคืน
            if (phoneSystem != null && phoneSystem.CurrentState != Enum.PhoneState.Hidden)
                phoneSystem.LowerPhone();

            LockPlayer();

            _homePosition = transform.position;
            _homeRotation = transform.rotation;

            // ต้องเปิดอยู่ที่หน้าที่มีโน้ตก่อนลอยขึ้นมา — ท่าถือคำนวณจากหน้ากระดาษที่กางอยู่
            int max = MaxUnlockedSpread();
            if (_page < 0 || _page > max) yield return TurnTo(max, turnSpeed * 2f);

            // สมุดเปิดค้างหน้าโน้ตใหม่มาตั้งแต่ผ่านกฎ แล้ววิดีโอเล่นจบไปตอนไม่มีใครดู — เล่นให้ดูใหม่
            if (_video != null) _video.ReplayUnwatched();

            BookNoteSurface page = SurfaceOn(_page) ?? SurfaceOn(max);
            if (page != null && HoldPose(page, out Vector3 pos, out Quaternion rot))
                yield return MoveTo(pos, rot);

            ShowHint(readingHint);
        }

        private IEnumerator PutBackRoutine()
        {
            HideHint();
            yield return MoveTo(_homePosition, _homeRotation);

            _holding = false;
            IsReading = false;
            ReleasePlayer();

            // ปิดหลังวางลงแล้ว ผู้เล่นเดินไปได้เลยไม่ต้องรอ
            yield return TurnTo(Closed, closeSpeed);
        }

        /// <summary>
        /// ท่าถือ: หน้ากระดาษหันเข้าหากล้องตรงๆ ตัวหนังสือตั้งตรง หน้าคู่อยู่กลางจอ
        ///
        /// คิดจาก quad ของโน้ต (ไม่ใช่จากตัวหนังสือ) เพราะโมเดลมาจาก Blender แกนของตัวหนังสือ
        /// ไม่บอกว่าหน้ากระดาษหันทางไหน แต่ quad รู้แน่ — ด้านหน้าของมันคือด้านที่อ่าน (-Z)
        /// และด้านบนคือหัวตัวหนังสือ (+Y) จึงหมุนทั้งเล่มให้ quad ตรงกับกล้อง
        /// </summary>
        private bool HoldPose(BookNoteSurface page, out Vector3 position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = transform.rotation;

            Camera cam = PlayerCamera();
            if (cam == null) return false;

            Transform q = page.transform;
            Quaternion want = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
            Quaternion turn = want * Quaternion.Inverse(q.rotation);
            rotation = turn * transform.rotation;

            // หน้าคู่ = สองหน้ากว้างเท่า quad ต่อกันที่สัน
            Vector2 pageSize = page.FittedSize;
            float distance = holdDistance > 0f ? holdDistance : FitDistance(cam, pageSize.x * 2f, pageSize.y);

            // quad อยู่หน้าขวา (Front) หรือหน้าซ้าย (Back) — เลื่อนไปครึ่งหน้า ให้สันอยู่กลางจอ
            float side = page.Side == BookNoteSurface.PageSide.Front ? 1f : -1f;
            Vector3 quadAt = cam.transform.position
                           + cam.transform.forward * distance
                           + cam.transform.right * (side * pageSize.x * 0.5f);

            position = quadAt - turn * (q.position - transform.position);
            return true;
        }

        /// <summary>ระยะที่กว้าง × สูงนี้พอดีจอพร้อมขอบ</summary>
        private float FitDistance(Camera cam, float width, float height)
        {
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Mathf.Max(0.1f, cam.aspect);
            float d = Mathf.Max(height * 0.5f / tanV, width * 0.5f / tanH);
            return Mathf.Max(cam.nearClipPlane + 0.05f, d * holdMargin);
        }

        private IEnumerator MoveTo(Vector3 position, Quaternion rotation)
        {
            _moving = true;

            Vector3    fromPos = transform.position;
            Quaternion fromRot = transform.rotation;
            float t = 0f;

            while (t < 1f)
            {
                t = moveSeconds > 0f ? Mathf.Min(1f, t + Time.deltaTime / moveSeconds) : 1f;
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.SetPositionAndRotation(Vector3.Lerp(fromPos, position, k),
                                                 Quaternion.Slerp(fromRot, rotation, k));
                yield return null;
            }

            _moving = false;
        }

        private BookNoteSurface SurfaceOn(int spread)
        {
            if (_video == null || spread < 0) return null;
            foreach (var s in _video.Surfaces)
                if (s != null && s.Spread == spread) return s;
            return null;
        }

        // ─────────────────────────── ล็อกผู้เล่น ───────────────────────────

        // ล็อกเฉพาะที่ยังปลดอยู่ — ถ้าระบบอื่นล็อกไว้ก่อน ห้ามไปปลดแทนเจ้าของตอนวางสมุด
        private void LockPlayer()
        {
            var pc = PlayerController.Instance;
            if (pc == null) return;

            if (pc.CanMove) { pc.SetMovement(false); _lockedMove = true; }
            if (pc.CanLook) { pc.SetLook(false);     _lockedLook = true; }
        }

        private void ReleasePlayer()
        {
            var pc = PlayerController.Instance;
            if (pc != null)
            {
                if (_lockedMove) pc.SetMovement(true);
                if (_lockedLook) pc.SetLook(true);
            }
            _lockedMove = _lockedLook = false;
        }

        // ─────────────────────────── พลิก ───────────────────────────

        /// <summary>A / D ตอนถือ — ไม่เกินหน้าแรก และไม่เกินหน้าสุดท้ายที่ปลดล็อกแล้ว</summary>
        private void TryTurn(int direction)
        {
            int target = _page + direction;
            if (target < 0 || target > MaxUnlockedSpread()) return;
            StartCoroutine(TurnTo(target, turnSpeed));
        }

        /// <summary>พลิกทีละหน้าจนถึงหน้าคู่ target (Closed = ปิดสมุด)</summary>
        private IEnumerator TurnTo(int target, float speed)
        {
            target = Mathf.Clamp(target, Closed, pageCount);

            while (_page != target)
            {
                if (_page < target)
                {
                    if (_page == Closed) yield return Turn(openState, true, 0, speed);
                    else                 yield return Turn(PageState(_page + 1), true, _page + 1, speed);
                }
                else
                {
                    if (_page > 0) yield return Turn(PageState(_page), false, _page - 1, speed);
                    else           yield return Turn(openState, false, Closed, speed);
                }
            }
        }

        private IEnumerator Turn(string state, bool forward, int resultPage, float speed)
        {
            if (!animator.HasState(0, Animator.StringToHash(state)))
            {
                Debug.LogWarning($"[Book] ไม่มี state '{state}' ใน Animator Controller ของ '{name}'", this);
                _page = resultPage;   // ข้ามไป ไม่งั้น TurnTo วนไม่จบ
                yield break;
            }

            _turning = true;
            if (_video != null) _video.BeginTurn();

            float length = Mathf.Max(0.05f, ClipLength(state));
            float t = 0f;

            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime * Mathf.Max(0.01f, speed) / length);
                Pose(state, forward ? t : 1f - t);
                yield return null;
            }

            _page = resultPage;
            if (_video != null)
            {
                if (_page == Closed) _video.Close();
                else                 _video.ShowSpread(_page);
            }

            _turning = false;
        }

        private void Pose(string state, float normalizedTime) =>
            animator.Play(state, 0, Mathf.Clamp01(normalizedTime));

        /// <summary>state ที่พลิกกระดูกหน้า page (หน้าคู่ page-1 → page)</summary>
        private string PageState(int page) => string.Format(pageStateFormat, page + clipNumberOffset);

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

        // ─────────────────────────── มอง / ปุ่ม ───────────────────────────

        /// <summary>ผู้เล่นอยู่ใกล้และมองสมุดอยู่ไหม (ผู้เล่นหันไม่ได้ = ไม่นับ: โทรศัพท์ / คัทซีน)</summary>
        private bool PlayerLookingAtBook()
        {
            var pc = PlayerController.Instance;
            if (pc == null || !pc.CanLook) return false;

            Camera cam = PlayerCamera();
            if (cam == null) return false;

            Vector3 target = focusPoint != null ? focusPoint.position : transform.position;
            Vector3 toBook = target - cam.transform.position;

            if (toBook.sqrMagnitude > readDistance * readDistance) return false;
            return Vector3.Angle(cam.transform.forward, toBook) < lookAngle;
        }

        /// <summary>
        /// กล้องที่ผู้เล่นมองอยู่จริง — ไม่ใช้ Camera.main อย่างเดียว ฉากนี้มีกล้องหลายตัว
        /// (คัทซีน, โทรศัพท์ที่เรนเดอร์ลง texture) ถ้ากล้องผู้เล่นไม่ได้ติด tag MainCamera จะได้ตัวอื่น
        /// </summary>
        private Camera PlayerCamera()
        {
            if (_cam != null && _cam.isActiveAndEnabled) return _cam;

            _cam = null;
            var pc = PlayerController.Instance;
            if (pc != null)
            {
                foreach (var cam in pc.GetComponentsInChildren<Camera>())
                {
                    if (cam.isActiveAndEnabled && cam.targetTexture == null) { _cam = cam; break; }
                }
            }

            if (_cam == null) _cam = Camera.main;
            return _cam;
        }

        private bool PrevPressed() => inputHandler != null && inputHandler.WasRulePrevPressed();
        private bool NextPressed() => inputHandler != null && inputHandler.WasRuleNextPressed();

        private string _hintText;

        private void ShowHint(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || PlayerDialogueUI.instance == null) return;
            if (_hintShown && _hintText == text) return;

            PlayerDialogueUI.instance.ShowLine(text, PersistentHold);
            _hintShown = true;
            _hintText  = text;
        }

        private void HideHint()
        {
            if (!_hintShown) return;
            _hintShown = false;
            _hintText  = null;
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }
    }
}
