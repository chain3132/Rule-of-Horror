using InputSystem;
using TMPro;
using UnityEngine;

/// <summary>
/// ติดไว้บน GameObject โทรศัพท์ที่วางอยู่บนพื้นในเขตสีแดง
///
/// เมื่อผู้เล่นเดินเข้า trigger collider → แสดงข้อความ interaction
/// กด E (OnInteractPressed) → โทรศัพท์หาย → แจ้ง Rule1.OnPhonePickedUp()
/// </summary>
public class PhoneGroundPickup : MonoBehaviour
{
    [SerializeField] private Rule1 rule1;
    [SerializeField] private InputHandler inputHandler;

    [Tooltip("ข้อความที่แสดงตอนผู้เล่นเข้ามาใกล้ (เช่น 'กด E เพื่อเก็บโทรศัพท์')")]
    [SerializeField] private TMP_Text interactionText;

    private bool _isSubscribed;

    // ─────────────────────────── Trigger ───────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || _isSubscribed) return;

        if (interactionText != null) interactionText.gameObject.SetActive(true);

        if (inputHandler != null)
        {
            inputHandler.OnInteractPressed += Interact;
            _isSubscribed = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (interactionText != null) interactionText.gameObject.SetActive(false);
        Unsubscribe();
    }

    // ─────────────────────────── Interaction ───────────────────────────

    private void Interact()
    {
        Unsubscribe();
        if (interactionText != null) interactionText.gameObject.SetActive(false);

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
    }
}
