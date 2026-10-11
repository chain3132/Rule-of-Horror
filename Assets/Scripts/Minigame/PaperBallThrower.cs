using Enum;
using InputSystem;
using UnityEngine;

/// <summary>
/// ฝั่งผู้เล่นของมินิเกมปาขยะ — แปะไว้บนตัวผู้เล่น (ตัวเดียวพอ)
///
/// หน้าที่: หาเป้าที่หยิบได้ (ลัง หรือก้อนที่ตกพื้น) → หยิบด้วย E → ถือค้างไว้หน้ากล้อง
///          → กดคลิกซ้ายค้างเพื่อเล็งแรง → ปล่อยเพื่อขว้าง
///
/// ทำไมตรรกะ "เล็งโดนอะไร" อยู่ที่นี่ ไม่ได้อยู่ที่ก้อนกระดาษแต่ละก้อน:
/// ของเดิมในโปรเจกต์ (Rule3/Paper.cs) ให้ทุกใบเช็กมุมกล้องเองใน Update แล้ว subscribe
/// อินพุตกันเอง ซึ่งพอมีหลายใบก็กลายเป็นงานซ้ำ N เท่า และเวลา unsubscribe ไม่ครบจะเกิด
/// MissingReferenceException ตอนกดคลิก ตัวนี้จึงรวมศูนย์: ก้อนกระดาษเป็นแค่ลูกบอลใบ้ๆ
///
/// ก้อนในมือหายไปเองได้ (ลังล้างก้อนทิ้งตอนเข้า Tension) — เลยไม่ subscribe OnModeChanged
/// แต่เช็ก _held == null ทุกเฟรมแทน ใครล้างก่อนล้างหลังก็ไม่พัง
/// </summary>
public class PaperBallThrower : MonoBehaviour
{
    // ─────────────────────────── Inspector ───────────────────────────

