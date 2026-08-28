using System;
using System.Collections;
using FMOD.Studio;
using FMODUnity;
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
/// CutsceneManager — Multi-shot intro cutscene
///
/// แนวคิด:
///   ตัวละครเดินตาม waypoints ตามปกติ
///   กล้องเป็น static shots หลายตัว สลับตามตำแหน่ง waypoint ที่ผู้เล่นเดินถึง
///   รองรับ shot แบบ TiltUp (เงยกล้องขึ้นช้าๆ) สำหรับ shot สุดท้าย
///
/// Shot Types:
///   Static   — กล้องนิ่ง ผู้เล่นเดินผ่านเฟรม
///   TiltUp   — กล้องนิ่งแล้วค่อยๆ เงยขึ้น (ไม่ follow player)
///
/// Flow (disableCutscene = false):
///   เดิน waypoints → สลับ shot → จบ → นั่ง → เริ่มเกม
///
/// Flow (disableCutscene = true):
///   teleport → นั่งทันที → เริ่มเกม  (ใช้ตอน test rule อื่น)
/// </summary>
public class CutsceneManager : MonoBehaviour
{
    public static CutsceneManager instance;

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

        [Tooltip("เปิด shot นี้เมื่อผู้เล่นเดินถึง waypoint index นี้\n" +
                 "0 = เปิดตั้งแต่ต้น, 1 = เปิดตอนถึง waypoint[1] เป็นต้น")]
        public int activateAtWaypoint = 0;

        [Tooltip("✓ = ตัดมา shot นี้ทันที (Cut)  ✗ = ค่อยๆ blend ตาม Blend Time ที่ตั้งไว้")]
        public bool instantCut = false;

        [Header("Track Player (หันมองผู้เล่นโดยไม่ขยับกล้อง)")]
        [Tooltip("กล้องจะค่อยๆ หันตามผู้เล่นขณะเดิน (กล้องไม่ขยับ แค่หมุน)")]
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

        [Header("Car — รถวิ่งผ่านระหว่าง shot นี้")]
        [Tooltip("GameObject รถ — วางไว้ใน scene แล้วปิดไว้ก่อน\n" +
                 "script จะเปิดตอน shot นี้เริ่ม และปิดเมื่อเปลี่ยน shot")]
        public GameObject carObject;

        [Tooltip("Waypoints ที่รถจะวิ่งผ่าน (เรียงตามลำดับ)\n" +
                 "รถจะ teleport ไป [0] แล้วเดินไปถึง waypoint สุดท้าย")]
        public Transform[] carWaypoints;

        [Tooltip("ความเร็วรถ (units/วินาที)")]
        public float carSpeed = 20f;

        [Tooltip("✓ = รถหันหน้าตามทิศทางที่วิ่ง")]
        public bool carFaceDirection = true;

        [Tooltip("หมุน Y เพิ่มเติมบน model (องศา) — ปรับตาม pivot ของ model\n" +
                 "ถ้ารถหันผิดทาง ลองเปลี่ยนเป็น 90 หรือ -90 หรือ 180")]
        public float carRotationOffset = -90f;

