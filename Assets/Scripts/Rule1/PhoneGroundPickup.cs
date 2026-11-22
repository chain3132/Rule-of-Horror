using InputSystem;
using TMPro;
using UnityEngine;

/// <summary>
/// ติดไว้บน GameObject โทรศัพท์ที่วางอยู่บนพื้นในเขตสีแดง
///
/// เมื่อผู้เล่นเดินเข้า trigger collider → ปุ่ม "E เก็บโทรศัพท์" ขึ้นบนจอ
/// กด E (OnInteractPressed) → โทรศัพท์หาย → แจ้ง Rule1.OnPhonePickedUp()
/// </summary>
public class PhoneGroundPickup : MonoBehaviour
{
    private const string PromptId = "interact.phone";

    [SerializeField] private Rule1 rule1;
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("ข้อความลอยในโลกแบบเดิม — ไม่ใช้แล้ว (ปุ่มขึ้นบนจอแทน) ใส่ไว้ก็จะถูกซ่อนตลอด")]
    [SerializeField] private TMP_Text interactionText;

    [Tooltip("คำที่ขึ้นข้างปุ่ม E บนจอ")]
    [SerializeField] private string promptAction = "เก็บโทรศัพท์";

    private bool _isSubscribed;
    private readonly InteractPromptHandle _prompt = new InteractPromptHandle();

    private void Awake()
    {
        if (interactionText != null) interactionText.gameObject.SetActive(false);
    }

    // ─────────────────────────── Trigger ───────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || _isSubscribed) return;

        _prompt.Set(PromptId, promptAction);

        if (inputHandler != null)
        {
            inputHandler.OnInteractPressed += Interact;
            _isSubscribed = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _prompt.Clear();
        Unsubscribe();
    }

    // ─────────────────────────── Interaction ───────────────────────────

    private void Interact()
    {
        Unsubscribe();
        _prompt.Clear();

        // ซ่อน pickup object
        gameObject.SetActive(false);

        // แจ้ง Rule1
        rule1.OnPhonePickedUp();
    }

    // ─────────────────────────── Cleanup ───────────────────────────

    private void Unsubscribe()
    {
        if (_isSubscribed && inputHandler != null)
        {
            inputHandler.OnInteractPressed -= Interact;
            _isSubscribed = false;
        }
    }

    private void OnDisable()
    {
        // กันกรณี GameObject ถูกปิดก่อนที่ผู้เล่นจะออกจาก trigger
        Unsubscribe();
        _prompt.Clear();
    }
}
