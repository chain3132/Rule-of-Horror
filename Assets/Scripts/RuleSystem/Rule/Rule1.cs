using System.Collections;
using System.HeartbeatSystem;
using FMOD.Studio;
using FMODUnity;
using Manager;
using PhoneSystem;
using Player;
using RuleSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Rule 1 — ห้ามออกนอกเขต
///
/// Flow:
///   StartRule()  → โทรศัพท์หายไป, phone on ground ปรากฏในเขตสีแดง (PhoneDropped)
///   PhoneDropped → ยิ่งออกไกล → distortion มากขึ้น; นอกทุก zone นานเกิน → ตาย
///   OnPhonePickedUp() → โทรศัพท์คืน, distortion หาย (ReturnToSeat)
///   ReturnToSeat → ผู้เล่นนั่ง → EndRule()
/// </summary>
public class Rule1 : RuleBase
{
    // ─────────────────────────── State ───────────────────────────

    enum Rule1State { None, PhoneDropped, ReturnToSeat }

    // ─────────────────────────── Inspector ───────────────────────────

    [Header("References")]
    [Tooltip("โทรศัพท์บนพื้นที่ผู้เล่นต้องไปเก็บ (มี PhoneGroundPickup ติดอยู่)")]
    [SerializeField] private GameObject phoneGroundObject;

    [Tooltip("จุด teleport ผู้เล่นหลังตาย (ตำแหน่งยืนใกล้เก้าอี้)")]
    [SerializeField] private Transform playerResetPoint;

    [Header("Death")]
    [Tooltip("วินาทีที่อยู่นอกทุก zone (Outside) ก่อนตาย")]
    [SerializeField] private float outsideDeathTime = 6f;

