using System;
using System.Collections;
using FMOD.Studio;
using FMODUnity;
using InputSystem;
using Manager;
using Player;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.UI;


/// <summary>
/// ฉากเปิดเกม — รถประจำทางพาผู้เล่นมาถึงศาลา
///
/// ลำดับ:
///   ① ผู้เล่นนั่งอยู่ในรถตั้งแต่เฟรมแรก รถอยู่ที่ปลายถนน (Bus Start Point)
///   ② รถค่อยๆ วิ่งเข้ามาจอดที่ศาลา — ช่วงนี้เป็นกล้องหนัง ตัด shot ตามวินาที + ขึ้นเครดิต
///   ③ รถจอด → ตัดกล้องมาเป็นมุมมองผู้เล่นที่นั่งอยู่ในรถ = จุดที่ผู้เล่นคุมตัวเองได้แล้ว
///   ④ กด E ลุกจากเบาะ เดินลงจากรถ
///   ⑤ นาฬิกาเดินต่อ (จะได้ถึง 18:40 ที่ข้อความแรกเข้า) แล้วรถค่อยๆ ขับออกไป
///
/// ฉากนี้คือฉากเดียวกับบทจบวนลูปของกฎข้อ 6 — คันที่ 4 พาผู้เล่นกลับมาถึงศาลาที่ 18:40
/// ด้วยลำดับเดียวกันทุกบีต นั่นคือตัวมุก: คืนที่สองไม่ใช่คืนใหม่ที่คล้ายคืนก่อน มันคือคืนเดิม
/// ซ้ำ ทั้งสองฝั่งจึงเรียก BusRide ตัวเดียวกัน (ขับ / พาผู้เล่นไปกับรถ / นั่ง / ลงจากรถ)
/// ถ้าจะแก้จังหวะของฉากนี้ ให้ไปแก้ค่าในกฎ 6 ให้ตรงกันด้วย
///
/// สิ่งที่ถอดออกจากเวอร์ชันก่อน: ผู้เล่นเดินตาม waypoints มานั่งที่ม้านั่งศาลา และรถอีกคัน
/// ที่วิ่งผ่านระหว่าง shot — ตอนนี้รถที่ผู้เล่นนั่งมาเป็นทั้งพาหนะและของที่วิ่งอยู่ในเฟรมแล้ว
/// </summary>
public class CutsceneManager : MonoBehaviour
{
    public static CutsceneManager instance;

    // ─────────────────────────── Cutscene Gate ───────────────────────────

    /// <summary>
    /// ตอนนี้ยังอยู่ในฉากเปิดอยู่ไหม — UI ที่ไม่ควรโผล่ทับฉากเปิดเช็กตัวนี้
    ///
    /// ต่างจาก _playing ตรงที่ตัวนั้นหมายถึง "ยังกด skip ได้อยู่" ซึ่งปิดไปตั้งแต่ต้น HandOverRoutine
    /// ทั้งที่บทพูดปิดฉากยังเล่นอยู่ ส่วนตัวนี้จะเป็น true จนถึงวินาทีที่คืนการควบคุมให้ผู้เล่นจริงๆ
    /// และครอบคลุมทางลัด disableCutscene ด้วย (ซึ่งก็ยังมีช่วงที่ผู้เล่นขยับไม่ได้เหมือนกัน)
    /// </summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>ฉากเปิดเริ่ม (true) / จบแล้ว (false)</summary>
    public static event Action<bool> OnPlayingChanged;

    private static void SetPlaying(bool value)
    {
        if (IsPlaying == value) return;

        IsPlaying = value;
        OnPlayingChanged?.Invoke(value);
    }

    // ─────────────────────────── Credit Entry ───────────────────────────

    [System.Serializable]
    public class CreditEntry
    {
        [Tooltip("บรรทัดบน — เช่น ชื่อค่าย / ตำแหน่ง\nเว้นว่างถ้าไม่ต้องการแสดง")]
        public string line1 = "";

        [Tooltip("บรรทัดล่าง — เช่น ชื่อนักพัฒนา\nเว้นว่างถ้าไม่ต้องการแสดง")]
        public string line2 = "";

        [Tooltip("ความเร็ว fade in — ค่าสูง = เร็ว, ค่าต่ำ = ช้า\n" +
                 "(ระยะเวลา = 1 / speed  เช่น speed 1 = 1 วิ, speed 2 = 0.5 วิ)")]
        public float fadeInSpeed  = 1.5f;

        [Tooltip("เวลาที่ข้อความค้างอยู่ที่ alpha เต็ม (วินาที)")]
        public float holdDuration = 2.0f;

        [Tooltip("ความเร็ว fade out — ค่าสูง = เร็ว, ค่าต่ำ = ช้า")]
        public float fadeOutSpeed = 1.5f;
    }

    // ─────────────────────────── Shot Data ───────────────────────────

    [System.Serializable]
    public class CutsceneShot
    {
        [Tooltip("CinemachineCamera ของ shot นี้ — วางไว้ใน scene ล่วงหน้า\n" +
                 "ปิด GameObject ไว้ก่อน script จะเปิด/ปิดเอง")]
        public CinemachineCamera vcam;

