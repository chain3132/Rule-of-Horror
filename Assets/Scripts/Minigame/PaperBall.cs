using UnityEngine;

/// <summary>
/// ก้อนกระดาษขยำ — ลูกที่ผู้เล่นหยิบจากลังแล้วขว้างใส่ถังขยะ
///
/// ตัวนี้ไม่คิดเองเลย ไม่อ่านอินพุต ไม่หาเป้า มันแค่สลับระหว่างสองสภาพ:
///   ถืออยู่   = kinematic + ปิด collider + เกาะอยู่กับมือ (PaperBallThrower เป็นคนขยับ)
///   ปล่อยแล้ว = rigidbody ปกติ ตกตามแรงโน้มถ่วง
/// คนสั่งคือ PaperBallThrower (หยิบ/ขว้าง), PaperBallBox (เกิด/เก็บคืน), TrashBin (ลงถัง)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PaperBall : MonoBehaviour
{
    // ─────────────────────────── Inspector ───────────────────────────

    [Header("เสียง (เว้นว่างได้ ไม่ใส่ก็เล่นได้ปกติ)")]
    [Tooltip("เว้นว่าง = หาจาก AudioSource บนตัวเอง ไม่มีก็เงียบไปเลย")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("เสียงกระดาษกระทบพื้น/ผนัง — ใส่หลายไฟล์ได้ จะสุ่มทุกครั้งที่กระทบ")]
    [SerializeField] private AudioClip[] impactClips;

    [Tooltip("กระทบแรงกว่านี้ (เมตร/วินาที) ถึงจะมีเสียง — กันเสียงรัวตอนก้อนกลิ้งอยู่บนพื้น")]
    [SerializeField] private float minImpactSpeed = 1.2f;

    [SerializeField, Range(0f, 1f)] private float impactVolume = 0.35f;

    [Header("พฤติกรรม")]
    [Tooltip("หลังขว้างแล้วต้องรอกี่วินาทีถึงจะหยิบซ้ำได้ — กันกดหยิบติดกันจนลูกเด้งกลับเข้ามือทันที")]
    [SerializeField] private float pickupCooldown = 0.5f;

    [Tooltip("ปาไปแล้วอีกกี่วินาทีก้อนจะหายไปเอง (นับใหม่ทุกครั้งที่ปา หยุดนับตอนถืออยู่)\n" +
             "0 = ไม่หายเอง ต้องรอให้เกินเพดานของลังหรือเข้า Tension")]
    [SerializeField] private float despawnAfter = 20f;

    [Tooltip("ก่อนหายจะย่อตัวลงกี่วินาที — 0 = หายไปเลยแบบตัดจบ")]
    [SerializeField] private float shrinkTime = 0.6f;

    // ─────────────────────────── State ───────────────────────────

    /// <summary>true = อยู่ในมือผู้เล่นตอนนี้</summary>
    public bool IsHeld { get; private set; }

    /// <summary>true = ลูกนี้ลงถังไปแล้ว — กันถังนับซ้ำตอนลูกกระเด้งในถัง</summary>
    public bool IsScored { get; private set; }

    /// <summary>ลังที่ปล่อยลูกนี้ออกมา — ถังขยะใช้ตอนคืนลูกกลับลัง</summary>
    public PaperBallBox Origin { get; private set; }

    private Rigidbody  _rb;
    private Collider[] _colliders;
    private float      _releasedAt = -99f;

    // นับถอยหลังหายเอง — เริ่มนับตอนปล่อยมือ ไม่ใช่ตอนเกิด (ถือค้างไว้นานแค่ไหนก็ไม่หาย)
    private float      _despawnAt = -1f;
    private float      _vanishStart;
    private bool       _vanishing;
    private Vector3    _baseScale = Vector3.one;

    // คนละก้อนก็ใช้ชุดเดียวกัน หาครั้งเดียวพอ (ผู้เล่นมีตัวเดียวและ DontDestroyOnLoad)
    private static Collider[] _playerColliders;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        _rb        = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>(true);
        _baseScale = transform.localScale;

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    private void Update()
    {
        if (_vanishing) { UpdateVanish(); return; }

        if (_despawnAt < 0f || IsHeld || IsScored) return;
        if (Time.time >= _despawnAt) BeginVanish();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (audioSource == null || impactClips == null || impactClips.Length == 0) return;
        if (collision.relativeVelocity.magnitude < minImpactSpeed) return;

        AudioClip clip = impactClips[Random.Range(0, impactClips.Length)];
        if (clip != null) audioSource.PlayOneShot(clip, impactVolume);
    }

    // ─────────────────────────── API ───────────────────────────

    /// <summary>ลังเรียกตอนเกิดลูกใหม่ — บอกว่าตัวเองมาจากลังไหน</summary>
    public void Init(PaperBallBox origin)
    {
        Origin = origin;
        IgnorePlayerCollision();
    }

    /// <summary>หยิบขึ้นมือ — หยุดฟิสิกส์แล้วเกาะไปกับ hand (PaperBallThrower ขยับ localPosition ต่อเอง)</summary>
    public void Grab(Transform hand)
    {
        IsHeld     = true;
        _despawnAt = -1f;   // ถืออยู่ไม่ต้องนับ — เริ่มนับใหม่ตอนปล่อยมือ

        _rb.isKinematic      = true;
        _rb.detectCollisions = false;
        SetCollidersEnabled(false);

        transform.SetParent(hand, false);
        transform.localPosition = Vector3.zero;
    }

    /// <summary>ปล่อยออกจากมือที่ตำแหน่ง/ความเร็ว/การหมุนที่กำหนด</summary>
    public void Release(Vector3 position, Vector3 velocity, Vector3 spin)
    {
        transform.SetParent(null, true);
        transform.position = position;

        IsHeld = false;

        _rb.isKinematic      = false;
        _rb.detectCollisions = true;
        SetCollidersEnabled(true);

        // สถานะ IgnoreCollision หายไปตอน collider ถูกปิด → ต้องสั่งใหม่ทุกครั้งที่เปิดกลับ
        // ไม่ใช่สั่งครั้งเดียวตอน Init (ดูเหตุผลที่ต้อง ignore ใน IgnorePlayerCollision)
        IgnorePlayerCollision();

        // ลูกเล็กและเร็ว — discrete จะทะลุพื้น/ผนังบาง
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.linearVelocity         = velocity;
        _rb.angularVelocity        = spin;

        _releasedAt = Time.time;
        _despawnAt  = despawnAfter > 0f ? Time.time + despawnAfter : -1f;
    }

    /// <summary>หยิบซ้ำได้ไหม (ก้อนที่ตกอยู่บนพื้น)</summary>
    public bool CanPickUp => !IsHeld && !IsScored && !_vanishing && Time.time - _releasedAt >= pickupCooldown;

    /// <summary>ถังขยะเรียกตอนกินลูกนี้ — กันนับซ้ำตอนลูกกระเด้งอยู่ในถัง</summary>
    public void MarkScored() => IsScored = true;

    public void Despawn()
    {
        if (this != null) Destroy(gameObject);
    }

    // ─────────────────────────── หายไปเอง ───────────────────────────

    /// <summary>
    /// ย่อตัวลงแล้วหายไป
    ///
    /// ย่อ ไม่ใช่ fade เพราะวัสดุเป็น URP Lit แบบ Opaque — จะ fade ได้ต้องสลับ Surface Type
    /// เป็น Transparent แล้วแก้ alpha ของ instance ซึ่งทำให้วัสดุแตกเป็นก้อนละ instance
    /// (และผู้เล่นจะเปลี่ยนไปใช้วัสดุ/โมเดลของตัวเองทีหลัง ย่อตัวทำงานกับทุกวัสดุ)
    /// </summary>
    private void BeginVanish()
    {
        _vanishing   = true;
        _vanishStart = Time.time;

        if (shrinkTime <= 0f) FinishVanish();
    }

    private void UpdateVanish()
    {
        float t = shrinkTime <= 0f ? 1f : (Time.time - _vanishStart) / shrinkTime;

        if (t >= 1f) { FinishVanish(); return; }

        transform.localScale = _baseScale * (1f - t);
    }

    private void FinishVanish()
    {
        // บอกลังด้วย ไม่ใช่ Destroy เงียบๆ — ลังเก็บลิสต์ก้อนที่ยังอยู่ในโลกไว้นับเพดาน
        // ถ้าหายไปเองโดยไม่บอก ลิสต์จะค้างเป็นช่องว่างแล้วเพดานจะกินโควตาไปเรื่อยๆ
        if (Origin != null) Origin.ReturnBall(this);
        else                Despawn();
    }

    // ─────────────────────────── Internal ───────────────────────────

    private void SetCollidersEnabled(bool value)
    {
        foreach (var collider in _colliders)
            if (collider != null) collider.enabled = value;
    }

    /// <summary>
    /// ไม่ให้ก้อนกระดาษชนกับตัวผู้เล่นเลย
    ///
    /// จุดปล่อยอยู่หน้ากล้องซึ่งอยู่ในรัศมีของ CharacterController อยู่แล้ว ถ้าให้ชนได้
    /// ลูกจะเด้งใส่หน้าตัวเองทุกครั้งที่ขว้าง (ยิ่งขว้างขึ้นยิ่งเด้งกลับ) แก้ด้วยการเลื่อนจุดปล่อย
    /// ให้ไกลขึ้นไม่พอ เพราะหันหน้าชิดกำแพงแล้วเลื่อนไปอีกก็ยังโดนตัวเอง
    ///
    /// ผลข้างเคียงคือเดินทับก้อนที่ตกบนพื้นแล้วมันจะไม่กระเด็น — ยอมแลก เพราะอาการเด้งใส่หน้า
    /// มันพังกว่ากันเยอะ (ถ้าวันหลังอยากให้เตะได้ ต้องแยก layer ให้ลูกกับผู้เล่นแทน)
    /// </summary>
    private void IgnorePlayerCollision()
    {
        if (_playerColliders == null || _playerColliders.Length == 0 || _playerColliders[0] == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            _playerColliders = player != null
                ? player.GetComponentsInChildren<Collider>(true)
                : new Collider[0];
        }

        foreach (var mine in _colliders)
        {
            if (mine == null || !mine.enabled) continue;

            foreach (var theirs in _playerColliders)
            {
                if (theirs == null || theirs.isTrigger || !theirs.enabled) continue;
                Physics.IgnoreCollision(mine, theirs, true);
            }
        }
    }
}
