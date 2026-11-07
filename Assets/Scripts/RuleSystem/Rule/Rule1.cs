using System.Collections;
using FMOD.Studio;
using FMODUnity;
using Manager;
using PhoneSystem;
using Player;
using RuleSystem;
using UnityEngine;

/// <summary>
/// Rule 1 — ห้ามออกนอกเขต
///
/// Flow:
///   StartRule()  → โทรศัพท์หายไป, phone on ground ปรากฏในเขตสีแดง (PhoneDropped)
///   OnPhonePickedUp() → โทรศัพท์คืน (ReturnToSeat)
///   ReturnToSeat → ผู้เล่นนั่ง → EndRule()
///
/// ภาพบิด เสียงหัวใจ และการตายนอกเขต ย้ายไปอยู่ที่ HeartbeatSystem แล้ว เพราะใช้กับทุกกฎตอน Tension
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

    [Header("Phone")]
    [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;

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
        if (!CanStart)
        {
            Debug.Log("[Rule1] ไม่เริ่ม — เล่นจบไปแล้ว, มีกฎอื่นเล่นอยู่ หรือเลยเวลาของกฎนี้ไปแล้ว", this);
            return;
        }

        StartRule();
    }

    /// <summary>
    /// เริ่มกฎ 1 ตอนนี้ได้ไหม — โทรศัพท์หลุดจากมือได้เฉพาะช่วงต้นคืนที่ยังไม่มีกฎอื่นเล่นอยู่
    ///
    /// แชทกฎ 1 เป็นตัวปลุกกฎนี้ และแชทค้างอ่านได้ทุกเมื่อ ถ้าปุ่มเทสข้ามเวลาไปกฎ 5 แล้วเผลอเปิดอ่าน
    /// แชทนั้น โทรศัพท์จะหายกลางกฎ 5 ทั้งที่เวลาของกฎ 1 ผ่านไปแล้ว
    /// (กติกาเขตตอน Tension ไม่เกี่ยวกับตัวนี้ มันอยู่ที่ HeartbeatSystem และใช้ทุกกฎตามปกติ)
    /// </summary>
    public bool CanStart =>
        !ruleActive && !_hasCompleted && ActiveRuleInScene() == null &&
        (TimeManager.instance == null || TimeManager.instance.CurrentTotalMinutes < endHour * 60 + endMinute);

    public override void DebugForceEnd()
    {
        StopAllCoroutines();
        _isDying = false;

        // ทางจบปกติโทรศัพท์คืนมาแล้ว แต่ปุ่มเทสจบได้ตอนโทรศัพท์ยังหายอยู่
        if (phoneSystem != null) phoneSystem.UnlockPhone();
        if (phoneGroundObject != null) phoneGroundObject.SetActive(false);
        _state = Rule1State.None;

        ReportCompleted();
        EndRule();
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
        StopGhostHum();
        GameHintUI.instance?.Hide();
        StopPhoneSound();
        GameModeController.instance.BlinkToMode(GameMode.Relax);
        // ไม่ปลด isBlockStanding ตรงนี้ — base.EndRule() ข้างบนล็อกไว้ให้จนตาปิดสนิทแล้วปลดเอง
        AudioManager.instance.SetHeartbeatLevel(0);
        AudioManager.instance.UpdateHeartbeat(); // force parameter to 0 ทันที
        TimeManager.instance.IsPauseTime(false);
        TimeManager.instance.SetTime(19, 39);
    }

    /// <summary>
    /// คืนใหม่จากบทจบวนลูปของกฎข้อ 6 — ปลดล็อกให้กฎ 1 เล่นได้อีกครั้ง
    ///
    /// กฎ 1 เป็นข้อเดียวที่กันเล่นซ้ำด้วยธงของตัวเอง (_hasCompleted) เพราะมันไม่ได้ trigger
    /// ตามเวลา ถ้าไม่ล้างตรงนี้ คืนรอบสองจะข้ามกฎ 1 ไปทั้งข้อ: อ่านแชทกฎจบแล้วโทรศัพท์
    /// ก็ไม่หาย ไม่มีอะไรเกิดขึ้น แล้วรอเฉยๆ ถึง 20:00
    /// </summary>
    public override void ResetForNewLoop()
    {
        _hasCompleted = false;
        _isDying      = false;
        _state        = Rule1State.None;
    }

    public override bool UsesTensionZones => ruleActive && _state != Rule1State.None && !_isDying;

    public override void DieOutsidePlayArea()
    {
        if (!_isDying) StartCoroutine(DieRoutine());
    }

    protected override void UpdateRule()
    {
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

        // คืนรอบสอง (วนลูปจากกฎ 6) ไม่พูดซ้ำ — ผู้เล่นเคยเจอโทรศัพท์หายมาแล้ว
        // ตกใจซ้ำแบบเดิมอ่านเป็นเกมลืม ไม่ใช่คืนเดิมที่วนกลับมา
        bool loopedNight = RuleSystem.Rule.Rule6.NightLoopCount > 0;
        if (!loopedNight && phoneDropDialogue != null && phoneDropDialogue.Length > 0)
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
        // รอผู้เล่นกลับมานั่ง
        if (PlayerController.Instance.IsSitting())
        {
            ReportCompleted();
            EndRule();
        }
    }

    // ─────────────────────────── Callback จาก PhoneGroundPickup ───────────────────────────

    /// <summary>เรียกจาก PhoneGroundPickup เมื่อผู้เล่น interact เก็บโทรศัพท์</summary>
    public void OnPhonePickedUp()
    {
        if (_state != Rule1State.PhoneDropped) return;
        ChangeState(Rule1State.ReturnToSeat);
    }

    // ─────────────────────────── Death ───────────────────────────

    IEnumerator DieRoutine()
    {
        _isDying = true;

        // 1. กล้องล้ม (fall animation)
        yield return StartCoroutine(PlayerController.Instance.PlayDeathFallRoutine());

        // รอบสอง (ขึ้นรถคันที่ 4 ของกฎ 6 มาแล้ว): ตายรอบเดียวจบ ไม่ฟื้นขึ้นมาอีก
        // กฎ 1 ไม่ย้อนเวลาเหมือนกฎอื่น มันแค่ teleport กลับจุดเริ่ม — ตัวที่ต้องตัดคือการฟื้น
        if (RuleSystem.PermanentDeath.Claim(this)) yield break;

        // 2. Blink ปิดตา → reset ขณะมืด → ลืมตา
        //
        //    กลับเข้า Tension ไม่ใช่ Relax — ตายแล้วกฎ 1 ยังไม่จบ ผู้เล่นต้องไปเก็บโทรศัพท์
        //    ใหม่ในกฎเดิม เดิมสั่ง Relax ทำให้ฟื้นมาเจอบรรยากาศโหมดพักทั้งที่ยังเล่นกฎอยู่
        //    (โทรศัพท์ยังถูกล็อก, ยังนับถอยหลังตายถ้าออกนอกเขต)
        GameModeController.instance.DirectBlinkToMode(GameMode.Tension, ResetAfterDeath);

        // 3. รอตาเปิดสนิท
        yield return new WaitUntil(() => GameModeController.instance.IsEyesOpen);

        // 4. สั่งหยุดนาฬิกาซ้ำ — BlinkRoutine ปล่อยนาฬิกาทุกครั้งที่ตาเปิดสุด
        //
        //    กฎ 1 ไม่ได้อยู่ใน RuleManager.rules (ถูก trigger จาก FriendListController ตอนอ่าน
        //    แชทกฎจบ) จึงไม่มีใครมาสั่ง EndRule ให้ตอน 19:40 — แต่กฎอื่น "อยู่" ในลิสต์
        //    ถ้าปล่อยนาฬิกาเดินต่อหลังตาย มันจะไต่จาก 19:0x ไปถึง 20:00 แล้ว RuleManager
        //    จะเริ่มกฎ 2 ทับกฎ 1 ที่ยังไม่จบ (ที่ 6 วินาที/นาทีเกม = ตายค้างไว้ราว 6 นาทีจริง)
        //
        //    กฎ 2-5 ไม่เจอปัญหานี้เพราะ GameOverRoutine ของแต่ละกฎ rewind เวลากลับไปก่อน
        //    ชั่วโมงเริ่มของตัวเองแล้วให้ RuleManager retrigger กฎเดิม — กฎ 1 เริ่มเองไม่ได้
        //    จึงต้องหยุดนาฬิกาค้างไว้แทน
        TimeManager.instance.IsPauseTime(true);

        // 5. คืนการควบคุมให้ผู้เล่น
        PlayerController.Instance.ResetCameraAfterDeath();
        PlayerController.Instance.SetMovement(true);

        _isDying = false;
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
        // เก็บโทรศัพท์ไปแล้วไม่วางซ้ำ — ตอนนี้ตายนอกเขตได้ทั้งระหว่างเดินกลับไปนั่ง ซึ่งโทรศัพท์อยู่ในมือแล้ว
        if (phoneGroundObject != null && _state == Rule1State.PhoneDropped)
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