        [Tooltip("ตัดมา shot นี้ตอนรถวิ่งมาแล้วกี่วินาที (นับจากรถเริ่มออกตัว)\n" +
                 "0 = เปิดตั้งแต่ต้น · ใส่ได้เกิน Arrive Seconds ไม่ได้ (จะไม่ได้เล่น)\n\n" +
                 "เรียงลำดับเองไม่ต้องกังวล — ตัวที่ \"ถึงคิวแล้วและคิวช้าสุด\" จะถูกเปิด\n" +
                 "ถ้าใส่เวลาซ้ำกัน ตัวที่อยู่ล่างกว่าในลิสต์ชนะ")]
        public float activateAtSecond = 0f;

        [Tooltip("✓ = ตัดมา shot นี้ทันที (Cut)  ✗ = ค่อยๆ blend ตาม Blend Time ที่ตั้งไว้")]
        public bool instantCut = false;

        [Header("Track Player (หันมองผู้เล่นโดยไม่ขยับกล้อง)")]
        [Tooltip("กล้องจะค่อยๆ หันตามผู้เล่น (กล้องไม่ขยับ แค่หมุน)\n" +
                 "ผู้เล่นนั่งอยู่ในรถตลอดฉากนี้ ตัวนี้จึงเท่ากับ \"หันตามรถ\" — ใช้ทำ shot แบบ\n" +
                 "กล้องตั้งไว้ข้างถนนแล้วกวาดตามรถที่วิ่งผ่านไป")]
        public bool trackPlayer = false;

        [Tooltip("ความเร็วในการหัน (องศา/วินาที ประมาณ) — ค่าต่ำ = ค่อยๆ หัน")]
        public float trackSpeed = 2f;

        [Tooltip("offset ความสูงของจุดที่มอง (0 = เท้า, 1.4 = หัว)")]
        public float trackHeightOffset = 1.4f;

        [Header("Tilt Up (shot สุดท้าย)")]
        [Tooltip("เงยกล้องขึ้นช้าๆ หลังจาก shot นี้ถูกเปิด")]
        public bool doTiltUp = false;

        [Tooltip("มุม X เริ่มต้น (องศา) — บวก = มองลง, ลบ = มองขึ้น")]
        public float tiltStartX = 20f;

        [Tooltip("มุม X ปลายทาง (เงยขึ้น)")]
        public float tiltEndX = -10f;

        [Tooltip("ใช้เวลา tilt กี่วินาที")]
        public float tiltDuration = 3f;

        [Header("Credits — แสดงทีละอัน ต่อเนื่องกัน")]
        [Tooltip("เพิ่ม element ได้เรื่อยๆ — แสดงตามลำดับ fade in → hold → fade out\n" +
                 "ทิ้งว่างถ้า shot นี้ไม่มี credit")]
        public CreditEntry[] credits;
    }

    // ─────────────────────────── Inspector ───────────────────────────

    [Header("Debug")]
    [Tooltip("✓ = ข้าม cutscene ทั้งหมด → ไปเริ่มที่ \"นั่งอยู่ในรถที่จอดหน้าศาลาแล้ว\" ทันที\n" +
             "ยังต้องกด E ลงจากรถเหมือนเดิม เพราะช่วงนั้นคือ gameplay ไม่ใช่ cutscene")]
    [SerializeField] private bool disableCutscene = false;

    [Header("Cameras")]
    [Tooltip("GameObject ที่มี Camera + CinemachineBrain (inactive ใน Editor)")]
    [SerializeField] private GameObject cutsceneCameraGO;

    [Tooltip("CinemachineBrain บน CutsceneCameraGO — ใช้ควบคุม blend style")]
    [SerializeField] private CinemachineBrain cinemachineBrain;

    [Tooltip("Camera ของผู้เล่น (บน cameraPivot) — ปิดระหว่าง cutscene")]
    [SerializeField] private Camera playerCamera;

    [Header("Shots — เรียงตามลำดับที่อยากให้เล่น")]
    [Tooltip("แต่ละ shot มี CinemachineCamera + วินาทีที่จะตัดมา\n" +
             "วาง CinemachineCamera GameObject ไว้ใน scene แล้วปิด GameObject ไว้")]
    [SerializeField] private CutsceneShot[] shots;

