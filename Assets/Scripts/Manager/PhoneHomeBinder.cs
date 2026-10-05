using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Manager
{
    /// <summary>
    /// ตัวเชื่อมหน้าโฮมของโทรศัพท์ — นาฬิกาใหญ่ แบต สัญญาณ การ์ดแจ้งเตือน และจุดแดงบนไอคอนแอป
    ///
    /// เหมือน RuleBookPanelBinder คือทุกช่องเว้นว่างได้ ใส่เฉพาะที่จัด UI ไว้แล้ว
    /// ตัวนี้ไม่สร้าง layout ไม่ยุ่งกับขนาด/สี แค่เอาค่ามาหยอดตอนข้อมูลเปลี่ยน
    ///
    /// การ์ดแจ้งเตือนมี 2 แบบให้เลือกเหมือนกัน: ทำการ์ดเดียวไว้แล้วให้มันสลับข้อมูล (ตามม็อกอัพ
    /// ที่มีใบเดียว) หรือใส่ prefab ให้มันสร้างเท่าจำนวนที่ค้างอยู่
    /// </summary>
    public class PhoneHomeBinder : MonoBehaviour
    {
        [Header("ข้อมูล")]
        [SerializeField] private PhoneStatusController status;
        [SerializeField] private PhoneNotificationCenter notifications;

        [Header("แถบสถานะ")]
        [SerializeField] private TMP_Text statusClockText;
        [SerializeField] private TMP_Text batteryText;
        [Tooltip("รูปแบบแบต — {0} = ตัวเลข")]
        [SerializeField] private string batteryFormat = "{0}%";
        [Tooltip("ขีดสัญญาณเรียงจากซ้าย ขีดที่เกินระดับจะถูกหรี่ลง")]
        [SerializeField] private Graphic[] signalBars;
        [Range(0f, 1f)]
        [SerializeField] private float dimmedBarAlpha = 0.25f;

        [Header("นาฬิกาใหญ่กลางจอ")]
        [SerializeField] private TMP_Text bigClockText;
        [Tooltip("บรรทัดใต้นาฬิกา เช่น คืนวันอังคาร — ไม่เปลี่ยนตามเวลา ตั้งค่าไว้เฉยๆ")]
        [SerializeField] private TMP_Text dayLabelText;
        [SerializeField] private string dayLabel = "คืนวันอังคาร";

        [Header("การ์ดแจ้งเตือน — แบบใบเดียว")]
        [Tooltip("ทั้งการ์ด จะถูกซ่อนเมื่อไม่มีอะไรค้าง")]
        [SerializeField] private GameObject notificationCard;
        [SerializeField] private TMP_Text notificationTitleText;
        [SerializeField] private TMP_Text notificationBodyText;
        [Tooltip("มุมขวาบนของการ์ด — จะขึ้นว่า ตอนนี้ หรือเวลาที่มันเด้ง")]
        [SerializeField] private TMP_Text notificationTimeText;
        [SerializeField] private Image notificationIcon;

        [Header("การ์ดแจ้งเตือน — แบบหลายใบ")]
        [SerializeField] private Transform notificationRoot;
        [Tooltip("prefab การ์ด — ต้องมี TMP_Text อย่างน้อย 3 ตัว (ชื่อ / เนื้อหา / เวลา)")]
        [SerializeField] private GameObject notificationCardPrefab;

        [Header("จุดแดงบนไอคอนแอป")]
        [Tooltip("เรียงตามลำดับปุ่มเลข 1/2/3 — ช่องไหนไม่มีก็เว้นว่าง")]
        [SerializeField] private GameObject[] appBadges;
        [Tooltip("(optional) ตัวเลขบนจุดแดง เรียงลำดับเดียวกับ appBadges")]
        [SerializeField] private TMP_Text[] appBadgeCounts;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void OnEnable()
        {
            if (status != null)
            {
                status.OnClockChanged   += HandleClock;
                status.OnBatteryChanged += HandleBattery;
                status.OnSignalChanged  += HandleSignal;

                HandleBattery(status.Battery);
                HandleSignal(status.SignalBars);
            }

            if (notifications != null) notifications.OnNotificationsChanged += RefreshNotifications;

            if (TimeManager.instance != null)
                HandleClock(TimeManager.instance.currentHour, TimeManager.instance.currentMinute);

            if (dayLabelText != null) dayLabelText.text = dayLabel;

            RefreshNotifications();
        }

        private void OnDisable()
        {
            if (status != null)
            {
                status.OnClockChanged   -= HandleClock;
                status.OnBatteryChanged -= HandleBattery;
                status.OnSignalChanged  -= HandleSignal;
            }

            if (notifications != null) notifications.OnNotificationsChanged -= RefreshNotifications;
        }

        // ─────────────────────────── Status Bar ───────────────────────────

        private void HandleClock(int hour, int minute)
        {
            string text = $"{hour:00}:{minute:00}";
            if (statusClockText != null) statusClockText.text = text;
            if (bigClockText    != null) bigClockText.text    = text;

            // เวลาเดินแล้ว "ตอนนี้" บนการ์ดอาจต้องกลายเป็นเวลาจริง
            RefreshNotificationTimes();
        }

        private void HandleBattery(int percent)
        {
            if (batteryText != null) batteryText.text = string.Format(batteryFormat, percent);
        }

        private void HandleSignal(int bars)
        {
            if (signalBars == null) return;

            for (int i = 0; i < signalBars.Length; i++)
            {
                if (signalBars[i] == null) continue;

                var color = signalBars[i].color;
                color.a = i < bars ? 1f : dimmedBarAlpha;
                signalBars[i].color = color;
            }
        }

        // ─────────────────────────── Notifications ───────────────────────────

        /// <summary>วาดการ์ดแจ้งเตือน + จุดแดงใหม่ทั้งหมด</summary>
        public void RefreshNotifications()
        {
            ClearSpawned();

            var items = notifications != null ? notifications.Items : null;
            int count = items?.Count ?? 0;

            // การ์ดใบเดียว — โชว์อันล่าสุด
            if (notificationCard != null) notificationCard.SetActive(count > 0);
            if (count > 0) FillSingleCard(items[0]);

            // การ์ดหลายใบ
            if (notificationRoot != null && notificationCardPrefab != null)
            {
                for (int i = 0; i < count; i++)
                {
                    var card  = Instantiate(notificationCardPrefab, notificationRoot);
                    var texts = card.GetComponentsInChildren<TMP_Text>();
                    if (texts.Length > 0) texts[0].text = items[i].title;
                    if (texts.Length > 1) texts[1].text = items[i].body;
                    if (texts.Length > 2) texts[2].text = notifications.TimeLabel(items[i]);
                    _spawned.Add(card);
                }
            }

            RefreshBadges();
        }

        private void FillSingleCard(PhoneNotification item)
        {
            if (notificationTitleText != null) notificationTitleText.text = item.title;
            if (notificationBodyText  != null) notificationBodyText.text  = item.body;
            if (notificationTimeText  != null) notificationTimeText.text  = notifications.TimeLabel(item);

            if (notificationIcon == null || item.icon == null) return;
            notificationIcon.sprite = item.icon;
        }

        /// <summary>อัปเดตเฉพาะข้อความเวลา — เรียกทุกนาที ไม่ต้องสร้างการ์ดใหม่ทั้งชุด</summary>
        private void RefreshNotificationTimes()
        {
            if (notifications == null || notifications.Items.Count == 0) return;
            if (notificationTimeText == null) return;

            notificationTimeText.text = notifications.TimeLabel(notifications.Items[0]);
        }

        private void RefreshBadges()
        {
            if (appBadges == null) return;

            for (int i = 0; i < appBadges.Length; i++)
            {
                int n = notifications != null ? notifications.CountForApp(i) : 0;

                if (appBadges[i] != null) appBadges[i].SetActive(n > 0);

                if (appBadgeCounts != null && i < appBadgeCounts.Length && appBadgeCounts[i] != null)
                    appBadgeCounts[i].text = n.ToString();
            }
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
        }
    }
}
