using UnityEngine;

/// <summary>
/// แปะบนวัตถุที่กดได้ แล้ว prompt ปุ่มจะขึ้นเองตอนผู้เล่นเดินเข้ามาในรัศมี
///
/// มีไว้เพื่อไม่ต้องไปแก้สคริปต์ของวัตถุทีละตัว — สวิตช์ วิทยุ ตู้ไฟ ตุ๊กตา ศาล จุดนั่ง โทรศัพท์
/// ทุกตัวใช้ component เดียวกันนี้ ต่างกันแค่ค่าใน Inspector
///
/// ของเดิมแต่ละวัตถุถือ TMP_Text ของตัวเองแล้ว SetActive เอง ซึ่งทำให้
///   • ข้อความไม่สม่ำเสมอ แก้คำต้องไล่แก้ทุก prefab
///   • กติกา "สอน 3 ครั้งแล้วเหลือแค่ปุ่ม" ทำไม่ได้ เพราะไม่มีใครรู้ยอดรวม
/// ตัวนี้ส่งต่อให้ KeyPromptManager เป็นคนนับและตัดสินใจแทน
///
/// ใช้ trigger collider ที่อยู่บน GameObject นี้ (ตั้ง Is Trigger ไว้) ถ้าไม่มีจะใช้ระยะห่างแทน
/// </summary>
[DisallowMultipleComponent]
public class KeyPromptTrigger : MonoBehaviour
{
    [Header("ข้อความ")]
    [Tooltip("รหัสของ prompt — ตัวที่สอนเรื่องเดียวกันต้องใช้รหัสเดียวกัน เช่นตุ๊กตาทุกตัวใช้ rule4.doll\n" +
             "เว้นว่าง = ใช้ชื่อ GameObject (ซึ่งแปลว่าแต่ละตัวจะสอนแยกกัน)")]
    [SerializeField] private string promptId;

    [Tooltip("ปุ่มที่โชว์ในกรอบ เช่น E / Q ค้าง / คลิกซ้าย ค้าง")]
    [SerializeField] private string key = "E";

    [Tooltip("สิ่งที่ปุ่มนั้นทำ เช่น เปิดไฟ — จะหายไปเองหลังสอนครบ 3 ครั้ง")]
    [SerializeField] private string action = "";

    [Header("ขอบเขต")]
    [Tooltip("จุดที่ prompt ไปเกาะ — เว้นว่างจะเกาะที่ตัว GameObject นี้")]
    [SerializeField] private Transform anchor;

    [Tooltip("ไม่มี trigger collider ก็ใช้ระยะนี้แทน (เมตร) — 0 = ต้องมี collider เท่านั้น")]
    [SerializeField] private float fallbackRadius = 2.5f;

    [Tooltip("tag ของผู้เล่น")]
    [SerializeField] private string playerTag = "Player";

    [Header("เงื่อนไข")]
    [Tooltip("✗ = ยังกดไม่ได้ prompt จะไม่ขึ้น — ให้กฎเป็นคนเปิดผ่าน SetAvailable() ตอนถึงตา")]
    [SerializeField] private bool availableFromStart = true;

    private Transform _player;
    private bool      _available;
    private bool      _showing;
    private bool      _hasTrigger;

    private string Id     => string.IsNullOrEmpty(promptId) ? name : promptId;
    private Transform Pin  => anchor != null ? anchor : transform;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        _available = availableFromStart;

        foreach (var collider in GetComponents<Collider>())
            if (collider.isTrigger) { _hasTrigger = true; break; }
    }

    private void OnDisable() => Hide();   // ปิดวัตถุทิ้งแล้ว prompt ต้องไม่ค้างกลางจอ

    private void Update()
    {
        // ไม่มี trigger collider → วัดระยะเอง เผื่อวัตถุที่ไม่อยากใส่ collider เพิ่ม
        if (_hasTrigger || fallbackRadius <= 0f) return;

        if (_player == null)
        {
            var found = GameObject.FindGameObjectWithTag(playerTag);
            if (found == null) return;
            _player = found.transform;
        }

        SetNear(Vector3.Distance(_player.position, Pin.position) <= fallbackRadius);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag)) SetNear(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(playerTag)) SetNear(false);
    }

    // ─────────────────────────── API ───────────────────────────

    /// <summary>
    /// เปิด/ปิดว่าตอนนี้กดได้ไหม — กฎเรียกตอนถึงตาของวัตถุนั้น
    /// (เช่นสวิตช์ไฟยังกดไม่ได้จนกว่า Rule 2 จะเริ่ม)
    /// </summary>
    public void SetAvailable(bool value)
    {
        if (_available == value) return;

        _available = value;
        if (!_available) Hide();
    }

    /// <summary>เปลี่ยนข้อความระหว่างเกม — เช่นสวิตช์ที่สลับระหว่าง "เปิดไฟ" กับ "ปิดไฟ"</summary>
    public void SetText(string newKey, string newAction)
    {
        key    = newKey;
        action = newAction;

        if (!_showing) return;

        // กำลังโชว์อยู่ ต้องยิงใหม่ให้ข้อความเปลี่ยนทันที — Show ตัวเดิมที่ยังค้างจะถูกมองข้าม
        KeyPromptManager.instance.Hide(Id);
        _showing = false;
        Show();
    }

    // ─────────────────────────── Internal ───────────────────────────

    private void SetNear(bool near)
    {
        if (near && _available) Show();
        else                    Hide();
    }

    private void Show()
    {
        if (_showing || KeyPromptManager.instance == null) return;

        _showing = true;
        KeyPromptManager.instance.Show(Id, key, action, Pin);
    }

    private void Hide()
    {
        if (!_showing || KeyPromptManager.instance == null) return;

        _showing = false;
        KeyPromptManager.instance.Hide(Id);
    }

    private void OnDrawGizmosSelected()
    {
        if (fallbackRadius <= 0f) return;

        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
        Gizmos.DrawWireSphere(Pin.position, fallbackRadius);
    }
}
