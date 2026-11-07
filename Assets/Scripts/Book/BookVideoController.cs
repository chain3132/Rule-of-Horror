using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace Book
{
    /// <summary>
    /// เล่นวิดีโอข้อความเวทมนตร์บนหน้ากระดาษของหนังสือ — ใส่ที่ตัวหนังสือ (ตัวที่มี Animator)
    ///
    /// หลักการ:
    ///   - หนังสือเปิดอยู่ที่ "หน้าคู่" ไหน (0 = เพิ่งเปิด, k = พลิกไปแล้ว k หน้า) ตัดสินว่า Note ไหนเห็น
    ///   - เล่นเฉพาะ Note ที่อยู่บนหน้าคู่ที่เปิดอยู่ — มากสุด Max Players ตัวพร้อมกัน (ค่าเริ่ม 2 = ซ้าย+ขวา)
    ///   - VideoPlayer + RenderTexture มีแค่เท่าจำนวนนั้น สร้างครั้งแรกที่ใช้ แล้ววนใช้ซ้ำ
    ///     ไม่ได้มีตัวละหน้า — 7 หน้า 2 ภาษาไม่ต้องมี 14 ตัวถอดรหัสวิดีโอพร้อมกัน
    ///   - เปลี่ยนหน้า: ระหว่างพลิก หยุดภาพค้างไว้ (หน้าที่กำลังพลิกยังมีหมึกติดไป)
    ///     พลิกเสร็จ Note ของหน้าเก่าหยุด/คืนเครื่องเล่น Note ของหน้าใหม่เริ่มจากต้น
    ///   - เล่นจบแล้วเล่นครั้งเดียวพอ: เก็บเฟรมสุดท้ายไว้เป็นภาพนิ่งแล้วคืนเครื่องเล่น พลิกกลับมาหน้าเดิม
    ///     หรือปิดแล้วเปิดสมุดใหม่ก็เห็นตัวหนังสือเต็มหน้าทันที วิดีโอเล่นเฉพาะ Note ที่ยังดูไม่จบ
    ///     (พลิกหนีกลางคัน = ยังไม่จบ กลับมาจะเล่นใหม่จากต้น)
    ///   - เปลี่ยนภาษาตอนเปิดอยู่: สลับไปอีกไฟล์แล้วเล่นต่อจากวินาทีเดิม ไม่วูบขาว
    ///     Note ที่จบแล้วกระโดดไปเฟรมสุดท้ายของภาษาใหม่ ไม่เล่นใหม่
    ///
    /// รู้ว่าเปิดถึงหน้าไหนได้สองทาง (ใช้ทางเดียวหรือทั้งสองทางก็ได้):
    ///   1. Follow Animator — อ่านชื่อคลิปที่ Animator กำลังเล่นเอง คลิปจาก TryNewBook.fbx ชื่อ
    ///      "Armature|OpenBook" และ "Armature|Page1" ... "Armature|Page7" อยู่แล้ว ไม่ต้องแก้อะไร
    ///   2. เรียกเองจากสคริปต์ที่เล่นแอนิเมชันหนังสือ หรือจาก Animation Event:
    ///      Open() / BeginTurn() / ShowSpread(n) / Close()
    /// </summary>
    [DisallowMultipleComponent]
    public class BookVideoController : MonoBehaviour
    {
        /// <summary>หนังสือปิดอยู่</summary>
        public const int Closed = -1;

        /// <summary>กำลังพลิกหน้า (หรือกำลังเปิด/ปิดปก)</summary>
        public const int Turning = -2;

        private const int NoChange = int.MinValue;

        // ─────────────────────────── Inspector ───────────────────────────

        [Header("หนังสือ")]
        [Tooltip("Animator ของหนังสือ — เว้นว่าง = หาในตัวนี้/ลูกเอง")]
        [SerializeField] private Animator animator;

        [Tooltip("✓ = ดูจากคลิปที่ Animator เล่นอยู่ว่าเปิดถึงหน้าไหน (OpenBook / PageN)\n" +
                 "✗ = รอให้สคริปต์อื่นเรียก Open / BeginTurn / ShowSpread / Close เอง")]
        [SerializeField] private bool followAnimator = true;

        [SerializeField] private int animatorLayer;

        [Header("วิดีโอ")]
        [Tooltip("จำนวนวิดีโอที่เล่นพร้อมกันได้มากสุด — หน้าคู่หนึ่งมี 2 ด้าน จึงพอที่ 2")]
        [Range(1, 4)]
        [SerializeField] private int maxPlayers = 2;

        [Tooltip("ขนาด RenderTexture เทียบกับวิดีโอจริง\n" +
                 "0.5 = 874 × 1240 สำหรับวิดีโอ 1748 × 2480 — คมพอสำหรับหนังสือในมือ ใช้หน่วยความจำ 1/4")]
        [Range(0.25f, 1f)]
        [SerializeField] private float textureScale = 0.5f;

        [Tooltip("✓ = วนเล่นซ้ำทุกครั้งที่เปิดหน้า / ✗ = เล่นครั้งเดียว จบแล้วตัวหนังสือค้างบนหน้าถาวร")]
        [SerializeField] private bool loop;

        [Tooltip("เวลาที่หมึกค่อยๆ ปรากฏหลังวิดีโอได้เฟรมแรก (วินาที) — กันหน้ากระดาษกระพริบตอนเริ่ม")]
        [SerializeField] private float fadeInSeconds = 0.3f;

        [Tooltip("✓ = เปลี่ยนภาษาแล้วเล่นต่อจากวินาทีเดิม / ✗ = เริ่มใหม่จากต้น")]
        [SerializeField] private bool keepTimeOnLanguageChange = true;

        [Tooltip("✓ = วิดีโอเดินต่อแม้เกมหยุดเวลา (Time.timeScale = 0) เช่นอ่านหนังสือตอนเปิดเมนู")]
        [SerializeField] private bool useUnscaledTime;

        // ─────────────────────────── Runtime ───────────────────────────

        private sealed class Slot
        {
            public VideoPlayer     Player;
            public RenderTexture   Texture;
            public BookNoteSurface Surface;
            public bool            Ready;        // ได้เฟรมแรกแล้ว
            public float           Fade;
            public double          PendingTime = -1;
            public bool?           LightOnDark;  // null = ยังไม่รู้ (Auto ที่ยังไม่ได้ดูเฟรมแรก)
            public bool            Probing;
            public long            LastFrame = long.MinValue;   // เฟรมที่สร้าง mip ไปแล้ว
            public bool            ReachedEnd;   // วิดีโอเล่นจนจบแล้ว
            public int             EndFrame = -1;   // เฟรมเกมที่เห็นว่าจบ — ใช้รอให้เฟรมสุดท้ายลง texture ก่อนเก็บ
            public bool            Silent;       // ยังไม่ขึ้นจอ — ภาพนิ่งภาษาเดิมค้างอยู่จนกว่าภาษาใหม่จะพร้อม
        }

        /// <summary>เฟรมสุดท้ายของวิดีโอหนึ่งไฟล์ที่เก็บไว้เป็นภาพนิ่ง</summary>
        private sealed class HeldFrame
        {
            public RenderTexture Texture;
            public bool          LightOnDark;
        }

        /// <summary>เลขจำนวนเฟรมที่รอหลังวิดีโอจบก่อนเก็บภาพ — event จบยิงก่อนเฟรมสุดท้ายจะถูกเขียนลง texture</summary>
        private const int HoldDelayFrames = 2;

        // Note ที่เล่นจบแล้ว — อยู่ข้ามการวนคืนเหมือน BookPageTurner._unlocked ความจำไม่ได้หายไปกับเวลาที่ย้อน
        private static readonly HashSet<int> _finishedNotes = new HashSet<int>();

        private readonly Dictionary<VideoClip, HeldFrame> _held        = new Dictionary<VideoClip, HeldFrame>();
        private readonly List<BookNoteSurface>            _showingHeld = new List<BookNoteSurface>();

        private readonly List<BookNoteSurface>  _surfaces  = new List<BookNoteSurface>();
        private readonly List<AnimatorClipInfo> _clipInfos = new List<AnimatorClipInfo>();

        // ผลการดูเฟรมแรกของแต่ละไฟล์ (true = ขาวบนดำ) — ดูครั้งเดียวต่อไฟล์ เปิดหน้าซ้ำไม่ต้องรอ
        private static readonly Dictionary<VideoClip, bool> _detectedStyle = new Dictionary<VideoClip, bool>();

        // ปิด Reload Domain แล้ว static ไม่ถูกล้างเอง — export วิดีโอใหม่แล้วกด Play ต้องดูใหม่
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _detectedStyle.Clear();
            _finishedNotes.Clear();
        }

        private Slot[] _slots;
        private int    _spread = Closed;

        private AnimationClip _parsedClip;
        private int           _parsedPage;     // 0 = OpenBook, >0 = PageN, -1 = คลิปอื่น

        /// <summary>หน้าคู่ที่เปิดอยู่ (Closed / Turning / 0, 1, 2 ...)</summary>
        public int CurrentSpread => _spread;

        /// <summary>
        /// อ่านหน้าจาก Animator เองไหม — BookPageTurner ปิดตัวนี้ เพราะมันเลื่อนเวลาแอนิเมชันเอง
        /// (เล่นถอยหลังได้) แล้วบอกหน้าคู่ตรงๆ ผ่าน BeginTurn / ShowSpread / Close แทน
        /// </summary>
        public bool FollowAnimator
        {
            get => followAnimator;
            set => followAnimator = value;
        }

        // ─────────────────────────── API ───────────────────────────

        /// <summary>เปิดหนังสือ (เห็นหน้าคู่แรก)</summary>
        public void Open() => SetSpread(0);

        /// <summary>เริ่มพลิกหน้า — ภาพค้างไว้จนกว่าจะเรียก ShowSpread</summary>
        public void BeginTurn() => SetSpread(Turning);

        /// <summary>พลิกเสร็จ ตอนนี้เห็นหน้าคู่ที่ spread (= พลิกไปแล้ว spread หน้า)</summary>
        public void ShowSpread(int spread) => SetSpread(Mathf.Max(0, spread));

        /// <summary>ปิดหนังสือ — หยุดทุกวิดีโอ</summary>
        public void Close() => SetSpread(Closed);

        /// <summary>Note ทุกอันในหนังสือ</summary>
        public IReadOnlyList<BookNoteSurface> Surfaces => _surfaces;

        /// <summary>หา Note ในหนังสือใหม่ — เรียกถ้าเพิ่ม/ลบ BookNoteSurface ตอนเกมเล่นอยู่</summary>
        public void RefreshSurfaces()
        {
            _surfaces.Clear();
            GetComponentsInChildren(true, _surfaces);
        }

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            RefreshSurfaces();
        }

        private void OnEnable()
        {
            // อ่าน IsThai ก่อนหนึ่งครั้ง — L10n ต่อ event เปลี่ยนภาษาตอนถูกถามครั้งแรก
            _ = L10n.IsThai;
            L10n.Changed -= OnLanguageChanged;
            L10n.Changed += OnLanguageChanged;
        }

        private void OnDisable()
        {
            L10n.Changed -= OnLanguageChanged;

            // ซ่อนหนังสือ = ปิดหนังสือ คืนทุกอย่าง เปิดกลับมาจะเริ่มใหม่ตาม Animator
            _spread = Closed;
            ReleaseAll(destroy: true);
            DestroyHeldFrames();
        }

        private void Update()
        {
            if (followAnimator) ReadSpreadFromAnimator();
            UpdateMips();
            UpdateHolds();
            UpdateFades();
        }

        // ─────────────────────────── หน้าคู่ ───────────────────────────

        private void SetSpread(int spread)
        {
            if (spread == _spread) return;
            _spread = spread;

            if (spread == Turning)
            {
                // ค้างภาพไว้ หน้าที่กำลังพลิกยังมีหมึกติดไปด้วย — ยังไม่คืนเครื่องเล่น
                // เผื่อพลิกกลับมาหน้าเดิม (ยกเลิกการพลิก) จะได้เล่นต่อได้ทันที
                if (_slots == null) return;
                foreach (var slot in _slots)
                    if (slot.Surface != null && slot.Player.isPlaying) slot.Player.Pause();
                return;
            }

            // คืนเครื่องเล่นของ Note ที่ไม่ได้อยู่บนหน้านี้
            if (_slots != null)
                foreach (var slot in _slots)
                    if (slot.Surface != null && slot.Surface.Spread != spread) Release(slot);

            HideHeldOutside(spread);

            if (spread < 0) return;

            foreach (var surface in _surfaces)
            {
                if (surface == null || surface.Spread != spread) continue;

                Slot slot = SlotOf(surface);
                if (slot != null)
                {
                    // กลับมาหน้าเดิมหลังพลิกค้างกลางทาง — เล่นต่อ ไม่ต้องเริ่มใหม่
                    if (slot.Player.isPrepared && !slot.Player.isPlaying && !Finished(slot)) slot.Player.Play();
                    continue;
                }

                // เล่นจบไปแล้ว — โชว์ภาพนิ่งทันที ไม่ต้องใช้เครื่องเล่นเลย
                bool finished = _finishedNotes.Contains(surface.NoteNumber);
                if (finished && TryShowHeld(surface)) continue;

                slot = FreeSlot();
                if (slot == null)
                {
                    Debug.LogWarning($"[Book] หน้าคู่ {spread} มี Note มากกว่า Max Players ({maxPlayers}) — " +
                                     $"Note {surface.NoteNumber} ไม่ถูกเล่น", this);
                    continue;
                }

                // จบแล้วแต่ไม่มีภาพนิ่งของไฟล์นี้ — กระโดดไปเฟรมสุดท้าย แล้วเก็บเป็นภาพนิ่งต่อ
                Assign(slot, surface, jumpToEnd: finished);
            }
        }

        /// <summary>
        /// อ่านหน้าที่เปิดอยู่จาก Animator
        ///
        /// OpenBook เล่นอยู่       → กำลังเปิด (Turning)  · จบแล้ว → หน้าคู่ 0
        /// PageN เล่นอยู่          → กำลังพลิก (Turning)  · จบแล้ว → หน้าคู่ N
        /// เล่นถอยหลัง (speed < 0) → จบแล้วได้หน้าคู่ N-1 / OpenBook ถอยหลังจบ = ปิดหนังสือ
        /// คลิปอื่น (idle, Page1f ...) → ไม่เปลี่ยนอะไร
        ///
        /// ระหว่าง transition ใช้ state ปลายทาง — พอเริ่มพลิกหน้าถัดไป หน้าเดิมต้องหยุดเลย
        /// </summary>
        private void ReadSpreadFromAnimator()
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                return;

            AnimatorStateInfo state;
            if (animator.IsInTransition(animatorLayer))
            {
                state = animator.GetNextAnimatorStateInfo(animatorLayer);
                animator.GetNextAnimatorClipInfo(animatorLayer, _clipInfos);
            }
            else
            {
                state = animator.GetCurrentAnimatorStateInfo(animatorLayer);
                animator.GetCurrentAnimatorClipInfo(animatorLayer, _clipInfos);
            }

            if (_clipInfos.Count == 0 || _clipInfos[0].clip == null) return;

            int spread = SpreadFromClip(_clipInfos[0].clip, state);
            if (spread != NoChange) SetSpread(spread);
        }

        private int SpreadFromClip(AnimationClip clip, AnimatorStateInfo state)
        {
            int page = ParseClip(clip);
            if (page < 0) return NoChange;

            bool  reverse = state.speed * state.speedMultiplier < 0f;
            float t       = state.normalizedTime;
            bool  done    = reverse ? t <= 0f : t >= 1f;

            if (!done) return Turning;

            if (page == 0) return reverse ? Closed : 0;
            return reverse ? page - 1 : page;
        }

        // จำผลของคลิปล่าสุด — FollowAnimator ถูกเรียกทุกเฟรม ไม่ต้อง parse ชื่อซ้ำ
        private int ParseClip(AnimationClip clip)
        {
            if (clip == _parsedClip) return _parsedPage;

            _parsedClip = clip;

            // ชื่อจาก FBX เป็น "Armature|Page3" — ตัดส่วนหน้า "|" ออกก่อน
            string name = clip.name;
            int bar = name.LastIndexOf('|');
            if (bar >= 0) name = name.Substring(bar + 1);

            if (name == "OpenBook") _parsedPage = 0;
            else
            {
                int n = BookNoteSurface.ParsePageNumber(name);
                _parsedPage = n > 0 ? n : -1;
            }
            return _parsedPage;
        }

        // ─────────────────────────── เครื่องเล่น ───────────────────────────

        private void EnsureSlots()
        {
            // สร้างครั้งเดียว — แก้ Max Players ตอน Play จะมีผลหลังปิด/เปิดหนังสือ (OnDisable ล้างทิ้ง)
            if (_slots != null) return;

            _slots = new Slot[maxPlayers];

            for (int i = 0; i < maxPlayers; i++)
            {
                var go = new GameObject($"Book Video Player {i + 1}");
                go.transform.SetParent(transform, false);

                var player = go.AddComponent<VideoPlayer>();
                player.playOnAwake       = false;
                player.source            = VideoSource.VideoClip;
                player.renderMode        = VideoRenderMode.RenderTexture;
                player.aspectRatio       = VideoAspectRatio.Stretch;   // RT สัดส่วนเท่าวิดีโออยู่แล้ว
                player.audioOutputMode   = VideoAudioOutputMode.None;  // ข้อความเวทมนตร์ไม่มีเสียง
                player.waitForFirstFrame = true;
                player.skipOnDrop        = true;
                player.timeUpdateMode    = useUnscaledTime ? VideoTimeUpdateMode.UnscaledGameTime
                                                           : VideoTimeUpdateMode.GameTime;

                var slot = new Slot { Player = player };
                player.prepareCompleted += _ => OnPrepared(slot);
                player.started          += _ => slot.Ready = true;
                player.loopPointReached += _ => slot.ReachedEnd = true;
                player.errorReceived    += (_, message) =>
                    Debug.LogWarning($"[Book] วิดีโอ Note " +
                                     $"{(slot.Surface != null ? slot.Surface.NoteNumber.ToString() : "?")} " +
                                     $"เล่นไม่ได้: {message}", this);

                _slots[i] = slot;
            }
        }

        /// <param name="jumpToEnd">เริ่มที่เฟรมสุดท้ายเลย สำหรับ Note ที่เคยเล่นจบแล้ว</param>
        /// <param name="silent">ไม่แตะภาพบนหน้ากระดาษจนกว่าจะเก็บภาพนิ่งเสร็จ</param>
        private void Assign(Slot slot, BookNoteSurface surface, bool jumpToEnd = false, bool silent = false)
        {
            VideoClip clip = surface.ClipFor(L10n.IsThai);
            if (clip == null)
            {
                Debug.LogWarning($"[Book] Note {surface.NoteNumber} ยังไม่มีวิดีโอ — " +
                                 "กด Rule of Horror ▸ Book ▸ Link Note Videos", surface);
                return;
            }

            slot.Surface     = surface;
            slot.Ready       = false;
            slot.Fade        = 0f;
            slot.PendingTime = jumpToEnd ? double.MaxValue : -1;   // OnPrepared ตัดให้เหลือเฟรมสุดท้ายเอง
            slot.LightOnDark = StyleOf(surface, clip);
            slot.Probing     = false;
            slot.ReachedEnd  = false;
            slot.EndFrame    = -1;
            slot.Silent      = silent;

            EnsureTexture(slot, clip);
            ClearToPaper(slot.Texture, slot.LightOnDark ?? false);

            slot.Player.isLooping     = loop;
            slot.Player.targetTexture = slot.Texture;
            slot.Player.clip          = clip;
            slot.Player.Prepare();

            if (!silent) surface.Show(slot.Texture, 0f, slot.LightOnDark ?? false);

#if UNITY_EDITOR
            Debug.Log($"[Book] Note {surface.NoteNumber} → '{clip.name}' ({slot.Texture.width}×{slot.Texture.height})", surface);
#endif
        }

        /// <summary>ตั้งไว้ใน Inspector → ใช้ตามนั้น / Auto → ที่เคยดูไว้แล้ว / ยังไม่รู้ → null</summary>
        private static bool? StyleOf(BookNoteSurface surface, VideoClip clip)
        {
            switch (surface.Style)
            {
                case BookNoteSurface.InkStyle.DarkOnLight: return false;
                case BookNoteSurface.InkStyle.LightOnDark: return true;
            }
            return clip != null && _detectedStyle.TryGetValue(clip, out bool lightOnDark) ? lightOnDark : (bool?)null;
        }

        /// <summary>
        /// ดูเฟรมแรกว่าพื้นขาวหรือพื้นดำ — อ่านแค่สี่เหลี่ยมเล็กๆ กลางภาพ ไม่ใช่ทั้งเฟรม
        ///
        /// เฟรมแรกของข้อความเวทมนตร์ยังไม่มีตัวหนังสือ (ค่อยๆ เขียนขึ้นมา) กลางภาพจึงเป็นสีพื้นล้วน
        /// อ่านกลับจาก GPU แบบ async ไม่ทำให้เกมกระตุก ระหว่างรอหมึกยังไม่ขึ้น (fade ค้างที่ 0)
        /// ไม่งั้นพื้นดำจะโผล่เต็มหน้าแวบหนึ่งก่อนรู้ว่าต้องกลับสี
        /// </summary>
        private void ProbeStyle(Slot slot)
        {
            slot.Probing = true;
            VideoClip clip = slot.Player.clip;

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Resolve(slot, clip, lightOnDark: false);
                return;
            }

            const int size = 16;
            int x = Mathf.Max(0, slot.Texture.width  / 2 - size / 2);
            int y = Mathf.Max(0, slot.Texture.height / 2 - size / 2);

            UnityEngine.Rendering.AsyncGPUReadback.Request(slot.Texture, 0, x, size, y, size, 0, 1,
                TextureFormat.RGBA32, request =>
                {
                    if (request.hasError) { Resolve(slot, clip, lightOnDark: false); return; }

                    var data = request.GetData<Color32>();
                    float sum = 0f;
                    foreach (var c in data) sum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

                    float average = data.Length > 0 ? sum / (data.Length * 255f) : 1f;
                    Resolve(slot, clip, lightOnDark: average < 0.5f);
                });
        }

        private void Resolve(Slot slot, VideoClip clip, bool lightOnDark)
        {
            if (clip != null) _detectedStyle[clip] = lightOnDark;

            // ระหว่างรอ slot อาจถูกคืนหรือเปลี่ยนไฟล์ไปแล้ว — เก็บผลไว้ใน cache อย่างเดียว
            if (slot.Surface == null || slot.Player.clip != clip) return;

            slot.LightOnDark = lightOnDark;
            slot.Probing     = false;

#if UNITY_EDITOR
            Debug.Log($"[Book] '{clip.name}' = {(lightOnDark ? "ตัวหนังสือขาวบนพื้นดำ" : "หมึกเข้มบนพื้นขาว")}", slot.Surface);
#endif
        }

        private void OnPrepared(Slot slot)
        {
            if (slot.Surface == null) return;

            if (slot.PendingTime >= 0)
            {
                double last = slot.Player.length - 1.0 / Mathf.Max(1f, (float)slot.Player.frameRate);
                slot.Player.time = System.Math.Min(slot.PendingTime, System.Math.Max(0, last));
                slot.PendingTime = -1;
            }

            // เตรียมเสร็จตอนกำลังพลิกหน้า — รอให้พลิกเสร็จก่อน (SetSpread จะสั่ง Play เอง)
            if (_spread == slot.Surface.Spread) slot.Player.Play();
        }

        private void Release(Slot slot)
        {
            slot.Player.Stop();      // Stop คืนตัวถอดรหัสวิดีโอ ไม่ใช่แค่หยุดภาพ
            slot.Player.clip = null;

            if (slot.Surface != null) slot.Surface.Hide();
            slot.Surface     = null;
            slot.Ready       = false;
            slot.Fade        = 0f;
            slot.LightOnDark = null;
            slot.Probing     = false;
            slot.ReachedEnd  = false;
            slot.EndFrame    = -1;
            slot.Silent      = false;
        }

        // ─────────────────────────── ภาพนิ่งหลังเล่นจบ ───────────────────────────

        /// <summary>วิดีโอไหนจบแล้ว เก็บเฟรมสุดท้ายเป็นภาพนิ่ง แล้วคืนเครื่องเล่น</summary>
        private void UpdateHolds()
        {
            if (_slots == null || loop) return;

            foreach (var slot in _slots)
            {
                if (slot.Surface == null || !slot.Ready) continue;

                // เผื่อ event จบไม่ยิงตอนกระโดดไปเฟรมสุดท้าย จึงดูจากเวลาที่หยุดอยู่ด้วย
                bool stoppedAtEnd = _spread == slot.Surface.Spread && slot.Player.isPrepared &&
                                    !slot.Player.isPlaying && Finished(slot);
                if (!slot.ReachedEnd && !stoppedAtEnd) continue;

                if (slot.EndFrame < 0) slot.EndFrame = Time.frameCount;
                if (Time.frameCount - slot.EndFrame < HoldDelayFrames) continue;

                // ยังไม่รู้ว่าพื้นขาวหรือพื้นดำ — UpdateFades กำลังดูเฟรมอยู่ ภาพนิ่งต้องรู้ค่านี้
                if (slot.LightOnDark == null) continue;

                Hold(slot);
            }
        }

        private void Hold(Slot slot)
        {
            BookNoteSurface surface = slot.Surface;
            VideoClip       clip    = slot.Player.clip;

            var held = new HeldFrame { Texture = CreateInkTexture(slot.Texture.width, slot.Texture.height,
                                                                   $"Book Note {surface.NoteNumber} Held"),
                                       LightOnDark = slot.LightOnDark.Value };

            Graphics.Blit(slot.Texture, held.Texture);
            held.Texture.GenerateMips();

            if (_held.TryGetValue(clip, out HeldFrame old)) DestroyHeld(old);
            _held[clip] = held;

            _finishedNotes.Add(surface.NoteNumber);

            Release(slot);   // ซ่อนหน้านี้ไปด้วย แต่ภาพนิ่งขึ้นแทนในเฟรมเดียวกัน จึงไม่เห็นกระพริบ
            ShowHeld(surface, held);
        }

        /// <summary>โชว์ภาพนิ่งของภาษาปัจจุบัน — คืน false ถ้ายังไม่มี</summary>
        private bool TryShowHeld(BookNoteSurface surface)
        {
            VideoClip clip = surface.ClipFor(L10n.IsThai);
            if (clip == null || !_held.TryGetValue(clip, out HeldFrame held)) return false;

            // RenderTexture หายได้ถ้าอุปกรณ์กราฟิกถูกรีเซ็ต — ให้กลับไปกระโดดเฟรมสุดท้ายแทนหน้าว่าง
            if (!held.Texture.IsCreated()) return false;

            ShowHeld(surface, held);
            return true;
        }

        private void ShowHeld(BookNoteSurface surface, HeldFrame held)
        {
            surface.Show(held.Texture, 1f, held.LightOnDark);
            if (!_showingHeld.Contains(surface)) _showingHeld.Add(surface);
        }

        private void HideHeldOutside(int spread)
        {
            for (int i = _showingHeld.Count - 1; i >= 0; i--)
            {
                BookNoteSurface surface = _showingHeld[i];
                if (surface != null && surface.Spread == spread) continue;

                if (surface != null) surface.Hide();
                _showingHeld.RemoveAt(i);
            }
        }

        private void DestroyHeldFrames()
        {
            foreach (BookNoteSurface surface in _showingHeld)
                if (surface != null) surface.Hide();
            _showingHeld.Clear();

            foreach (HeldFrame held in _held.Values) DestroyHeld(held);
            _held.Clear();
        }

        private static void DestroyHeld(HeldFrame held)
        {
            if (held.Texture == null) return;

            held.Texture.Release();
            Destroy(held.Texture);
        }

        private void ReleaseAll(bool destroy)
        {
            if (_slots == null) return;

            foreach (var slot in _slots)
            {
                if (slot.Player != null) Release(slot);

                if (!destroy) continue;

                if (slot.Player != null)
                {
                    slot.Player.targetTexture = null;
                    Destroy(slot.Player.gameObject);
                }

                if (slot.Texture != null)
                {
                    slot.Texture.Release();
                    Destroy(slot.Texture);
                }
            }

            if (destroy) _slots = null;
        }

        private Slot SlotOf(BookNoteSurface surface)
        {
            if (_slots == null) return null;
            foreach (var slot in _slots)
                if (slot.Surface == surface) return slot;
            return null;
        }

        private Slot FreeSlot()
        {
            EnsureSlots();
            foreach (var slot in _slots)
                if (slot.Surface == null) return slot;
            return null;
        }

        private static bool Finished(Slot slot) =>
            slot.Player.length > 0 && slot.Player.time >= slot.Player.length - 0.05;

        // ─────────────────────────── RenderTexture ───────────────────────────

        /// <summary>
        /// RenderTexture ของช่องนี้ ขนาดตามวิดีโอ × Texture Scale — ใช้ของเดิมถ้าขนาดเท่ากัน
        ///
        /// sRGB เพราะวิดีโอเป็นสี sRGB — โปรเจกต์นี้เป็น Linear ถ้าใช้ RT แบบ linear สีหมึกจะซีดลง
        /// ไม่มี depth (วิดีโอไม่ต้องใช้)
        ///
        /// มี mipmap + anisotropic: หน้ากระดาษบนจอเล็กกว่าวิดีโอเป็นสิบเท่าและมักมองเฉียงๆ
        /// ไม่มี mip เส้นตัวหนังสือจะหายเป็นจุดๆ และกระพริบทุกครั้งที่ขยับกล้อง
        /// VideoPlayer เขียนแค่ mip 0 — สร้าง mip ที่เหลือเองทุกครั้งที่ได้เฟรมใหม่ (UpdateMips)
        /// </summary>
        private void EnsureTexture(Slot slot, VideoClip clip)
        {
            int w = Mathf.Max(16, Mathf.RoundToInt(clip.width  * textureScale));
            int h = Mathf.Max(16, Mathf.RoundToInt(clip.height * textureScale));

            if (slot.Texture != null && slot.Texture.width == w && slot.Texture.height == h) return;

            if (slot.Texture != null)
            {
                slot.Player.targetTexture = null;
                slot.Texture.Release();
                Destroy(slot.Texture);
            }

            slot.Texture   = CreateInkTexture(w, h, $"Book Ink {System.Array.IndexOf(_slots, slot) + 1}");
            slot.LastFrame = long.MinValue;
        }

        private static RenderTexture CreateInkTexture(int width, int height, string textureName)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name             = textureName,
                useMipMap        = true,
                autoGenerateMips = false,
                wrapMode         = TextureWrapMode.Clamp,
                filterMode       = FilterMode.Trilinear,
                anisoLevel       = 8,
            };
            texture.Create();
            return texture;
        }

        /// <summary>
        /// ล้าง RT เป็นสีพื้นของวิดีโอ = "ไม่มีหมึก" (ขาวสำหรับหมึกเข้มบนพื้นขาว, ดำสำหรับขาวบนดำ)
        ///
        /// RT ที่เพิ่งสร้างหรือถูกใช้ซ้ำมีภาพเก่า/ขยะค้างอยู่ — ถ้าไม่ล้าง Note ใหม่จะโผล่ตัวหนังสือ
        /// ของ Note ก่อนหน้าแวบหนึ่งก่อนวิดีโอใหม่ได้เฟรมแรก
        /// </summary>
        private static void ClearToPaper(RenderTexture texture, bool lightOnDark)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, lightOnDark ? Color.black : Color.white);
            RenderTexture.active = previous;
            texture.GenerateMips();
        }

        private void UpdateMips()
        {
            if (_slots == null) return;

            foreach (var slot in _slots)
            {
                if (slot.Surface == null || slot.Texture == null || !slot.Ready) continue;

                long frame = slot.Player.frame;
                if (frame == slot.LastFrame) continue;   // หยุด/ค้างเฟรมสุดท้าย ไม่ต้องทำซ้ำ

                slot.LastFrame = frame;
                slot.Texture.GenerateMips();
            }
        }

        // ─────────────────────────── Fade ───────────────────────────

        private void UpdateFades()
        {
            if (_slots == null) return;

            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            foreach (var slot in _slots)
            {
                if (slot.Surface == null || !slot.Ready || slot.Fade >= 1f) continue;

                // ยังไม่รู้ว่าพื้นขาวหรือพื้นดำ — ดูเฟรมแรกก่อน ค่อยเริ่ม fade
                if (slot.LightOnDark == null)
                {
                    if (!slot.Probing) ProbeStyle(slot);
                    continue;
                }

                if (slot.Silent) continue;

                slot.Fade = fadeInSeconds > 0f ? Mathf.Min(1f, slot.Fade + dt / fadeInSeconds) : 1f;
                slot.Surface.Show(slot.Texture, slot.Fade, slot.LightOnDark.Value);
            }
        }

        // ─────────────────────────── ภาษา ───────────────────────────

        /// <summary>
        /// เปลี่ยนภาษาตอนหนังสือเปิดอยู่ — สลับไฟล์ของ Note ที่กำลังเล่น
        ///
        /// ไม่ล้าง RT และไม่ fade ใหม่: เฟรมสุดท้ายของภาษาเดิมค้างอยู่จนเฟรมแรกของภาษาใหม่มาทับ
        /// หน้ากระดาษจึงไม่วูบขาว แค่ตัวหนังสือเปลี่ยนภาษาไปเฉยๆ
        /// </summary>
        private void OnLanguageChanged()
        {
            bool thai = L10n.IsThai;

            if (_slots != null) SwitchPlayingLanguage(thai);

            // ภาพนิ่งเปลี่ยนภาษาด้วย: มีของภาษาใหม่แล้วใช้เลย ไม่มีก็ให้เครื่องเล่นกระโดดไปเฟรมสุดท้าย
            // โดยภาพนิ่งภาษาเดิมค้างอยู่จนของใหม่พร้อม หน้ากระดาษจึงไม่วูบ
            for (int i = _showingHeld.Count - 1; i >= 0; i--)
            {
                BookNoteSurface surface = _showingHeld[i];
                if (surface == null || SlotOf(surface) != null || TryShowHeld(surface)) continue;

                Slot slot = FreeSlot();
                if (slot != null) Assign(slot, surface, jumpToEnd: true, silent: true);
            }
        }

        private void SwitchPlayingLanguage(bool thai)
        {
            foreach (var slot in _slots)
            {
                if (slot.Surface == null) continue;

                VideoClip clip = slot.Surface.ClipFor(thai);
                if (clip == null || clip == slot.Player.clip) continue;

                slot.ReachedEnd = false;
                slot.EndFrame   = -1;

                double time = keepTimeOnLanguageChange && slot.Player.isPrepared ? slot.Player.time : 0;

                // ไฟล์ภาษาใหม่รู้แบบแล้ว (ตั้งเองหรือเคยดูแล้ว) ใช้เลย — ไม่รู้ก็ใช้แบบเดิมไปก่อน
                // สองภาษามาจาก comp เดียวกัน แทบไม่มีทางต่างกัน
                bool? style = StyleOf(slot.Surface, clip);
                if (style != null) slot.LightOnDark = style;
                slot.Probing = false;

                // ไฟล์คนละขนาดต้องสร้าง RT ใหม่ ซึ่งว่างเปล่า — กรณีนี้ต้อง fade ใหม่จริงๆ
                var before = slot.Texture;
                EnsureTexture(slot, clip);
                if (slot.Texture != before)
                {
                    ClearToPaper(slot.Texture, slot.LightOnDark ?? false);
                    slot.Ready = false;
                    slot.Fade  = 0f;
                    if (!slot.Silent) slot.Surface.Show(slot.Texture, 0f, slot.LightOnDark ?? false);
                }

                slot.Player.targetTexture = slot.Texture;
                slot.Player.clip          = clip;
                slot.PendingTime          = time;
                slot.Player.Prepare();
            }
        }
    }
}