    [Header("รถที่พาผู้เล่นมาถึง")]
    [Tooltip("InputHandler — ต้องรับปุ่ม E เองตอนผู้เล่นนั่งอยู่ในรถ\n" +
             "เบาะรถไม่มี SittingPoint (ตัวนั้นเป็นของม้านั่งศาลา) ถ้าไม่ใส่จะลุกไม่ได้เลย")]
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("รถที่ผู้เล่นนั่งมา — ใช้ component Rule6Bus ตัวเดียวกับรถของกฎข้อ 6 เพื่อให้ได้\n" +
             "ช่อง Seat / Sit Pose พร้อม gizmo ท่านั่งมาด้วย (จะได้ไม่ต้องเดาว่าตัวจะลอยไหม)\n\n" +
             "ต้องเป็นคันของตัวเอง ห้ามใช้คันเดียวกับที่อยู่ในช่อง Buses ของกฎ 6")]
    [SerializeField] private Rule6.Rule6Bus introBus;

    [Tooltip("จุดที่รถเริ่มวิ่งเข้ามา — ใช้จุดเดียวกับ Approach ของกฎข้อ 6 ได้เลย")]
    [SerializeField] private Transform busStartPoint;

    [Tooltip("จุดจอดหน้าศาลา — ต้องเป็นจุดเดียวกับ Stop ของกฎข้อ 6\n" +
             "เพราะบทจบวนลูปของกฎ 6 คือฉากนี้ซ้ำ ถ้าคนละจุดมุกจะพัง")]
    [SerializeField] private Transform busStopPoint;

    [Tooltip("จุดที่รถวิ่งหายไปหลังผู้เล่นลงแล้ว — ใช้จุดเดียวกับ Depart ของกฎข้อ 6 ได้เลย")]
    [SerializeField] private Transform busDepartPoint;

    [Tooltip("รถวิ่งจาก Start มาจอดที่ Stop นานเท่านี้ (วินาที) — ช้าๆ ได้ ช่วงนี้คือทั้งฉากเปิด\n" +
             "เวลาของ shot ทุกตัวนับอยู่ในช่วงนี้")]
    [SerializeField] private float arriveSeconds = 14f;

    [Tooltip("นิ่งอยู่กับกล้องหนังอีกเท่านี้หลังรถจอดสนิท ก่อนตัดไปเป็นมุมมองผู้เล่น (วินาที)")]
    [SerializeField] private float holdAtStop = 1f;

    [Tooltip("รถค่อยๆ ขับออกไปนานเท่านี้ (วินาที) — เกิดหลังผู้เล่นลงจากรถแล้ว")]
    [SerializeField] private float departSeconds = 4f;

    [Tooltip("ข้อความ hint ตอนนั่งอยู่ในรถที่จอดแล้ว")]
    [SerializeField] private string getOffHint = "E   ลงจากรถ";

    [Tooltip("ยืนห่างจากเบาะเท่านี้ (เมตร) = ลงจากรถแล้ว รถจึงออกได้")]
    [SerializeField] private float offBusDistance = 4.5f;

    [Tooltip("หลังผู้เล่นลงจากรถแล้ว รออีกเท่านี้รถจึงค่อยๆ ขับออกไป (วินาที)")]
    [SerializeField] private float busDepartDelay = 2f;

    [Header("Blend")]
    [Tooltip("เวลา blend ระหว่าง shot (วินาที) — 0 = ตัดทันที")]
    [SerializeField] private float blendTime = 0.8f;

    [Header("Cinematic Bars")]
    [Tooltip("RectTransform ของแถบดำบน — Anchor: Top, Pivot: Top")]
    [SerializeField] private RectTransform topBar;

    [Tooltip("RectTransform ของแถบดำล่าง — Anchor: Bottom, Pivot: Bottom")]
    [SerializeField] private RectTransform bottomBar;

    [Tooltip("ความสูงของแถบ (pixels) — ประมาณ 80–120 ดูหนังมาตรฐาน")]
    [SerializeField] private float barHeight = 90f;

    [Tooltip("เวลา slide เข้า/ออก (วินาที)")]
    [SerializeField] private float barAnimDuration = 0.6f;

    [Header("Credits UI")]
    [Tooltip("CanvasGroup บน Panel ที่ครอบ Text ทั้งสอง — ใช้ควบคุม alpha")]
    [SerializeField] private CanvasGroup creditGroup;

    [Tooltip("TextMeshProUGUI บรรทัดบน (ชื่อค่ายพัฒนา)")]
    [SerializeField] private TextMeshProUGUI creditTextTop;

    [Tooltip("TextMeshProUGUI บรรทัดล่าง (ชื่อนักพัฒนา)")]
    [SerializeField] private TextMeshProUGUI creditTextBottom;

    [Header("Cutscene Music")]
    [Tooltip("FMOD Event path เพลงที่เล่นตลอด cutscene\nเว้นว่างถ้าไม่ต้องการ")]
    [SerializeField] private string cutsceneMusicEvent = "";

    [Header("Ending Dialogue")]
    [Tooltip("บทพูดผู้เล่นตอนตัดกล้องมาเป็นมุมมองตัวเอง — นั่งอยู่ในรถที่จอดแล้ว ก่อนลงจากรถ\n" +
             "ต้องมี PlayerDialogueUI ใน scene ด้วย")]
    [SerializeField] private DialogueLine[] endingDialogue;

    [Tooltip("หน่วงก่อนแสดงบทพูด (วินาที) — ให้ผู้เล่นได้มองรอบตัวในรถก่อนสักพัก")]
    [SerializeField] private float endingDialogueDelay = 0.6f;

    [Header("Phone Hint (2 สเต็ป)")]
    [Tooltip("สเต็ป 1 — แสดงก่อนผู้เล่นเปิดโทรศัพท์")]
    [SerializeField] private string phoneHintBeforeOpen = "TAB  หยิบโทรศัพท์";
    [Tooltip("สเต็ป 2 — สลับมาแสดงอัตโนมัติหลังผู้เล่นกด TAB เปิดโทรศัพท์ (รองรับ \\n)")]
    [TextArea(2, 4)]
    [SerializeField] private string phoneHintAfterOpen = "1      เปิดแชท\n2      ไฟฉาย";

    [Header("Skip")]
    [Tooltip("กด Spacebar เพื่อข้าม intro cutscene — รถจะกระโดดไปจอดที่ป้ายทันที\n" +
             "(ยังต้องกด E ลงจากรถเหมือนเดิม ส่วนนั้นเป็น gameplay แล้ว)")]
    [SerializeField] private bool allowSkip = true;

    [Tooltip("ข้อความบอกปุ่ม skip ที่ต้องซ่อนเมื่อจบ cutscene หรือกด skip")]
    [SerializeField] private GameObject skipText;

    // ─────────────────────────── Runtime ───────────────────────────

    private bool      _playing;
    private bool      _skipRequested;
    private Transform _playerTransform;
    private int       _currentShotIndex = -1;
    private Coroutine _tiltCoroutine;
    private Coroutine _trackCoroutine;
    private Coroutine _barCoroutine;
    private Coroutine _creditCoroutine;
    private Coroutine _creditFadeCoroutine;
    private Coroutine _shotCoroutine;
    private EventInstance _cutsceneMusicInstance;

    /// <summary>
    /// ตัวขับรถ + ตัวพาผู้เล่นไปกับรถ — ตัวเดียวกับที่กฎข้อ 6 ใช้
    ///
    /// ฉากเปิดนี้กับบทจบวนลูปของกฎ 6 เป็นฉากเดียวกัน (รถพาเรามาจอดที่ศาลาตอน 18:40 แล้วเรา
    /// ลงจากรถ) เลยต้องเป็นโค้ดชุดเดียวกัน ไม่ใช่สองชุดที่พยายามทำให้เหมือนกัน
    /// </summary>
    private readonly Rule6.BusRide _ride = new Rule6.BusRide();

    // ─────────────────────────── Lifecycle ───────────────────────────

    // ตั้งใน Awake ไม่ใช่ Start — Awake ของทุกตัวรันก่อน Start ของทุกตัว ตัวที่เช็กธงนี้ใน Start
    // หรือ Update เฟรมแรกจะได้เห็นค่าที่ถูกต้องแน่นอน ไม่ขึ้นกับลำดับ script
    private void Awake()
    {
        instance = this;
        SetPlaying(true);
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        instance = null;
        SetPlaying(false);   // โหลดซีนใหม่ระหว่างฉากเปิด ธง static ต้องไม่ค้าง
    }

    private void Start()
    {
        _playerTransform = PlayerController.Instance.transform;

        if (disableCutscene)
        {
            StartCoroutine(SkipToGameplayRoutine());
            return;
        }

        StartCutscene();
    }

    private void Update()
    {
        if (_playing && allowSkip && IsSkipKeyPressedThisFrame())
            RequestSkip();
    }

    // ─────────────────────────── Skip Mode ───────────────────────────

    /// <summary>
    /// กด Spacebar = ข้ามช่วงกล้องหนัง
    ///
    /// ไม่ teleport ผู้เล่นเองแล้ว — ผู้เล่นนั่งอยู่ในรถ จึงย้ายรถไปจุดจอดแล้วให้ตัวพาผู้เล่น
    /// ลากตามไป (BusRide.Drive เห็นธงนี้แล้วกระโดดไปปลายทางพร้อม Step() ให้เลย)
    /// ถ้า teleport ผู้เล่นแยกจากรถ ผู้เล่นจะไปยืนอยู่กลางถนนและรถจะวิ่งทับผ่านไป
    /// </summary>
    private void RequestSkip()
    {
        if (_skipRequested) return;

        _skipRequested = true;
        HideCreditsImmediate(stopCoroutines: true);
        HideSkipTextImmediate();
    }

    private bool IsSkipKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Space))
            return true;
