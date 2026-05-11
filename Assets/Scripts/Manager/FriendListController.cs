using System;
using System.Collections;
using Manager;
using Player;
using ScriptableObject;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FriendListController — จัดการรายชื่อ Contact ใน FriendList
///
/// Flow:
///   TimeManager tick → ถึงเวลา → แสดง contact + badge + เสียงแจ้งเตือน
///   contact ที่ triggersRule1OnEnd → ปลดล็อกการลุก (isBlockStanding = false) ตอน unlock
///   ผู้เล่นกด contact → ChatUIController.OpenContactChat()
///   Conversation จบ → delay → BlinkToMode → Rule1.TriggerStartRule()
/// </summary>
public class FriendListController : MonoBehaviour
{
    // ─────────────────────────── Contact Data ───────────────────────────

    [System.Serializable]
    public class ContactEntry
    {
        [Header("Contact Info")]
        public string contactName;

        [Tooltip("ConversationData ของ contact นี้ เรียงตามลำดับที่อยากให้เล่น")]
        public ConversationData[] conversations;

        [Header("Notification — เวลาที่จะ unlock + แจ้งเตือน")]
        public int notifyHour;
        public int notifyMinute;

        [Header("UI")]
        [Tooltip("ปุ่ม contact ใน FriendList (ซ่อนอยู่ตั้งแต่ต้น)")]
        public Button contactButton;

        [Tooltip("Dot/Badge แสดงว่ามีข้อความใหม่")]
        public GameObject notificationBadge;

        [Header("Rule Trigger")]
        [Tooltip("✓ = เมื่อ conversation จบ → เริ่ม Rule1 sequence\n" +
                 "และ → ปลดล็อกการลุกเมื่อ contact นี้ unlock")]
        public bool triggersRule1OnEnd = false;

        // ─── Runtime ───
        [HideInInspector] public bool isUnlocked;
        [HideInInspector] public bool hasBeenNotified;
    }

    // ─────────────────────────── Inspector ───────────────────────────

    [SerializeField] private ContactEntry[] contacts;
    [SerializeField] private ChatUIController chatUI;
    [SerializeField] private Rule1 rule1;

    [Header("Rule1 Start Sequence")]
    [Tooltip("หน่วงหลังอ่านจบก่อน mode change (วินาที)")]
    [SerializeField] private float delayBeforeModeChange = 1.5f;

    [Tooltip("Mode ที่จะเปลี่ยนเป็นก่อน Rule1 เริ่ม")]
    [SerializeField] private GameMode rule1StartMode = GameMode.Tension;

    // ─────────────────────────── Events ───────────────────────────

    public event Action<ContactEntry> OnContactConversationEnd;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        foreach (var contact in contacts)
        {
            // ซ่อน contact ทั้งหมดตั้งแต่ต้น
            if (contact.contactButton != null)
                contact.contactButton.gameObject.SetActive(false);

            if (contact.notificationBadge != null)
                contact.notificationBadge.SetActive(false);

            var c = contact;
            contact.contactButton?.onClick.AddListener(() => OnContactClicked(c));
        }
    }

    private void OnEnable()
    {
        TimeManager.OnTimeChanged += CheckNotifications;
        if (chatUI != null)
            chatUI.OnContactConversationEnd += HandleConversationEnd;
    }

    private void OnDisable()
    {
        TimeManager.OnTimeChanged -= CheckNotifications;
        if (chatUI != null)
            chatUI.OnContactConversationEnd -= HandleConversationEnd;
    }

    // ─────────────────────────── Notification ───────────────────────────

    private void CheckNotifications(int hour, int minute)
    {
        int currentTime = hour * 60 + minute;

        foreach (var contact in contacts)
        {
            if (contact.hasBeenNotified) continue;

            int unlockTime = contact.notifyHour * 60 + contact.notifyMinute;
            if (currentTime < unlockTime) continue;

            contact.isUnlocked      = true;
            contact.hasBeenNotified = true;

            // แสดง contact ใน FriendList
            if (contact.contactButton != null)
                contact.contactButton.gameObject.SetActive(true);

            // แสดง badge
            if (contact.notificationBadge != null)
                contact.notificationBadge.SetActive(true);

            // เสียงแจ้งเตือน
            if (AudioManager.instance != null)
                AudioManager.instance.PlayMessageNotification();

            // ปลดล็อกการลุกเมื่อ contact ที่เกี่ยวกับ Rule1 unlock (18:40)
            if (contact.triggersRule1OnEnd)
                PlayerController.Instance.isBlockStanding = false;
        }
    }

    // ─────────────────────────── Contact Click ───────────────────────────

    private void OnContactClicked(ContactEntry contact)
    {
        if (!contact.isUnlocked) return;

        if (contact.notificationBadge != null)
            contact.notificationBadge.SetActive(false);

        if (chatUI != null)
            chatUI.OpenContactChat(contact);
    }

    // ─────────────────────────── Conversation End ───────────────────────────

    private void HandleConversationEnd(ContactEntry contact)
    {
        OnContactConversationEnd?.Invoke(contact);

        if (contact.triggersRule1OnEnd && rule1 != null)
            StartCoroutine(StartRule1Sequence());
    }

    private IEnumerator StartRule1Sequence()
    {
        if (rule1 == null)
        {
            Debug.LogError("[FriendListController] rule1 reference is null — assign Rule1 in Inspector");
            yield break;
        }

        // หน่วงสักพักหลังอ่านจบ
        yield return new WaitForSeconds(delayBeforeModeChange);

        // เปลี่ยน mode เหมือน Rule2 gameflow (BlinkToMode มี faint + blink)
        GameModeController.instance.BlinkToMode(rule1StartMode);

        // รอ 1 frame ให้ animation เริ่ม (IsEyesOpen ยังเป็น true อยู่ในช่วงแรก)
        yield return null;

        // รอให้ตาปิด (faint/blink กำลังเกิด)
        yield return new WaitUntil(() => !GameModeController.instance.IsEyesOpen);

        // รอให้ตาเปิดสนิทหลัง transition เสร็จ
        yield return new WaitUntil(() => GameModeController.instance.IsEyesOpen);

        // เริ่ม Rule1 ทันทีหลังตาเปิด
        rule1.TriggerStartRule();
    }
}