    [Header("Phone")]
    [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;

    [Header("Post Processing")]
    [SerializeField] private Volume globalVolume;

    // ── Zone Lens Distortion (ambient barrel) ────────────────────────────────
    [Header("Zone Lens Distortion")]
    [Tooltip("LensDistortion intensity ของ Orange zone\n" +
             "แนะนำ -0.10 ถึง -0.20 (เบา ๆ — wave เป็นหลัก)")]
    [SerializeField] private float orangeLens   = -0.12f;
    [Tooltip("ChromaticAberration ของ Orange zone")]
    [SerializeField] private float orangeChroma = 0.30f;

    [Tooltip("LensDistortion intensity ของ Red zone\n" +
             "แนะนำ -0.25 ถึง -0.40")]
    [SerializeField] private float redLens   = -0.30f;
    [Tooltip("ChromaticAberration ของ Red zone")]
    [SerializeField] private float redChroma = 0.60f;

    [Header("Wave Distortion")]
    [Tooltip("Component ที่ควบคุม WaveDistortion.shader\n" +
             "ดู WaveDistortionEffect.cs สำหรับวิธี setup ใน Inspector")]
    [SerializeField] private WaveDistortionEffect waveEffect;

    [Header("Player Dialogue")]
    [Tooltip("บทพูดที่แสดงตอนโทรศัพท์หายออกจากมือผู้เล่น\n" +
             "ตัวอย่าง: 'โทรศัพท์ของฉัน... หายไปไหน?'")]
    [SerializeField] private DialogueLine[] phoneDropDialogue;

    [Tooltip("หน่วงก่อนแสดงบทพูด (วินาที) ให้เวลา effect โทรศัพท์หายเล่นก่อน")]
    [SerializeField] private float phoneDropDialogueDelay = 0.8f;

    [Header("Hint UI")]
    [Tooltip("ข้อความ hint บอกผู้เล่นให้กด E เพื่อลุกขึ้น (แสดงตอน Rule 1 เริ่ม)\n" +
             "เว้นว่างถ้าไม่ต้องการแสดง hint")]
    [TextArea(1, 3)]
    [SerializeField] private string standHintText = "E    ลุกขึ้น";

    [Header("Sound")]
    [Tooltip("เสียงโทรศัพท์ 3D ที่วางอยู่บนพื้น (loop)\nเว้นว่างถ้าไม่ต้องการ")]
    [SerializeField] private string phoneSoundEvent = "";

    [Tooltip("เสียงผีฮัม หลังจากเก็บโทรศัพท์จนกว่าจะนั่ง (loop)\nเว้นว่างถ้าไม่ต้องการ")]
    [SerializeField] private string ghostHumEvent = "";

    // ─────────────────────────── Runtime ───────────────────────────

    private Rule1State    _state        = Rule1State.None;
    private float         _dieTimer;
    private bool          _isDying;
    private bool          _hasCompleted;   // กันไม่ให้ trigger ซ้ำหลัง EndRule
    private bool          _standHintActive;
    private EventInstance _phoneSoundInstance;
    private EventInstance _ghostHumInstance;

    // ─────────────────────────── RuleBase Overrides ───────────────────────────

    /// <summary>
    /// เรียกจาก FriendListController เมื่อผู้เล่นอ่าน Rules conversation จบ
    /// ใช้แทนการ trigger อัตโนมัติจาก RuleManager (ตั้ง startHour = 23 ใน Inspector เพื่อไม่ให้ auto-trigger)
    /// </summary>
    public void TriggerStartRule()
    {
        if (ruleActive || _hasCompleted) return;   // กันทั้ง active และ completed แล้ว
        StartRule();
    }

    public override void StartRule()
    {
        base.StartRule();
        TimeManager.instance.IsPauseTime(true);
        ChangeState(Rule1State.PhoneDropped);
    }

    public override void EndRule()
    {
        _hasCompleted = true;   // ล็อกไม่ให้ trigger ซ้ำ
        base.EndRule();
        ResetDistortion();
        StopGhostHum();
        GameHintUI.instance?.Hide();
        StopPhoneSound();
        GameModeController.instance.BlinkToMode(GameMode.Relax);
        PlayerController.Instance.isBlockStanding = false;
        AudioManager.instance.SetHeartbeatLevel(0);
        AudioManager.instance.UpdateHeartbeat(); // force parameter to 0 ทันที
        TimeManager.instance.IsPauseTime(false);
        TimeManager.instance.SetTime(19, 39);
    }

    protected override void UpdateRule()
    {
        HeartbeatSystem.instance.CheckPlayerInsideZone();
        AudioManager.instance.UpdateHeartbeat();

        switch (_state)
        {
            case Rule1State.PhoneDropped:  UpdatePhoneDropped();  break;
            case Rule1State.ReturnToSeat:  UpdateReturnToSeat();  break;
        }
    }

    // ─────────────────────────── State: PhoneDropped ───────────────────────────

    void StartPhoneDropped()
    {
        PlayerController.Instance.isBlockStanding = false;
        
        // ล็อกโทรศัพท์ → ผู้เล่นกด toggle ไม่ได้
        if (phoneSystem != null) phoneSystem.LockPhone();
        // แสดงโทรศัพท์บนพื้นในเขตสีแดง
        if (phoneGroundObject != null) phoneGroundObject.SetActive(true);

        // เสียงโทรศัพท์ 3D ที่ตำแหน่งโทรศัพท์บนพื้น
        if (!string.IsNullOrWhiteSpace(phoneSoundEvent) && phoneGroundObject != null)
        {
            _phoneSoundInstance = RuntimeManager.CreateInstance(phoneSoundEvent);
            RuntimeManager.AttachInstanceToGameObject(_phoneSoundInstance, phoneGroundObject.transform);
            _phoneSoundInstance.start();
        }

        _dieTimer = outsideDeathTime;

        // ซ่อน phone controls hint แล้วแสดง stand hint
        if (GameHintUI.instance != null)
        {
            GameHintUI.instance.Hide();
            if (!string.IsNullOrWhiteSpace(standHintText))
            {
                GameHintUI.instance.Show(standHintText);
                _standHintActive = true;
            }
        }

        if (phoneDropDialogue != null && phoneDropDialogue.Length > 0)
            StartCoroutine(ShowPhoneDropDialogue());
    }

    IEnumerator ShowPhoneDropDialogue()
    {
        yield return new WaitForSeconds(phoneDropDialogueDelay);
        if (PlayerDialogueUI.instance != null)
            PlayerDialogueUI.instance.ShowSequence(phoneDropDialogue);
    }

    void UpdatePhoneDropped()
    {
        if (_isDying) return;

        // ซ่อน stand hint ทันทีที่ผู้เล่นลุกขึ้น
        if (_standHintActive && !PlayerController.Instance.IsSitting())
        {
            GameHintUI.instance?.Hide();
            _standHintActive = false;
        }

        ZoneLevel zone = HeartbeatSystem.instance.CurrentZoneLevel;
        ApplyZoneDistortion(zone);

        if (zone == ZoneLevel.Outside)
        {
            _dieTimer -= Time.deltaTime;
            if (_dieTimer <= 0f)
                StartCoroutine(DieRoutine());
        }
        else
        {
            // รีเซ็ต timer เมื่อกลับเข้า zone ใด ๆ
            _dieTimer = outsideDeathTime;
        }
    }

    // ─────────────────────────── State: ReturnToSeat ───────────────────────────

    void StartReturnToSeat()
    {
        // ปลดล็อกโทรศัพท์ → ผู้เล่น toggle ได้อีกครั้ง
        if (phoneSystem != null) phoneSystem.UnlockPhone();

        // หยุดเสียงโทรศัพท์บนพื้น
        StopPhoneSound();
        
        AudioManager.instance.SetHeartbeatLevel(0);
        AudioManager.instance.UpdateHeartbeat();

        // เริ่มเสียงผีฮัมจนกว่าผู้เล่นจะนั่ง
        if (!string.IsNullOrWhiteSpace(ghostHumEvent))
        {
            _ghostHumInstance = RuntimeManager.CreateInstance(ghostHumEvent);
            _ghostHumInstance.start();
        }
    }

    void UpdateReturnToSeat()
    {
        // อัปเดต wave / distortion ตาม zone เหมือน PhoneDropped state
        // (ทำงานไม่ว่าจะหยิบโทรศัพท์แล้วหรือยัง)
        ZoneLevel zone = HeartbeatSystem.instance.CurrentZoneLevel;
        ApplyZoneDistortion(zone);

        // รอผู้เล่นกลับมานั่ง
        if (PlayerController.Instance.IsSitting())
            EndRule();
    }

    // ─────────────────────────── Callback จาก PhoneGroundPickup ───────────────────────────

    /// <summary>เรียกจาก PhoneGroundPickup เมื่อผู้เล่น interact เก็บโทรศัพท์</summary>
    public void OnPhonePickedUp()
    {
        if (_state != Rule1State.PhoneDropped) return;
        ChangeState(Rule1State.ReturnToSeat);
    }

    // ─────────────────────────── Distortion ───────────────────────────

    void ApplyZoneDistortion(ZoneLevel zone)
    {
        LensDistortion      lens   = null;
        ChromaticAberration chroma = null;
        if (globalVolume != null)
        {
            globalVolume.profile.TryGet(out lens);
            globalVolume.profile.TryGet(out chroma);
        }

        switch (zone)
        {
            // ── ปลอดภัย → ปิด effect ทั้งหมด ────────────────────────────
            case ZoneLevel.Green:
                SetLens(lens, 0f);
                SetChroma(chroma, 0f);
                waveEffect?.SetOff();
                break;

            // ── Orange → wave แกว่งช้า + lens เบา ๆ ──────────────────
            case ZoneLevel.Orange:
                SetLens(lens, orangeLens);
                SetChroma(chroma, orangeChroma);
                waveEffect?.SetOrange();
                break;

            // ── Red → wave รุนแรง + lens medium + chroma สูง ──────────
            case ZoneLevel.Red:
                SetLens(lens, redLens);
                SetChroma(chroma, redChroma);
                waveEffect?.SetRed();
                break;

            // ── Outside → barrel distortion สูงสุด ไม่มี wave ──────────
            // (กำลังนับถอยหลังตาย — static ดูน่ากลัวกว่า wave)
            case ZoneLevel.Outside:
                SetLens(lens, -1.0f);
                SetChroma(chroma, 1.0f);
                waveEffect?.SetOff();
                break;
        }
    }

    static void SetLens(LensDistortion lens, float v)
    {
        if (lens == null) return;
        lens.intensity.value = v;
        lens.center.value    = Vector2.zero;
    }

    static void SetChroma(ChromaticAberration chroma, float v)
    {
        if (chroma != null) chroma.intensity.value = v;
    }

    void ResetDistortion()
    {
        waveEffect?.SetOff();

        if (globalVolume == null) return;
        if (globalVolume.profile.TryGet(out LensDistortion lens))
        {
            lens.intensity.value = 0f;
            lens.center.value    = Vector2.zero;
        }
        if (globalVolume.profile.TryGet(out ChromaticAberration chroma))
            chroma.intensity.value = 0f;
    }

    // ─────────────────────────── Death ───────────────────────────

    IEnumerator DieRoutine()
    {
        _isDying = true;
        ResetDistortion();

        // 1. กล้องล้ม (fall animation)
        yield return StartCoroutine(PlayerController.Instance.PlayDeathFallRoutine());

        // 2. Blink ปิดตา → reset ขณะมืด → ลืมตา
        GameModeController.instance.DirectBlinkToMode(GameMode.Relax, ResetAfterDeath);

        // 3. รอตาเปิดสนิท
        yield return new WaitUntil(() => GameModeController.instance.IsEyesOpen);

        // 4. คืนการควบคุมให้ผู้เล่น
        PlayerController.Instance.ResetCameraAfterDeath();
        PlayerController.Instance.SetMovement(true);

        _isDying  = false;
        _dieTimer = outsideDeathTime;
    }

    /// <summary>รีเซ็ตตำแหน่งและ phone ขณะหน้าจอดำ (callback ตอนตาปิดสนิท)</summary>
    void ResetAfterDeath()
    {
        // Teleport ผู้เล่นกลับจุดเริ่มต้น
        if (playerResetPoint != null)
        {
            var cc = PlayerController.Instance.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            PlayerController.Instance.transform.position = playerResetPoint.position;
            if (cc != null) cc.enabled = true;
        }

        // วางโทรศัพท์กลับบนพื้นใหม่ + เริ่มเสียงใหม่
        if (phoneGroundObject != null)
        {
            phoneGroundObject.SetActive(true);
            StopPhoneSound();
            if (!string.IsNullOrWhiteSpace(phoneSoundEvent))
            {
                _phoneSoundInstance = RuntimeManager.CreateInstance(phoneSoundEvent);
                RuntimeManager.AttachInstanceToGameObject(_phoneSoundInstance, phoneGroundObject.transform);
                _phoneSoundInstance.start();
            }
        }

        _dieTimer = outsideDeathTime;
    }

    // ─────────────────────────── Helper ───────────────────────────

    void ChangeState(Rule1State newState)
    {
        _state = newState;
        switch (newState)
        {
            case Rule1State.PhoneDropped:  StartPhoneDropped();  break;
            case Rule1State.ReturnToSeat:  StartReturnToSeat();  break;
        }
    }

    void StopPhoneSound()
    {
        if (_phoneSoundInstance.isValid())
        {
            _phoneSoundInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _phoneSoundInstance.release();
            _phoneSoundInstance = default;
        }
    }

    void StopGhostHum()
    {
        if (_ghostHumInstance.isValid())
        {
            _ghostHumInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _ghostHumInstance.release();
            _ghostHumInstance = default;
        }
    }
}
