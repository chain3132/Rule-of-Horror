using System;
using InputSystem;
using RuleSystem.Rule;
using TMPro;
using UnityEngine;

/// <summary>
/// วิทยุบนม้านั่งศาลา — ใช้แค่ในกฎ 2 (ไฟดับครั้งแรก เปิดวิทยุได้ยินเสียงคลื่น)
///
/// ปุ่ม "E เปิดวิทยุ" ขึ้นบนจอเฉพาะตอนเข้าใกล้ + กฎ 2 ปลดล็อกให้แล้ว + ยังไม่ได้เปิด
/// กฎอื่นวิทยุใช้ไม่ได้ จึงไม่ขึ้นปุ่มให้ผู้เล่นเข้าใจผิด
/// </summary>
public class RadioControl : MonoBehaviour
{
    private const string PromptId = "interact.radio";

    [SerializeField] private InputHandler inputHandler;

    [Tooltip("ข้อความลอยในโลกแบบเดิม — ไม่ใช้แล้ว (ปุ่มขึ้นบนจอแทน) ใส่ไว้ก็จะถูกซ่อนตลอด")]
    [SerializeField] private TMP_Text interactionText;

    [Tooltip("คำที่ขึ้นข้างปุ่ม E บนจอ")]
    [SerializeField] private string promptAction = "เปิดวิทยุ";

    public RuleSystem.Rule.Rule2 rule2;

    private bool isUnlocked = false;
    private bool _playerInZone;
    private bool _isSubscribed;
    private readonly InteractPromptHandle _prompt = new InteractPromptHandle();

    private void Awake()
    {
        if (interactionText != null) interactionText.gameObject.SetActive(false);
    }

    // ฟังปุ่มตลอดตอนเปิดอยู่ แล้วเช็คเงื่อนไขตอนกดแทน — เดิมฟังเฉพาะตอนเดินเข้าโซน
    // ยืนอยู่ในโซนก่อนกฎปลดล็อกจึงกดไม่ติดจนกว่าจะเดินออกแล้วเข้าใหม่
    private void OnEnable()
    {
        if (inputHandler != null && !_isSubscribed)
        {
            inputHandler.OnInteractPressed += Interact;
            _isSubscribed = true;
        }
    }

    private void OnDisable()
    {
        if (inputHandler != null && _isSubscribed) inputHandler.OnInteractPressed -= Interact;
        _isSubscribed = false;
        _playerInZone = false;
        _prompt.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInZone = true;
        RefreshPrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInZone = false;
        RefreshPrompt();
    }

    public void UnlockRadio(bool value)
    {
        isUnlocked = value;
        RefreshPrompt();
    }

    private void RefreshPrompt()
    {
        if (_playerInZone && isUnlocked) _prompt.Set(PromptId, promptAction);
        else                             _prompt.Clear();
    }

    public void Interact()
    {
        if (!_playerInZone || !isUnlocked) return;

        // เปิดได้ครั้งเดียวต่อรอบไฟดับ — Rule2 ปลดล็อกใหม่เองตอนเริ่มรอบถัดไป (ตาย/คืนวนลูป)
        isUnlocked = false;
        RefreshPrompt();

        AudioManager.instance.PlayRadio();
        rule2.OnRadioTurnedOn();
    }
}
