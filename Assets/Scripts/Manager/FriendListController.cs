using System;
using System.Collections;
using System.Collections.Generic;
using Enum;
using InputSystem;
using Manager;
using ScriptableObject;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class FriendListController : MonoBehaviour
{
    // ─────────────────────────── Contact Data ───────────────────────────

    [System.Serializable]
    public class ContactEntry
    {
        [Header("Contact Info")]
        public string contactName;

        [Tooltip("รูปโปรไฟล์ของ contact นี้ — ใช้ทั้งในลิสต์และหัวแชท")]
        public Sprite avatar;

        [Tooltip("ข้อความทั้งหมดที่คนนี้ส่งมาตลอดคืน พร้อมเวลาของแต่ละชุด เรียงตามเวลา — " +
                 "คนปริศนาคนเดียวส่ง 5 รอบ คือ 1 contact ที่มี 5 ข้อความ ไม่ใช่ 5 contact")]
        public ScheduledMessage[] messages;

        [Header("UI")]
        [Tooltip("ปุ่ม contact ใน FriendList (ซ่อนอยู่ตั้งแต่ต้น)")]
        public Button contactButton;

        [Tooltip("Dot/Badge แสดงว่ามีข้อความใหม่")]
        public GameObject notificationBadge;

        [Tooltip("(optional) กรอบ/แถบไฮไลต์ที่บอกว่าตัวชี้อยู่ที่ contact นี้ — จอโทรศัพท์คลิกไม่ได้ " +
                 "ต้องมีอะไรบอกว่ากำลังเลือกอันไหนอยู่")]
        public GameObject selectionHighlight;

        [Tooltip("(optional) Text ชื่อบนปุ่ม contact — จะเซ็ตให้ = contactName ตอน Awake")]
        public TMP_Text buttonNameText;

        [Tooltip("(optional) Image รูปโปรไฟล์บนปุ่ม contact — จะเซ็ตให้ = avatar ตอน Awake")]
        public Image buttonAvatarImage;

        [Tooltip("(optional) Text บรรทัดรองใต้ชื่อ — โชว์ข้อความล่าสุดของแชทนี้\n" +
                 "เว้นว่าง = หาลูกของปุ่มที่ชื่อ \"Detail\" ให้เอง")]
        public TMP_Text buttonPreviewText;

        // ─── Runtime ───
        [HideInInspector] public bool isUnlocked;
        [HideInInspector] public bool hasBeenNotified;

        /// <summary>ข้อความที่ส่งถึงแล้ว เรียงตามที่ใส่ไว้ — ใช้หาว่าจะเล่นชุดไหนต่อ</summary>
        public IEnumerable<ScheduledMessage> DeliveredMessages
        {
            get
            {
                if (messages == null) yield break;

                foreach (var message in messages)
                    if (message != null && message.delivered && message.conversation != null)
                        yield return message;
            }
        }

        /// <summary>หาตารางเวลาของบทสนทนาชุดหนึ่ง (null = ไม่ใช่ของ contact นี้)</summary>
        public ScheduledMessage Find(ConversationData conversation)
        {
            if (messages == null || conversation == null) return null;

            foreach (var message in messages)
                if (message != null && message.conversation == conversation) return message;

            return null;
        }
    }

    // ─────────────────────────── Inspector ───────────────────────────

    [SerializeField] private ContactEntry[] contacts;
    [SerializeField] private ChatUIController chatUI;
    [SerializeField] private Rule1 rule1;

    [Header("Keyboard (จอโทรศัพท์คลิกไม่ได้)")]
    [Tooltip("ใช้เช็กว่าตอนนี้เปิดหน้ารายชื่ออยู่ไหม ถึงจะรับปุ่ม W/S/E")]
    [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;
    [SerializeField] private InputHandler inputHandler;

    [Header("Notification")]
    [Tooltip("ศูนย์แจ้งเตือน — เว้นว่างได้ถ้ายังไม่ทำการ์ดบนหน้าโฮม")]
    [SerializeField] private PhoneNotificationCenter notificationCenter;

    [Tooltip("ปุ่มเลขของแอปข้อความ ใช้บอกว่าจุดแดงควรไปอยู่ไอคอนไหน")]
    [SerializeField] private int messageAppIndex;

    [Tooltip("ข้อความบรรทัดรองบนการ์ดแจ้งเตือน — {0} = จำนวนข้อความ")]
    [SerializeField] private string newMessageBody = "ข้อความใหม่ {0} ข้อความ";

    [Header("Rule1 Start Sequence")]
    [Tooltip("หน่วงหลังอ่านจบก่อน mode change (วินาที)")]
    [SerializeField] private float delayBeforeModeChange = 1.5f;

    [Tooltip("Mode ที่จะเปลี่ยนเป็นก่อน Rule1 เริ่ม")]
    [SerializeField] private GameMode rule1StartMode = GameMode.Tension;

    // ─────────────────────────── Events ───────────────────────────

    public event Action<ContactEntry> OnContactConversationEnd;

    /// <summary>ตัวชี้ย้ายไป contact อื่น — UI เอาไปเลื่อน scroll ตามได้</summary>
    public event Action<ContactEntry> OnCursorChanged;

    // ─────────────────────────── Keyboard Runtime ───────────────────────────

    private readonly List<ContactEntry> _navigable = new List<ContactEntry>();
    private int _cursor;
    private bool _wasInList;

    /// <summary>contact ที่ตัวชี้อยู่ตอนนี้ (null = ยังไม่มีใครปลดล็อก)</summary>
    public ContactEntry Highlighted =>
        _cursor >= 0 && _cursor < _navigable.Count ? _navigable[_cursor] : null;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        foreach (var contact in contacts)
        {
            ValidateHighlight(contact);

            // ซ่อน contact ทั้งหมดตั้งแต่ต้น
            if (contact.contactButton != null)
                contact.contactButton.gameObject.SetActive(false);

            if (contact.notificationBadge != null)
                contact.notificationBadge.SetActive(false);

            if (contact.selectionHighlight != null)
                contact.selectionHighlight.SetActive(false);

            // เซ็ตชื่อ + รูปโปรไฟล์บนปุ่มจากข้อมูล contact
            if (contact.buttonNameText != null)
                contact.buttonNameText.text = L10n.T(contact.contactName);
            if (contact.buttonAvatarImage != null && contact.avatar != null)
                contact.buttonAvatarImage.sprite = contact.avatar;

            SetupPreviewText(contact);

            var c = contact;
            contact.contactButton?.onClick.AddListener(() => OnContactClicked(c));
        }
    }

    private void OnEnable()
    {
        TimeManager.OnTimeChanged += CheckNotifications;
        if (chatUI != null)
            chatUI.OnContactConversationEnd += HandleConversationEnd;
        L10n.Changed += RefreshContactNames;
    }

    /// <summary>ชื่อบนปุ่ม contact ถูกเซ็ตครั้งเดียวตอน Awake — เปลี่ยนภาษาแล้วต้องเซ็ตใหม่</summary>
    private void RefreshContactNames()
    {
        foreach (var contact in contacts)
            if (contact.buttonNameText != null)
                contact.buttonNameText.text = L10n.T(contact.contactName);

        RefreshPreviews();
    }

    // ─────────────────────────── ข้อความตัวอย่างใต้ชื่อ ───────────────────────────

    /// <summary>
    /// หา Text บรรทัดรองของแถว แล้วตั้งให้เป็นบรรทัดเดียว
    ///
    /// แถวเป็น Notification_Box.prefab ที่มีลูกชื่อ Detail พิมพ์คำว่า "Detail" ค้างไว้ ไม่มีใครเขียนทับ
    /// ข้อความแชทยาวหลายบรรทัด ถ้าปล่อยตัดคำตามปกติจะล้นทับแถวถัดไป
    /// </summary>
    private static void SetupPreviewText(ContactEntry contact)
    {
        if (contact.buttonPreviewText == null && contact.contactButton != null)
            foreach (var text in contact.contactButton.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "Detail") { contact.buttonPreviewText = text; break; }

        var preview = contact.buttonPreviewText;
        if (preview == null) return;

        preview.textWrappingMode = TextWrappingModes.NoWrap;
        preview.overflowMode     = TextOverflowModes.Truncate;
        preview.text             = "";
    }

    /// <summary>เขียนข้อความล่าสุดลงใต้ชื่อทุกแถว — เรียกตอนมีข้อความเข้า, กลับมาหน้ารายชื่อ, เปลี่ยนภาษา</summary>
    private void RefreshPreviews()
    {
        foreach (var contact in contacts)
        {
            var label = contact.buttonPreviewText;
            if (label == null) continue;

            string text = chatUI != null ? chatUI.GetPreview(contact) : "";
            text = RichTag.Replace(text, "").Replace("\r", "").Replace('\n', ' ').Trim();
            label.text = FitOneLine(label, text);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex RichTag =
        new System.Text.RegularExpressions.Regex("<[^>]*>");

    private const string PreviewEllipsis = "...";

    /// <summary>
    /// ตัดข้อความให้พอดีกรอบ Detail แล้วต่อท้ายด้วย "..." — วัดความกว้างจริงด้วยฟอนต์ของ Text นั้น
    ///
    /// ทำเองแทนการตั้ง Overflow = Ellipsis ของ TMP เพราะแบบนั้นข้อความภาษาไทยยังล้นกรอบอยู่
    /// (ฟอนต์ไทยไม่มีตัว "…" ให้ TMP ใช้ และภาษาไทยไม่มีเว้นวรรคให้ตัด) — ใช้จุดสามตัว เพราะฟอนต์มีแน่
    ///
    /// ไม่ตัดกลางสระบน/ล่างหรือวรรณยุกต์ (ตัวที่ซ้อนบนพยัญชนะ) ไม่งั้นพยัญชนะตัวสุดท้ายจะเสียสระไป
    /// </summary>
    private static string FitOneLine(TMP_Text label, string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var   rt    = label.rectTransform;
        float width = rt.rect.width - label.margin.x - label.margin.z;
        if (width <= 1f) return text;   // ยังไม่มีขนาด (layout ยังไม่คำนวณ) — ปล่อยให้ Truncate ของ TMP ช่วย

        if (label.GetPreferredValues(text).x <= width) return text;

        // หาจำนวนตัวอักษรมากที่สุดที่ต่อ "..." แล้วยังไม่เกินกรอบ
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (label.GetPreferredValues(text.Substring(0, mid) + PreviewEllipsis).x <= width) lo = mid;
            else hi = mid - 1;
        }

        while (lo > 0 && lo < text.Length && IsAttachedChar(text[lo])) lo--;

        return text.Substring(0, lo).TrimEnd() + PreviewEllipsis;
    }

    /// <summary>ตัวที่ต้องอยู่ติดกับตัวหน้า — สระบน/ล่าง วรรณยุกต์ ไม้หันอากาศ (ไทยเป็น NonSpacingMark) และครึ่งหลังของ emoji</summary>
    private static bool IsAttachedChar(char c) =>
        char.IsLowSurrogate(c) ||
        char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark ||
        char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.SpacingCombiningMark;

    private void OnDisable()
    {
        TimeManager.OnTimeChanged -= CheckNotifications;
        if (chatUI != null)
            chatUI.OnContactConversationEnd -= HandleConversationEnd;
        L10n.Changed -= RefreshContactNames;
    }

    // ─────────────────────────── Notification ───────────────────────────

    /// <summary>
    /// ถึงเวลาของข้อความไหนบ้างแล้ว — ไล่ทุกข้อความของทุกคน ไม่ใช่ทุกคนคนละครั้ง
    ///
    /// คนปริศนาส่งมาทั้งคืน 5 รอบจากเบอร์เดิม รายชื่อจึงมีแถวเดียวแต่เด้งแจ้งเตือนได้หลายครั้ง
    /// </summary>
    private void CheckNotifications(int hour, int minute)
    {
        int currentTime = hour * 60 + minute;

        foreach (var contact in contacts)
        {
            if (contact.messages == null) continue;

            foreach (var message in contact.messages)
            {
                if (message == null || message.delivered) continue;
                if (currentTime < message.TotalMinutes) continue;

                Deliver(contact, message);
            }
        }
    }

    /// <summary>ข้อความชุดหนึ่งส่งถึงแล้ว</summary>
    private void Deliver(ContactEntry contact, ScheduledMessage message)
    {
        message.delivered = true;

        bool isFirstFromThisContact = !contact.isUnlocked;
        contact.isUnlocked      = true;
        contact.hasBeenNotified = true;

        // แถวในรายชื่อโผล่ครั้งเดียวตอนข้อความแรกของคนนี้ — รอบถัดไปแถวมีอยู่แล้ว
        if (isFirstFromThisContact && contact.contactButton != null)
            contact.contactButton.gameObject.SetActive(true);

        if (contact.notificationBadge != null)
            contact.notificationBadge.SetActive(true);

        if (AudioManager.instance != null)
            AudioManager.instance.PlayMessageNotification();

        // การ์ดบนหน้าโฮม — ผู้เล่นที่ยังไม่เปิดโทรศัพท์จะได้เห็นว่ามีอะไรรออยู่ตอนเปิดมา
        if (notificationCenter != null)
        {
            int unread = UnreadCount(contact);
            notificationCenter.Push(new PhoneNotification
            {
                title      = contact.contactName,
                body       = string.Format(newMessageBody, unread),
                bodyFormat = newMessageBody,   // แปลตอนวาด — ดู PhoneNotification.DisplayBody
                bodyArg    = unread,
                icon       = contact.avatar,
                appIndex   = messageAppIndex,
                source     = contact
            });
        }

        if (isFirstFromThisContact) RebuildNavigation();
        RefreshPreviews();

        // ข้อความกฎ 1 (18:40) ไม่หยุดนาฬิกาแล้ว — เดิมหยุดตรงนี้ กฎ 1 เลยเริ่มตอนนาฬิกาโชว์ 18:40
        // ทั้งที่แชทบอกว่ากฎเริ่ม 19:00 ตอนนี้นาฬิกาเดินต่อ แล้ว Rule1 หยุดไว้เองที่เวลาเริ่มของมัน
        // (Rule1.HoldClockAtStart) — ยังไม่อ่าน/ยังไม่นั่งก็ค้างอยู่ที่ 19:00 กฎ 2 จึงไม่แทรก
    }

    /// <summary>ข้อความที่ส่งถึงแล้วแต่ผู้เล่นยังอ่านไม่จบ — ใช้เขียนบนการ์ดและจุดแดง</summary>
    private int UnreadCount(ContactEntry contact)
    {
        if (contact.messages == null) return 0;

        int n = 0;
        foreach (var message in contact.messages)
            if (message != null && message.delivered && !message.read) n++;

        return n;
    }

    /// <summary>
    /// มีข้อความที่ส่งถึงแล้วแต่ยังอ่านและตอบไม่จบไหม — ทุก contact รวมกัน กฎจะยังไม่เริ่มจนกว่าจะเป็น false
    ///
    /// นับเฉพาะชุดที่มีบทสนทนาจริง ช่องที่ลืมใส่บทไม่มีทางอ่านจบ ถ้านับรวมกฎจะไม่มีวันเริ่ม
    /// </summary>
    public bool HasUnreadMessages
    {
        get
        {
            foreach (var contact in contacts)
                foreach (var message in contact.DeliveredMessages)
                    if (!message.read) return true;

            return false;
        }
    }

    /// <summary>จุดแดงจะหายก็ต่อเมื่ออ่านครบทุกชุดที่ส่งมาแล้ว ไม่ใช่แค่เปิดแชทผ่านๆ</summary>
    private void RefreshBadge(ContactEntry contact)
    {
        if (contact.notificationBadge != null)
            contact.notificationBadge.SetActive(UnreadCount(contact) > 0);
    }

    // ─────────────────────────── Keyboard Navigation ───────────────────────────

    /// <summary>
    /// เลื่อนรายชื่อด้วย W/S แล้วกด E เพื่อเปิด
    ///
    /// ต้องมีเพราะจอโทรศัพท์ถ่ายผ่าน PhoneCamera ลง RenderTexture — ตัวชี้เมาส์อยู่บนจอจริง
    /// มันไม่มีทางไปโดน Button ที่อยู่ในพื้นผิว 3D ได้เลย ปุ่มที่มีอยู่จึงกดไม่ได้จริงในเกม
    /// (ยังเก็บ Button ไว้ เผื่อวันหลังอยากเทสด้วยการคลิกใน Scene view)
    /// </summary>
    private void Update()
    {
        // กลับมาหน้ารายชื่อ (จากแชท/หน้าโฮม) — แชทที่เพิ่งคุยไปเปลี่ยนข้อความล่าสุดแล้ว
        bool inList = phoneSystem != null && phoneSystem.CurrentState == PhoneState.FriendList;
        if (inList && !_wasInList) RefreshPreviews();
        _wasInList = inList;

        if (inputHandler == null || phoneSystem == null) return;
        if (!inList) return;
        if (_navigable.Count == 0) return;

        if (inputHandler.WasNavUpPressed())   MoveCursor(-1);
        if (inputHandler.WasNavDownPressed()) MoveCursor(1);

        if (!inputHandler.WasConfirmPressed()) return;

        var selected = Highlighted;
        if (selected != null) OnContactClicked(selected);
    }

    /// <summary>เลื่อนตัวชี้ — วนหัวท้ายได้ รายชื่อสั้นมาก การชนขอบแล้วหยุดจะรู้สึกเหมือนปุ่มเสีย</summary>
    public void MoveCursor(int delta)
    {
        if (_navigable.Count == 0) return;

        _cursor = ((_cursor + delta) % _navigable.Count + _navigable.Count) % _navigable.Count;
        ApplyHighlight();
    }

    /// <summary>สร้างรายการที่เลื่อนได้ใหม่ — เรียกทุกครั้งที่มี contact ปลดล็อกเพิ่ม</summary>
    private void RebuildNavigation()
    {
        var keep = Highlighted;

        _navigable.Clear();
        foreach (var contact in contacts)
            if (contact.isUnlocked) _navigable.Add(contact);

        int index = keep != null ? _navigable.IndexOf(keep) : -1;
        _cursor = index >= 0 ? index : 0;   // contact เดิมหายไปแล้วค่อยเด้งกลับหัวรายการ
        ApplyHighlight();
    }

    /// <summary>
    /// กัน selectionHighlight ที่ชี้มาที่ตัวแถวเอง (หรือตัวแม่ของแถว)
    ///
    /// ถ้าปล่อยผ่าน ApplyHighlight จะ SetActive(false) ทับตัวแถวที่ไม่ได้เลือก กลายเป็นรายชื่อ
    /// โชว์ทีละตัว ซึ่งดูเหมือนบั๊กของระบบเลื่อนมากกว่าจะเดาได้ว่าต่อผิดช่อง
    /// ไฮไลต์ต้องเป็น "ลูก" ของแถว เช่นกรอบหรือแถบพื้นหลัง ไม่ใช่ตัวแถว
    /// </summary>
    private void ValidateHighlight(ContactEntry contact)
    {
        if (contact.selectionHighlight == null || contact.contactButton == null) return;

        // IsChildOf คืน true กับตัวเองด้วย เลยครอบทั้งเคส "เป็นตัวเดียวกัน" และ "เป็นตัวแม่"
        if (!contact.contactButton.transform.IsChildOf(contact.selectionHighlight.transform)) return;

        Debug.LogError(
            $"[FriendList] '{contact.contactName}': Selection Highlight ชี้มาที่ตัวแถวเอง " +
            $"('{contact.selectionHighlight.name}') ทำให้แถวที่ไม่ได้เลือกถูกปิดไปด้วย " +
            "ใส่ลูกของแถวที่เป็นกรอบ/แถบไฮไลต์แทน — ตอนนี้ขอข้ามช่องนี้ไว้ก่อน",
            contact.selectionHighlight);

        contact.selectionHighlight = null;
    }

    private void ApplyHighlight()
    {
        var selected = Highlighted;

        foreach (var contact in contacts)
            if (contact.selectionHighlight != null)
                contact.selectionHighlight.SetActive(contact == selected);

        OnCursorChanged?.Invoke(selected);
    }

    // ─────────────────────────── Contact Click ───────────────────────────

    private void OnContactClicked(ContactEntry contact)
    {
        if (!contact.isUnlocked) return;

        // การ์ดบนหน้าโฮมหายตอนเปิดดู (เห็นแล้ว) แต่จุดแดงยังอยู่จนกว่าจะอ่านจบจริง
        if (notificationCenter != null)
            notificationCenter.ClearFor(contact);

        if (chatUI != null)
            chatUI.OpenContactChat(contact);
    }

    // ─────────────────────────── Conversation End ───────────────────────────

    private void HandleConversationEnd(ContactEntry contact, ConversationData conversation)
    {
        var message = contact.Find(conversation);
        if (message != null) message.read = true;

        RefreshBadge(contact);
        OnContactConversationEnd?.Invoke(contact);

        // ธง Rule1 อยู่ที่ข้อความชุดนั้น ไม่ใช่ที่ตัวคน — คนปริศนาส่งมาทั้งคืน
        // มีแค่ชุด 18:40 ชุดเดียวที่อ่านจบแล้วต้องเข้า Rule 1
        // อ่านย้อนหลังตอนเลยเวลากฎ 1 ไปแล้ว (ปุ่มเทสข้ามเวลา) = อ่านเฉยๆ โทรศัพท์ต้องไม่หาย
        if (message != null && message.triggersRule1OnEnd && rule1 != null && rule1.CanStart)
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

        // อ่านจบก่อน 19:00 → รอนาฬิกาเดินถึงเวลาเริ่มของกฎ 1 ก่อน (นาฬิกาหยุดค้างที่ 19:00 ให้เอง)
        yield return rule1.WaitForStartTime();

        // รออ่านข้อความคนอื่นให้จบและรอไปนั่งที่ศาลาก่อน เหมือนกฎ 2-6 — เดิมเริ่มทันทีที่อ่านแชทจบ
        // ผู้เล่นที่ยืนอ่านอยู่ที่อื่นจึงโดนกฎ 1 ทั้งที่ยังไม่ได้นั่ง (ตอนนี้นาฬิกาหยุดอยู่ที่ 19:00 แล้ว)
        yield return RuleSystem.RuleBase.WaitForMessagesAndSeat(remindAs: rule1);

        // ระหว่างรอนั่ง ปุ่มเทสอาจข้ามไปกฎอื่นแล้ว — ต้องเช็กก่อนกะพริบเข้า Tension
        if (!rule1.CanStart) yield break;

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