#endif

        return false;
    }

    /// <summary>
    /// ทางลัดตอนติ๊ก Disable Cutscene — เริ่มที่สภาพ "รถจอดหน้าศาลาแล้ว เรานั่งอยู่ในนั้น"
    ///
    /// ไม่ใช่การข้ามไปนั่งที่ม้านั่งศาลาเหมือนเวอร์ชันก่อน เพราะช่วงลงจากรถเป็น gameplay
    /// ไม่ใช่ cutscene — ถ้าข้ามส่วนนั้นไปด้วย เวลาเทสกฎข้ออื่นสภาพเริ่มเกมจะไม่เหมือนจริง
    /// </summary>
    private IEnumerator SkipToGameplayRoutine()
    {
        yield return null; // รอ 1 frame ให้ singleton พร้อม

        PlayerController.Instance.SetMovement(false);
        PlayerController.Instance.SetLook(false);
        TimeManager.instance.IsPauseTime(true);
        PlayerController.Instance.isBlockStanding = true;

        HideSkipTextImmediate();

        if (cutsceneCameraGO != null) cutsceneCameraGO.SetActive(false);
        if (playerCamera     != null) playerCamera.enabled = true;

        yield return BoardBusAt(busStopPoint);
        yield return HandOverRoutine(playDialogue: false);
    }

    // ─────────────────────────── Start Cutscene ───────────────────────────

    private void StartCutscene()
    {
        _playing       = true;
        _skipRequested = false;

        PlayerController.Instance.SetMovement(false);
        PlayerController.Instance.SetLook(false);
        PlayerController.Instance.isBlockStanding = true;
        TimeManager.instance.IsPauseTime(true);

        // เปิด cutscene camera ก่อนทำอย่างอื่น — ช่วงถัดไปมีการ teleport ผู้เล่นเข้าไปในรถ
        // ซึ่งต้องไม่ถูกเห็นผ่านกล้องของผู้เล่นเอง
        if (playerCamera     != null) playerCamera.enabled = false;
        if (cutsceneCameraGO != null) cutsceneCameraGO.SetActive(true);

        // เริ่มเสียง cutscene
        if (!string.IsNullOrWhiteSpace(cutsceneMusicEvent))
        {
            _cutsceneMusicInstance = RuntimeManager.CreateInstance(cutsceneMusicEvent);
            _cutsceneMusicInstance.start();
        }

        // ซ่อน credit ตั้งแต่ต้น
        if (creditGroup != null) creditGroup.alpha = 0f;

        // slide bars เข้า
        SetBarsImmediate(0f);
        _barCoroutine = StartCoroutine(AnimateBars(0f, barHeight, barAnimDuration));

        WarnIfShotsUntimed();

        StartCoroutine(IntroRoutine());
    }

    /// <summary>
    /// เตือนถ้า shot ทุกตัวยังอยู่ที่วินาที 0
    ///
    /// มีเพราะช่องนี้เคยเป็น "เปิดตอนถึง waypoint ที่เท่าไหร่" (int) แล้วเปลี่ยนเป็นวินาที (float)
    /// ตอนฉากเปิดเปลี่ยนจากเดินมาเป็นนั่งรถมา — Unity แปลงค่าเก่าให้ไม่ได้ ของเดิมจึงกลายเป็น
    /// 0 ทั้งหมด ซึ่งอ่านเป็น "ตัดมา shot สุดท้ายตั้งแต่วินาทีแรก" แล้ว shot อื่นจะไม่ได้เล่นเลย
    /// อาการนี้เหมือนกล้องพังมากกว่าเหมือนค่าที่หายไป เลยต้องมีบรรทัดบอก
    /// </summary>
    private void WarnIfShotsUntimed()
    {
        if (shots == null || shots.Length < 2) return;

        foreach (var shot in shots)
            if (shot.vcam != null && shot.activateAtSecond > 0f) return;

        Debug.LogWarning($"[Cutscene] shot ทั้ง {shots.Length} ตัวตั้งไว้ที่วินาที 0 — จะได้เห็นแค่ตัว" +
                         "ล่างสุดในลิสต์ตัวเดียวทั้งฉาก ใส่ Activate At Second ของแต่ละตัวให้กระจาย " +
                         $"อยู่ในช่วง 0 ถึง {arriveSeconds:0.#} วินาที (ช่องนี้เคยเป็นเลข waypoint " +
                         "ตอนฉากเปิดยังเป็นการเดิน ค่าเก่าจึงใช้ต่อไม่ได้)", this);
    }

    // ─────────────────────────── Intro Ride ───────────────────────────

    /// <summary>
    /// วางรถที่จุดที่กำหนด แล้วพาผู้เล่นไปนั่งในรถคันนั้น
    ///
    /// ต้อง teleport ผู้เล่นไปที่ท่านั่งก่อนเรียกให้นั่ง เพราะ SitRoutine lerp จากตำแหน่งเดิม
    /// ไปที่เบาะใน 0.7 วินาที — ถ้าผู้เล่นเกิดที่จุด spawn เดิม เขาจะไถลข้ามแผนที่เข้ามา
    /// </summary>
    private IEnumerator BoardBusAt(Transform point)
    {
        if (introBus == null || point == null)
        {
            Debug.LogError("[Cutscene] ยังไม่ได้ใส่ Intro Bus หรือจุดของเส้นทาง — " +
                           "ฉากเปิดจะไม่มีรถ ผู้เล่นจะเริ่มเกมที่จุด spawn เฉยๆ " +
                           "(รัน Rule of Horror ▸ Rule 6 ▸ Build Bus Stop เพื่อสร้างและต่อให้)", this);
            yield break;
        }

        introBus.transform.SetPositionAndRotation(point.position, point.rotation);
        introBus.Appear();

        TeleportPlayer(introBus.SitPose);

        bool failed = false;
        yield return _ride.Sit(introBus, 0f, this, onFailed: () => failed = true);

        // พาไปนั่งไม่สำเร็จแล้วยังพาไปกับรถ = ผู้เล่นจะยืนลอยไปตามรถทั้งคัน ปล่อยให้ยืนอยู่
        // กับที่ดีกว่า แล้วรถจะวิ่งผ่านตัวไปจอด (อ่านเป็นบั๊กเหมือนกัน แต่หาต้นเหตุได้จาก log)
        if (!failed) _ride.BeginCarry(introBus.transform);
    }

    /// <summary>
    /// ช่วงกล้องหนัง: รถวิ่งจากปลายถนนเข้ามาจอดที่ศาลา พร้อมผู้เล่นที่นั่งอยู่ในนั้น
    ///
    /// decelerate = true เพราะนี่คือรถที่กำลัง "เข้าจอด" ไม่ใช่รถที่วิ่งผ่าน — ต้องช้าลง
    /// ท้ายทางจนหยุดนิ่ง ไม่ใช่ถึงจุดแล้วดับไปเฉยๆ
    /// </summary>
    private IEnumerator IntroRoutine()
    {
        // เปิด shot แรกก่อนพาผู้เล่นขึ้นรถ — ระหว่างอนิเมชั่นนั่ง (0.7 วิ) ถ้ายังไม่มี vcam ตัวไหน
        // เปิดอยู่ CinemachineBrain จะไม่มีกล้องให้คุม แล้วเฟรมแรกของเกมจะเป็นจอว่าง
        ActivateShotForTime(0f);

        yield return BoardBusAt(busStartPoint);

        _shotCoroutine = StartCoroutine(ShotScheduleRoutine());

        yield return _ride.Drive(introBus != null ? introBus.transform : null,
                                 busStartPoint, busStopPoint,
                                 arriveSeconds, decelerate: true, carryPlayer: true,
                                 stopEarly: () => _skipRequested);

        if (!_skipRequested && holdAtStop > 0f)
            yield return new WaitForSeconds(holdAtStop);

        yield return HandOverRoutine(playDialogue: true);
    }

    /// <summary>
    /// ตัด shot ตามเวลา — เดินนาฬิกาของตัวเองขนานไปกับรถที่กำลังวิ่ง
    ///
    /// เช็กทุกเฟรมแทนที่จะไล่ตามลำดับในลิสต์ เพื่อให้ไม่ต้องเรียง shots เองใน Inspector
    /// (ลิสต์ที่ต้องเรียงเองคือลิสต์ที่จะไม่เรียงในวันที่เพิ่ม shot ทีหลัง)
    /// </summary>
    private IEnumerator ShotScheduleRoutine()
    {
        float elapsed = 0f;

        while (true)
        {
            ActivateShotForTime(elapsed);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ─────────────────────────── Shot Control ───────────────────────────

    /// <summary>เปิด shot ที่ถึงคิวแล้วและคิวช้าสุด — ตัวเดิมอยู่แล้วก็ไม่ทำอะไร</summary>
    private void ActivateShotForTime(float seconds)
    {
        if (shots == null) return;

        int   best   = -1;
        float bestAt = float.NegativeInfinity;

        for (int i = 0; i < shots.Length; i++)
        {
            var shot = shots[i];
            if (shot.vcam == null || shot.activateAtSecond > seconds) continue;

            // >= เพื่อให้ตัวที่อยู่ล่างกว่าในลิสต์ชนะเมื่อใส่เวลาซ้ำกัน
            if (shot.activateAtSecond >= bestAt) { bestAt = shot.activateAtSecond; best = i; }
        }

        if (best >= 0) ActivateShot(best);
    }

    private void ActivateShot(int index)
    {
        if (index == _currentShotIndex) return;

        // ปิด shot เก่า
        if (_currentShotIndex >= 0 && _currentShotIndex < shots.Length)
        {
            var prev = shots[_currentShotIndex];
            if (prev.vcam != null) prev.vcam.gameObject.SetActive(false);
        }

        // หยุด coroutine เก่า
        if (_tiltCoroutine   != null) { StopCoroutine(_tiltCoroutine);   _tiltCoroutine   = null; }
        if (_trackCoroutine  != null) { StopCoroutine(_trackCoroutine);  _trackCoroutine  = null; }
        if (_creditCoroutine != null) { StopCoroutine(_creditCoroutine); _creditCoroutine = null; }
        if (_creditFadeCoroutine != null) { StopCoroutine(_creditFadeCoroutine); _creditFadeCoroutine = null; }

        // ซ่อน credit ทันทีเมื่อเปลี่ยน shot
        HideCreditsImmediate(stopCoroutines: false);

        _currentShotIndex = index;
        var current = shots[index];
        if (current.vcam == null) return;

        // ตั้ง blend style ก่อน activate
        if (cinemachineBrain != null)
        {
            cinemachineBrain.DefaultBlend = current.instantCut
                ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
                : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendTime);
        }

        // เปิด shot ใหม่
        current.vcam.gameObject.SetActive(true);

        // เริ่ม track player (หันมอง ไม่ขยับ)
        if (current.trackPlayer)
            _trackCoroutine = StartCoroutine(TrackPlayerRoutine(current));

        // เริ่ม tilt ถ้า shot นี้ต้องการ
        if (current.doTiltUp)
            _tiltCoroutine = StartCoroutine(TiltRoutine(current));

        // เริ่ม credit ถ้ามีรายการ
        if (current.credits != null && current.credits.Length > 0 && creditGroup != null)
            _creditCoroutine = StartCoroutine(CreditRoutine(current));
    }

    // ─────────────────────────── Credit Coroutine ───────────────────────────

    /// <summary>วนแสดง credit ทีละ entry: fade in → hold → fade out → entry ถัดไป</summary>
    private IEnumerator CreditRoutine(CutsceneShot shot)
    {
        if (creditGroup == null || shot.credits == null) yield break;

        foreach (var entry in shot.credits)
        {
            if (_skipRequested) break;

            // ── ตั้งข้อความ ──
            // ชื่อคนอยู่ในตารางแปลเป็นตัวเดิมทั้งสองภาษา — แปลเฉพาะตำแหน่ง/ชื่อทีม
            if (creditTextTop    != null) creditTextTop.text    = L10n.T(entry.line1);
            if (creditTextBottom != null) creditTextBottom.text = L10n.T(entry.line2);

            // ซ่อน bottom text ถ้าว่าง
            if (creditTextBottom != null)
                creditTextBottom.gameObject.SetActive(!string.IsNullOrWhiteSpace(entry.line2));

            // ── Fade In ──
            yield return StartCreditFade(0f, 1f, entry.fadeInSpeed);

            // ── Hold ──
            float elapsed = 0f;
            while (elapsed < entry.holdDuration && !_skipRequested)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // ── Fade Out ──
            yield return StartCreditFade(1f, 0f, entry.fadeOutSpeed);
        }

        _creditCoroutine = null;
    }

    private IEnumerator StartCreditFade(float from, float to, float speed)
    {
        if (_creditFadeCoroutine != null)
            StopCoroutine(_creditFadeCoroutine);

        _creditFadeCoroutine = StartCoroutine(FadeCreditGroup(from, to, speed));
        yield return _creditFadeCoroutine;
        _creditFadeCoroutine = null;
    }

    /// <summary>
    /// Fade CanvasGroup alpha จาก from → to โดยใช้ speed (alpha/วินาที)
    /// speed สูง = เร็ว | speed ต่ำ = ช้า | speed 0 = ทันที
    /// </summary>
    private IEnumerator FadeCreditGroup(float from, float to, float speed)
    {
        if (creditGroup == null) yield break;

        if (speed <= 0f) { creditGroup.alpha = to; yield break; }

        float duration = 1f / speed;   // แปลง speed → duration
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed           += Time.deltaTime;
            creditGroup.alpha  = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }

        creditGroup.alpha = to;
    }

    // ─────────────────────────── Tilt Coroutine ───────────────────────────

    private IEnumerator TiltRoutine(CutsceneShot shot)
    {
        if (shot.vcam == null) yield break;

        Transform camTransform = shot.vcam.transform;
        float elapsed = 0f;

        Vector3 startEuler = camTransform.eulerAngles;
        // ตั้งมุมเริ่มต้น X
        startEuler.x = shot.tiltStartX;
        camTransform.eulerAngles = startEuler;

        while (elapsed < shot.tiltDuration && !_skipRequested)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / shot.tiltDuration);

            Vector3 euler   = camTransform.eulerAngles;
            euler.x         = Mathf.Lerp(shot.tiltStartX, shot.tiltEndX, t);
            camTransform.eulerAngles = euler;

            yield return null;
        }

        if (!_skipRequested)
        {
            Vector3 euler = camTransform.eulerAngles;
            euler.x       = shot.tiltEndX;
            camTransform.eulerAngles = euler;
        }
    }

    // ─────────────────────────── Track Player Coroutine ───────────────────────────

    /// <summary>หมุนกล้องตามผู้เล่นช้าๆ โดยไม่ขยับตำแหน่ง</summary>
    private IEnumerator TrackPlayerRoutine(CutsceneShot shot)
    {
        if (shot.vcam == null) yield break;

        Transform camTransform = shot.vcam.transform;

        while (!_skipRequested)
        {
            Vector3 lookTarget = _playerTransform.position + Vector3.up * shot.trackHeightOffset;
            Vector3 dir        = lookTarget - camTransform.position;

            if (dir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                camTransform.rotation = Quaternion.Slerp(
                    camTransform.rotation,
                    targetRot,
                    shot.trackSpeed * Time.deltaTime);
            }

            yield return null;
        }
    }

    // ─────────────────────────── Hand Over ───────────────────────────

    /// <summary>
    /// รถจอดแล้ว → ส่งเกมคืนให้ผู้เล่น
    ///
    /// ลำดับของการ "ตัดกล้อง" คือสลับกล้องก่อน แล้วค่อยเก็บแถบดำ ไม่ใช่กลับกัน — วินาทีที่
    /// เปลี่ยนจากหนังเป็นเกมคือวินาทีที่มุมกล้องเปลี่ยน ถ้าเก็บแถบก่อนผู้เล่นจะได้ดูกล้องหนัง
    /// แบบเต็มจอค้างอยู่ 0.6 วินาที ซึ่งอ่านเป็นฉากจบแล้วค้าง
    ///
    /// ปล่อยนาฬิกาตอนผู้เล่นลงจากรถเสร็จ ไม่ใช่ตอนรถขับออกไปหมด: รถออกไปเป็นฉากหลังให้
    /// ผู้เล่นดูเล่นๆ ถ้าไปรอมันก่อนจะปล่อยเวลา ผู้เล่นจะคุมตัวเองได้แต่เวลาค้างอยู่เฉยๆ
    /// 4 วินาที แล้วข้อความแรกจะมาช้ากว่าที่ควร
    /// </summary>
    private IEnumerator HandOverRoutine(bool playDialogue)
    {
        _playing = false;
        HideSkipTextImmediate();

        // หยุด coroutine ของช่วงหนังทั้งหมด
        if (_tiltCoroutine   != null) { StopCoroutine(_tiltCoroutine);   _tiltCoroutine   = null; }
        if (_trackCoroutine  != null) { StopCoroutine(_trackCoroutine);  _trackCoroutine  = null; }
        if (_barCoroutine    != null) { StopCoroutine(_barCoroutine);    _barCoroutine    = null; }
        if (_shotCoroutine   != null) { StopCoroutine(_shotCoroutine);   _shotCoroutine   = null; }
        if (_creditCoroutine != null) { StopCoroutine(_creditCoroutine); _creditCoroutine = null; }
        if (_creditFadeCoroutine != null) { StopCoroutine(_creditFadeCoroutine); _creditFadeCoroutine = null; }

        StopCutsceneMusic();
        HideCreditsImmediate(stopCoroutines: false);

        // ── ตัดกล้องมาเป็นมุมมองผู้เล่นที่นั่งอยู่ในรถ ──
        if (shots != null)
            foreach (var shot in shots)
                if (shot.vcam != null) shot.vcam.gameObject.SetActive(false);

        if (cutsceneCameraGO != null) cutsceneCameraGO.SetActive(false);
        if (playerCamera     != null) playerCamera.enabled = true;

        // หันหน้าได้แล้ว (ยังลุกไม่ได้ — isBlockStanding ปลดใน LeaveBus ตอนขึ้น hint)
        PlayerController.Instance.SetLook(true);

        // ฉากเปิดจบที่วินาทีนี้ ไม่ใช่ตอนรถขับออกไป — ตัวนี้คือธงที่ UI อย่าง key prompt ใช้
        // เช็กว่า "ยังดูหนังอยู่ไหม" ถ้าค้างไว้จนรถออก ปุ่มที่ต้องกดจะถูกซ่อนตอนที่ต้องกดจริง
        SetPlaying(false);

        yield return StartCoroutine(AnimateBars(CurrentBarHeight(), 0f, barAnimDuration));

        // ── บทพูดตอนนั่งอยู่ในรถที่จอดแล้ว ──
        if (playDialogue && endingDialogue != null && endingDialogue.Length > 0 &&
            PlayerDialogueUI.instance != null)
        {
            yield return new WaitForSeconds(endingDialogueDelay);

            bool dialogueDone = false;
            Action onDone = () => dialogueDone = true;
            PlayerDialogueUI.instance.OnSequenceComplete += onDone;
            PlayerDialogueUI.instance.ShowSequence(endingDialogue);
            yield return new WaitUntil(() => dialogueDone);
            PlayerDialogueUI.instance.OnSequenceComplete -= onDone;
        }

        // ── กด E ลุกจากเบาะ แล้วเดินลงจากรถ ──
        if (introBus != null)
            yield return _ride.LeaveBus(introBus, inputHandler, getOffHint,
                                        offBusDistance, busDepartDelay);
        else
            PlayerController.Instance.isBlockStanding = false;

        // ── คืนเกมให้ผู้เล่นเต็มตัว ──
        // แสดง hint โทรศัพท์ — ซ่อนเองเมื่อผู้เล่นกดปุ่มแชท หรือเมื่อ Rule1 เริ่ม
        if (GameHintUI.instance != null)
            GameHintUI.instance.ShowPhoneOnboarding(phoneHintBeforeOpen, phoneHintAfterOpen);

        TimeManager.instance.IsPauseTime(false);

        // ── รถค่อยๆ ขับออกไป ──
        // ห้ามปิด GameObject ก่อนบรรทัดนี้จบ — coroutine ของเราตายไปพร้อมกับมัน แล้วรถจะ
        // ค้างอยู่หน้าศาลาตลอดคืน บังป้ายรถที่กฎข้อ 6 ต้องใช้ตอน 24:00
        if (introBus != null && busStopPoint != null && busDepartPoint != null)
        {
            yield return _ride.Drive(introBus.transform, busStopPoint, busDepartPoint,
                                     departSeconds, decelerate: false);
            introBus.Hide();
        }

        gameObject.SetActive(false);
    }

    // ─────────────────────────── Helper ───────────────────────────

    /// <summary>ความสูงแถบดำตอนนี้ — เก็บแถบจากค่าจริง ไม่ใช่จาก barHeight ที่อาจยังไม่ถึง</summary>
    private float CurrentBarHeight() => topBar != null ? topBar.sizeDelta.y : barHeight;

    /// <summary>หยุดและ release เพลง cutscene (เรียกซ้ำได้)</summary>
    private void StopCutsceneMusic()
    {
        if (!_cutsceneMusicInstance.isValid()) return;

        _cutsceneMusicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _cutsceneMusicInstance.release();
        _cutsceneMusicInstance = default;
    }

    private void HideCreditsImmediate(bool stopCoroutines)
    {
        if (stopCoroutines)
        {
            if (_creditCoroutine != null) { StopCoroutine(_creditCoroutine); _creditCoroutine = null; }
            if (_creditFadeCoroutine != null) { StopCoroutine(_creditFadeCoroutine); _creditFadeCoroutine = null; }
        }

        if (creditGroup != null)
        {
            creditGroup.alpha = 0f;
            creditGroup.interactable = false;
            creditGroup.blocksRaycasts = false;
        }

        if (creditTextTop != null) creditTextTop.text = "";
        if (creditTextBottom != null) creditTextBottom.text = "";
    }

    private void HideSkipTextImmediate()
    {
        if (skipText != null)
            skipText.SetActive(false);
    }

    private void TeleportPlayer(Transform target)
    {
        if (target == null) return;

        var cc = PlayerController.Instance.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _playerTransform.SetPositionAndRotation(target.position, target.rotation);
        if (cc != null) cc.enabled = true;
    }

    // ─────────────────────────── Cinematic Bars ───────────────────────────

    /// <summary>ตั้งความสูง bar ทันทีโดยไม่ animate (ใช้ตอน init)</summary>
    private void SetBarsImmediate(float height)
    {
        if (topBar    != null) topBar.sizeDelta    = new Vector2(topBar.sizeDelta.x,    height);
        if (bottomBar != null) bottomBar.sizeDelta = new Vector2(bottomBar.sizeDelta.x, height);
    }

    /// <summary>Animate bars จาก fromHeight → toHeight</summary>
    private IEnumerator AnimateBars(float fromHeight, float toHeight, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t      = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            float height = Mathf.Lerp(fromHeight, toHeight, t);

            if (topBar    != null) topBar.sizeDelta    = new Vector2(topBar.sizeDelta.x,    height);
            if (bottomBar != null) bottomBar.sizeDelta = new Vector2(bottomBar.sizeDelta.x, height);

            yield return null;
        }

        SetBarsImmediate(toHeight);
    }
}