        [Header("Car Sound — เสียง 3D ติดตามตำแหน่งรถ")]
        [Tooltip("FMOD Event path ของเสียงรถ เช่น event:/SFX/CarEngine\n" +
                 "เว้นว่างถ้าไม่ต้องการเสียง")]
        public string carSoundEvent = "";
    }

    // ─────────────────────────── Inspector ───────────────────────────

    [Header("Debug")]
    [Tooltip("✓ = ข้าม cutscene ทั้งหมด → นั่งที่เก้าอี้ทันที")]
    [SerializeField] private bool disableCutscene = false;

    [Header("Cameras")]
    [Tooltip("GameObject ที่มี Camera + CinemachineBrain (inactive ใน Editor)")]
    [SerializeField] private GameObject cutsceneCameraGO;

    [Tooltip("CinemachineBrain บน CutsceneCameraGO — ใช้ควบคุม blend style")]
    [SerializeField] private CinemachineBrain cinemachineBrain;

    [Tooltip("Camera ของผู้เล่น (บน cameraPivot) — ปิดระหว่าง cutscene")]
    [SerializeField] private Camera playerCamera;

    [Header("Shots — เรียงตามลำดับที่อยากให้เล่น")]
    [Tooltip("แต่ละ shot มี CinemachineCamera + เงื่อนไขการ activate\n" +
             "วาง CinemachineCamera GameObject ไว้ใน scene แล้วปิด GameObject ไว้")]
    [SerializeField] private CutsceneShot[] shots;

    [Header("Path — ตัวละครเดินตาม waypoints")]
    [Tooltip("Waypoint[0] = จุดเริ่มต้น, Waypoint สุดท้าย = หน้าเก้าอี้")]
    [SerializeField] private Transform[] waypoints;

    [Header("Seat")]
    [SerializeField] private Transform seatTarget;

    [Header("Walk")]
    [SerializeField] private float walkSpeed   = 2f;
    [SerializeField] private float rotateSpeed = 8f;

    [Tooltip("หน่วงก่อนนั่ง (วินาที)")]
    [SerializeField] private float pauseBeforeSit = 0.5f;

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
    [Tooltip("บทพูดผู้เล่นที่แสดงหลังนั่งเก้าอี้ตอน cutscene จบ\n" +
             "ต้องมี PlayerDialogueUI ใน scene ด้วย")]
    [SerializeField] private DialogueLine[] endingDialogue;

    [Tooltip("หน่วงก่อนแสดงบทพูด (วินาที) เพื่อให้ animation นั่งเสร็จก่อน")]
    [SerializeField] private float endingDialogueDelay = 0.6f;

    [Header("Phone Hint")]
    [Tooltip("ข้อความ hint ที่แสดงหลัง cutscene จบ บอกผู้เล่นวิธีใช้โทรศัพท์\n" +
             "รองรับ \\n สำหรับขึ้นบรรทัด — เว้นว่างถ้าไม่ต้องการ")]
    [TextArea(2, 5)]
    [SerializeField] private string phoneHintText = "TAB  หยิบโทรศัพท์\n1      เปิดแชท\n2      ไฟฉาย";

    [Header("Skip")]
    [Tooltip("กด Spacebar เพื่อข้าม intro cutscene (ยังต้องนั่งอยู่)")]
    [SerializeField] private bool allowSkip = true;

    [Tooltip("Waypoint index ที่จะ snap ไปเมื่อกด skip เช่น 7 = W_7\n" +
             "ถ้าค่าเกินจำนวน waypoint จะใช้ waypoint สุดท้ายแทน")]
    [SerializeField] private int skipToWaypointIndex = 7;

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
    private Coroutine _carCoroutine;
    private EventInstance _carSoundInstance;
    private EventInstance _cutsceneMusicInstance;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake() => instance = this;

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

    private void RequestSkip()
    {
        if (_skipRequested) return;

        _skipRequested = true;
        HideCreditsImmediate(stopCoroutines: true);
        HideSkipTextImmediate();
        TeleportPlayer(GetSkipWaypoint());
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

    private IEnumerator SkipToGameplayRoutine()
    {
        yield return null; // รอ 1 frame ให้ singleton พร้อม

        PlayerController.Instance.SetMovement(false);
        PlayerController.Instance.SetLook(false);
        TimeManager.instance.IsPauseTime(true);

        HideSkipTextImmediate();

        if (cutsceneCameraGO != null) cutsceneCameraGO.SetActive(false);
        if (playerCamera     != null) playerCamera.enabled = true;

        if (seatTarget != null)
        {
            TeleportPlayer(seatTarget);
            PlayerController.Instance.StartSittingSequence(seatTarget);
        }

        yield return new WaitUntil(() => PlayerController.Instance.IsSitting());
        yield return new WaitForSeconds(0.3f);

        PlayerController.Instance.SetLook(true);
        TimeManager.instance.IsPauseTime(false);
        gameObject.SetActive(false);
    }

    // ─────────────────────────── Start Cutscene ───────────────────────────

    private void StartCutscene()
    {
        _playing       = true;
        _skipRequested = false;

        PlayerController.Instance.SetMovement(false);
        PlayerController.Instance.SetLook(false);
        TimeManager.instance.IsPauseTime(true);

        // teleport ผู้เล่นไป waypoint[0] ก่อน
        if (waypoints != null && waypoints.Length > 0)
            TeleportPlayer(waypoints[0]);

        // เปิด cutscene camera ระบบ
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

        // เปิด shot แรก (activateAtWaypoint == 0)
        ActivateShotForWaypoint(0);

        StartCoroutine(WalkRoutine());
    }

    // ─────────────────────────── Shot Control ───────────────────────────

    /// <summary>เปิด shot ที่ activateAtWaypoint ตรงกับ waypointIndex — ปิดของเก่า</summary>
    private void ActivateShotForWaypoint(int waypointIndex)
    {
        for (int i = 0; i < shots.Length; i++)
        {
            var shot = shots[i];
            if (shot.vcam == null) continue;

            if (shot.activateAtWaypoint == waypointIndex)
            {
                ActivateShot(i);
                return;
            }
        }
    }

    private void ActivateShot(int index)
    {
        if (index == _currentShotIndex) return;

        // ปิด shot เก่า + ซ่อนรถของ shot เก่า
        if (_currentShotIndex >= 0 && _currentShotIndex < shots.Length)
        {
            var prev = shots[_currentShotIndex];
            if (prev.vcam != null) prev.vcam.gameObject.SetActive(false);
            if (prev.carObject != null) prev.carObject.SetActive(false);
        }

        // หยุด coroutine เก่า
        if (_tiltCoroutine   != null) { StopCoroutine(_tiltCoroutine);   _tiltCoroutine   = null; }
        if (_trackCoroutine  != null) { StopCoroutine(_trackCoroutine);  _trackCoroutine  = null; }
        if (_creditCoroutine != null) { StopCoroutine(_creditCoroutine); _creditCoroutine = null; }
        if (_creditFadeCoroutine != null) { StopCoroutine(_creditFadeCoroutine); _creditFadeCoroutine = null; }
        if (_carCoroutine    != null) { StopCoroutine(_carCoroutine);    _carCoroutine    = null; }
        StopCarSound();
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

        // เริ่มรถถ้า shot นี้มีรถ
        if (current.carObject != null && current.carWaypoints != null && current.carWaypoints.Length >= 2)
            _carCoroutine = StartCoroutine(CarRoutine(current));
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
            if (creditTextTop    != null) creditTextTop.text    = entry.line1;
            if (creditTextBottom != null) creditTextBottom.text = entry.line2;

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

    // ─────────────────────────── Car Coroutine ───────────────────────────

    /// <summary>
    /// teleport รถไป waypoint[0] แล้ววิ่งไปตาม waypoints ด้วยความเร็ว carSpeed
    /// เมื่อถึงปลายทางแล้ว ซ่อนรถทันที
    /// </summary>
    private IEnumerator CarRoutine(CutsceneShot shot)
    {
        if (shot.carObject == null || shot.carWaypoints == null || shot.carWaypoints.Length < 2)
            yield break;

        Transform carT = shot.carObject.transform;

        // teleport ไป waypoint แรก
        carT.position = shot.carWaypoints[0].position;
        carT.rotation = shot.carWaypoints[0].rotation;

        shot.carObject.SetActive(true);

        // ── เริ่มเสียง 3D ──
        bool hasSound = !string.IsNullOrWhiteSpace(shot.carSoundEvent);
        if (hasSound)
        {
            _carSoundInstance = RuntimeManager.CreateInstance(shot.carSoundEvent);
            // Attach กับ transform รถ → FMOD อัปเดต 3D position/velocity อัตโนมัติทุก frame
            RuntimeManager.AttachInstanceToGameObject(_carSoundInstance, carT);
            _carSoundInstance.start();
        }

        // วิ่งผ่านทุก waypoint
        for (int i = 1; i < shot.carWaypoints.Length; i++)
        {
            Transform target = shot.carWaypoints[i];

            while (Vector3.Distance(carT.position, target.position) > 0.05f)
            {
                carT.position = Vector3.MoveTowards(
                    carT.position, target.position, shot.carSpeed * Time.deltaTime);

                if (shot.carFaceDirection)
                {
                    Vector3 dir = (target.position - carT.position);
                    dir.y = 0f;
                    if (dir != Vector3.zero)
                        carT.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, shot.carRotationOffset, 0f);
                }

                yield return null;
            }

            carT.position = target.position;
        }

        // ถึงปลายทาง → หยุดเสียง + ซ่อนรถ
        StopCarSound();
        shot.carObject.SetActive(false);
        _carCoroutine = null;
    }

    /// <summary>หยุดและ release FMOD instance เสียงรถ (safe to call ซ้ำ)</summary>
    private void StopCarSound()
    {
        if (_carSoundInstance.isValid())
        {
            _carSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _carSoundInstance.release();
            _carSoundInstance = default;
        }
    }

    // ─────────────────────────── Walk Coroutine ───────────────────────────

    private IEnumerator WalkRoutine()
    {
        var cc       = PlayerController.Instance.GetComponent<CharacterController>();
        var animator = PlayerController.Instance.GetComponentInChildren<Animator>();

        if (cc != null) cc.enabled = false;

        // เริ่ม animation เดิน
        if (animator != null) animator.SetBool("walk", true);

        for (int i = 1; i < waypoints.Length; i++)
        {
            if (_skipRequested) break;

            // ตรวจว่ามี shot ที่ต้องเปิดตอนถึง waypoint นี้
            ActivateShotForWaypoint(i);

            Transform wp = waypoints[i];

            while (!_skipRequested &&
                   Vector3.Distance(_playerTransform.position, wp.position) > 0.05f)
            {
                _playerTransform.position = Vector3.MoveTowards(
                    _playerTransform.position, wp.position, walkSpeed * Time.deltaTime);

                Vector3 dir = (wp.position - _playerTransform.position).normalized;
                dir.y = 0f;
                if (dir != Vector3.zero)
                {
                    _playerTransform.rotation = Quaternion.Slerp(
                        _playerTransform.rotation,
                        Quaternion.LookRotation(dir),
                        rotateSpeed * Time.deltaTime);
                }

                yield return null;
            }

            if (!_skipRequested)
                _playerTransform.position = wp.position;
        }

        // หยุด animation เดิน
        if (animator != null) animator.SetBool("walk", false);

        if (cc != null) cc.enabled = true;

        if (!_skipRequested && pauseBeforeSit > 0f)
            yield return new WaitForSeconds(pauseBeforeSit);

        yield return StartCoroutine(FinishRoutine());
    }

    // ─────────────────────────── Finish ───────────────────────────

    private IEnumerator FinishRoutine()
    {
        _playing = false;
        HideSkipTextImmediate();

        // หยุด coroutine ทั้งหมด
        if (_tiltCoroutine   != null) { StopCoroutine(_tiltCoroutine);   _tiltCoroutine   = null; }
        if (_trackCoroutine  != null) { StopCoroutine(_trackCoroutine);  _trackCoroutine  = null; }
        if (_barCoroutine    != null) { StopCoroutine(_barCoroutine);    _barCoroutine    = null; }
        if (_creditCoroutine != null) { StopCoroutine(_creditCoroutine); _creditCoroutine = null; }
        if (_creditFadeCoroutine != null) { StopCoroutine(_creditFadeCoroutine); _creditFadeCoroutine = null; }
        if (_carCoroutine    != null) { StopCoroutine(_carCoroutine);    _carCoroutine    = null; }
        StopCarSound();

        // หยุดเสียง cutscene
        if (_cutsceneMusicInstance.isValid())
        {
            _cutsceneMusicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _cutsceneMusicInstance.release();
            _cutsceneMusicInstance = default;
        }

        // ซ่อนรถและ credit ทันที
        if (_currentShotIndex >= 0 && _currentShotIndex < shots.Length)
        {
            var last = shots[_currentShotIndex];
            if (last.carObject != null) last.carObject.SetActive(false);
        }
        HideCreditsImmediate(stopCoroutines: false);

        // slide bars ออก แล้วรอเสร็จก่อนสลับกล้อง
        yield return StartCoroutine(AnimateBars(barHeight, 0f, barAnimDuration));

        // ปิด shot ทั้งหมด
        foreach (var shot in shots)
            if (shot.vcam != null) shot.vcam.gameObject.SetActive(false);

        // สลับกล้องกลับ
        if (cutsceneCameraGO != null) cutsceneCameraGO.SetActive(false);
        if (playerCamera     != null) playerCamera.enabled = true;

        if (seatTarget != null) TeleportPlayer(seatTarget);
        if (seatTarget != null) PlayerController.Instance.StartSittingSequence(seatTarget);

        yield return new WaitUntil(() => PlayerController.Instance.IsSitting());
        yield return new WaitForSeconds(0.4f);

        // แสดงบทพูดผู้เล่นหลังนั่งเก้าอี้
        if (endingDialogue != null && endingDialogue.Length > 0 && PlayerDialogueUI.instance != null)
        {
            yield return new WaitForSeconds(endingDialogueDelay);
            bool dialogueDone = false;
            Action onDone = () => dialogueDone = true;
            PlayerDialogueUI.instance.OnSequenceComplete += onDone;
            PlayerDialogueUI.instance.ShowSequence(endingDialogue);
            yield return new WaitUntil(() => dialogueDone);
            PlayerDialogueUI.instance.OnSequenceComplete -= onDone;
        }

        // บล็อกการลุก — FriendListController จะปลดล็อกเมื่อถึงเวลา 18:40
        PlayerController.Instance.isBlockStanding = true;

        // แสดง hint โทรศัพท์ — ซ่อนเองเมื่อผู้เล่นกดปุ่มแชท หรือเมื่อ Rule1 เริ่ม
        if (GameHintUI.instance != null && !string.IsNullOrWhiteSpace(phoneHintText))
            GameHintUI.instance.Show(phoneHintText, autoDismissOnChatKey: true);

        PlayerController.Instance.SetLook(true);
        TimeManager.instance.IsPauseTime(false);
        gameObject.SetActive(false);
    }

    // ─────────────────────────── Helper ───────────────────────────

    private Transform GetSkipWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return null;

        int targetIndex = Mathf.Clamp(skipToWaypointIndex, 0, waypoints.Length - 1);
        return waypoints[targetIndex];
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