    [Header("อ้างอิง")]
    [Tooltip("เว้นว่าง = หาในซีนให้เอง")]
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("เว้นว่าง = ใช้ Camera.main")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("จุดที่ก้อนกระดาษไปเกาะตอนถืออยู่ — เว้นว่าง = สร้างให้เองใต้กล้องตามค่า Hand Offset")]
    [SerializeField] private Transform handAnchor;

    [Tooltip("ตำแหน่งมือเทียบกับกล้อง (ขวา, ขึ้น, หน้า) — ใช้ตอน Hand Anchor ว่าง")]
    [SerializeField] private Vector3 handOffset = new Vector3(0.3f, -0.26f, 0.5f);

    [Header("การขว้าง")]
    [Tooltip("กดค้างนานเท่านี้ = แรงเต็ม (วินาที)")]
    [SerializeField] private float chargeTime = 0.55f;

    [Tooltip("แตะคลิกแล้วปล่อยเลย = แรงเท่านี้ (เมตร/วินาที)")]
    [SerializeField] private float minThrowSpeed = 4.5f;

    [Tooltip("กดค้างจนเต็ม = แรงเท่านี้")]
    [SerializeField] private float maxThrowSpeed = 11f;

    [Tooltip("เสยขึ้นเท่าไร (0 = ขว้างตรงตามที่เล็ง) — ช่วยให้โยนลงถังง่ายขึ้นเวลาเล็งที่ปากถัง")]
    [SerializeField] private float throwUpward = 0.12f;

    [Tooltip("ความเร็วหมุนของก้อนตอนลอย (สุ่มทิศ)")]
    [SerializeField] private float spin = 8f;

    [Tooltip("ปล่อยลูกห่างจากมือไปข้างหน้าเท่าไร — กันลูกเกิดคาอยู่ในตัวเองตอนยืนชิดกำแพง")]
    [SerializeField] private float releaseForward = 0.2f;

    [Tooltip("ตอนเล็งแรง ก้อนจะถูกดึงกลับมาทางตัวเท่านี้ — เป็นตัวบอกแรงแบบไม่ต้องมี UI")]
    [SerializeField] private Vector3 chargePullback = new Vector3(-0.05f, -0.05f, -0.2f);

    [Header("เก็บก้อนที่ตกพื้น")]
    [Tooltip("ก้อนที่ตกอยู่บนพื้น ยืนห่างได้ไม่เกินกี่เมตรถึงจะเก็บได้")]
    [SerializeField] private float ballReach = 2.2f;

    [Tooltip("ต้องเล็งมาที่ก้อนในมุมไม่เกินกี่องศา")]
    [SerializeField] private float ballLookAngle = 25f;

    [Header("Prompt ปุ่ม")]
    [SerializeField] private string takeBallPromptId    = "minigame.trash.pickup";
    [SerializeField] private string takeBallPromptAction = "เก็บก้อนกระดาษ";
    [SerializeField] private string throwPromptId       = "minigame.trash.throw";
    [SerializeField] private string throwPromptKey      = "คลิกซ้าย ค้าง";
    [SerializeField] private string throwPromptAction   = "ขว้าง";
    [SerializeField] private string stashPromptAction   = "เก็บคืนลัง";

    // ─────────────────────────── State ───────────────────────────

    private PaperBall    _held;
    private PaperBallBox _targetBox;
    private PaperBall    _targetBall;
    private string       _shownPromptId;
    private string       _shownPromptAction;
    private bool         _shownThrowPrompt;

    private bool  _charging;
    private float _charge;

    private PhoneSystem.PhoneSystem _phone;
    private bool                    _phoneSearched;

    /// <summary>true = มีก้อนกระดาษอยู่ในมือ</summary>
    public bool IsHolding => _held != null;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        if (inputHandler == null) inputHandler = FindAnyObjectByType<InputHandler>();
    }

    private void OnEnable()
    {
        if (inputHandler != null) inputHandler.OnInteractPressed += HandleInteract;
    }

    private void OnDisable()
    {
        if (inputHandler != null) inputHandler.OnInteractPressed -= HandleInteract;

        HidePrompt();
        HideThrowPrompt();
    }

    private void Update()
    {
        if (!EnsureCamera()) return;

        // โทรศัพท์ยกอยู่ = WASD/คลิก เป็นของโทรศัพท์ ห้ามแตะมินิเกม
        // (ก้อนที่ถืออยู่ค้างไว้ในมือได้ แค่ไม่รับอินพุตและไม่โชว์ prompt)
        if (IsPhoneRaised())
        {
            CancelCharge();
            HidePrompt();
            HideThrowPrompt();

            // ซ่อนก้อนไว้ด้วย — มือเดียวกันกับที่ถือโทรศัพท์ ถ้าปล่อยไว้ก้อนจะทับจอโทรศัพท์
            SetHeldVisible(false);
            return;
        }

        SetHeldVisible(true);

        // ก้อนในมือถูกล้างทิ้งโดยคนอื่น (เข้า Tension / โหลดซีนใหม่)
        if (_held == null && (_charging || _shownThrowPrompt)) CancelCharge();

        UpdateTarget();
        UpdateHold();
        UpdatePrompt();
    }

    // ─────────────────────────── เป้า ───────────────────────────

    private void UpdateTarget()
    {
        _targetBox  = null;
        _targetBall = null;

        var cam = playerCamera;

        foreach (var box in PaperBallBox.Active)
        {
            if (box == null || !box.IsAimedAt(cam)) continue;
            _targetBox = box;
            break;
        }

        // ถืออยู่แล้วก็ยังต้องรู้ว่าเล็งลังไหม (เอาคืนลังได้) แต่ไม่ต้องไปหาก้อนบนพื้น
        if (_held != null) return;

        // มือว่างแต่ลังหมด → ไม่ต้องโชว์ปุ่มที่กดไปก็ไม่เกิดอะไร ไปหาก้อนที่ตกอยู่บนพื้นต่อเลย
        if (_targetBox != null && !_targetBox.IsAvailable) _targetBox = null;
        if (_targetBox != null) return;

        float bestAngle = ballLookAngle;

        foreach (var box in PaperBallBox.Active)
        {
            if (box == null) continue;

            var balls = box.LiveBalls;
            for (int i = 0; i < balls.Count; i++)
            {
                PaperBall ball = balls[i];
                if (ball == null || !ball.CanPickUp) continue;

                Vector3 toBall = ball.transform.position - cam.transform.position;
                if (toBall.sqrMagnitude > ballReach * ballReach) continue;

                float angle = Vector3.Angle(cam.transform.forward, toBall);
                if (angle > bestAngle) continue;

                bestAngle   = angle;   // ก้อนที่อยู่ "ตรงกลางจอที่สุด" ชนะ ไม่ใช่ก้อนที่ใกล้ที่สุด
                _targetBall = ball;
            }
        }
    }

    // ─────────────────────────── หยิบ ───────────────────────────

    private void HandleInteract()
    {
        if (!enabled || !EnsureCamera() || IsPhoneRaised()) return;

        if (_held != null)
        {
            // ถืออยู่แล้วกด E ตอนเล็งลัง = เอาคืนลัง (เปลี่ยนใจ / ไม่อยากถือค้างไว้)
            if (_targetBox != null) Stash();
            return;
        }

        if (_targetBox != null)
        {
            PaperBall fresh = _targetBox.TakeBall();
            if (fresh != null) Hold(fresh);
            return;
        }

        if (_targetBall != null) Hold(_targetBall);
    }

    private void Hold(PaperBall ball)
    {
        _held = ball;
        ball.Grab(ResolveHand());

        CancelCharge();
        HidePrompt();
    }

    private void Stash()
    {
        PaperBall ball = _held;
        _held = null;
        CancelCharge();

        if (ball.Origin != null) ball.Origin.ReturnBall(ball);
        else                     ball.Despawn();
    }

    // ─────────────────────────── ถือ / ขว้าง ───────────────────────────

    private void UpdateHold()
    {
        if (_held == null || inputHandler == null) return;

        if (inputHandler.WasThrowPressed()) _charging = true;

        if (_charging)
        {
            _charge = chargeTime <= 0f ? 1f : Mathf.Min(1f, _charge + Time.deltaTime / chargeTime);
            if (inputHandler.WasThrowReleased()) { Throw(); return; }
        }

        // แกว่งเบาๆ ให้ไม่ดูเหมือนรูปติดจอ + ดึงกลับตามแรงที่เล็งไว้
        Vector3 sway = new Vector3(0f, Mathf.Sin(Time.time * 1.6f) * 0.01f, 0f);
        _held.transform.localPosition = Vector3.Lerp(Vector3.zero, chargePullback, _charge) + sway;
    }

    private void Throw()
    {
        Transform cam = playerCamera.transform;

        Vector3 dir   = (cam.forward + Vector3.up * throwUpward).normalized;
        float   speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, _charge);
        Vector3 from  = _held.transform.position + cam.forward * releaseForward;

        _held.Release(from, dir * speed, Random.onUnitSphere * spin);
        _held = null;

        CancelCharge();
    }

    private void SetHeldVisible(bool value)
    {
        if (_held == null || _held.gameObject.activeSelf == value) return;
        _held.gameObject.SetActive(value);
    }

    private void CancelCharge()
    {
        _charging = false;
        _charge   = 0f;
        HideThrowPrompt();
    }

    // ─────────────────────────── Prompt ───────────────────────────

    private void UpdatePrompt()
    {
        if (_held != null)
        {
            ShowThrowPrompt();

            // เล็งลังตอนถืออยู่ → บอกด้วยว่าเอาคืนได้
            if (_targetBox != null) Show(_targetBox.PromptId, _targetBox.PromptKey, stashPromptAction, _targetBox.AimPoint);
            else                    HidePrompt();
            return;
        }

        HideThrowPrompt();

        if (_targetBox != null)
            Show(_targetBox.PromptId, _targetBox.PromptKey, _targetBox.PromptAction, _targetBox.AimPoint);
        else if (_targetBall != null)
            Show(takeBallPromptId, "E", takeBallPromptAction, _targetBall.transform);
        else
            HidePrompt();
    }

    /// <summary>
    /// โชว์ prompt อันเดียวที่ "เล็งอยู่ตอนนี้" — เรียกซ้ำทุกเฟรมได้ ไม่เด้ง
    ///
    /// ต้องเทียบทั้ง id และข้อความ ไม่ใช่ id อย่างเดียว เพราะลังใบเดียวใช้ id เดิมแต่คำเปลี่ยน
    /// ("หยิบกระดาษขยำ" ตอนมือว่าง / "เก็บคืนลัง" ตอนถืออยู่) ถ้าเทียบแค่ id คำจะค้างอันเก่า
    /// </summary>
    private void Show(string id, string key, string action, Transform anchor)
    {
        if (KeyPromptManager.instance == null) return;
        if (_shownPromptId == id && _shownPromptAction == action) return;

        HidePrompt();

        _shownPromptId     = id;
        _shownPromptAction = action;
        KeyPromptManager.instance.Show(id, key, action, anchor);
    }

    private void HidePrompt()
    {
        if (_shownPromptId == null) return;

        if (KeyPromptManager.instance != null) KeyPromptManager.instance.Hide(_shownPromptId);

        _shownPromptId     = null;
        _shownPromptAction = null;
    }

    private void ShowThrowPrompt()
    {
        if (_shownThrowPrompt || KeyPromptManager.instance == null) return;

        _shownThrowPrompt = true;
        KeyPromptManager.instance.Show(throwPromptId, throwPromptKey, throwPromptAction);
    }

    private void HideThrowPrompt()
    {
        if (!_shownThrowPrompt) return;

        _shownThrowPrompt = false;
        if (KeyPromptManager.instance != null) KeyPromptManager.instance.Hide(throwPromptId);
    }

    // ─────────────────────────── Helper ───────────────────────────

    private bool EnsureCamera()
    {
        if (playerCamera != null) return true;

        playerCamera = Camera.main;
        return playerCamera != null;
    }

    /// <summary>
    /// จุดที่ก้อนไปเกาะ — ไม่ได้ตั้งไว้ก็สร้างให้เองใต้กล้อง
    ///
    /// ที่ต้อง parent กับกล้องจริงๆ (ไม่ใช่ขยับตำแหน่งเองทุกเฟรม) เพราะ PlayerController.Look
    /// ถูกเรียกจาก Update ของ InputHandler ซึ่งลำดับ Update ระหว่าง component ไม่การันตี
    /// ถ้าเราขยับเองอาจช้ากว่ากล้องหนึ่งเฟรม แล้วก้อนจะสั่นตอนหันกล้อง
    /// </summary>
    private Transform ResolveHand()
    {
        if (handAnchor != null) return handAnchor;

        var anchor = new GameObject("PaperBallHand").transform;
        anchor.SetParent(playerCamera.transform, false);
        anchor.localPosition = handOffset;
        anchor.localRotation = Quaternion.identity;

        handAnchor = anchor;
        return handAnchor;
    }

    private bool IsPhoneRaised()
    {
        if (!_phoneSearched)
        {
            _phone         = FindAnyObjectByType<PhoneSystem.PhoneSystem>();
            _phoneSearched = true;
        }

        return _phone != null && _phone.CurrentState != PhoneState.Hidden;
    }
}
