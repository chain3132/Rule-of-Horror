using System;
using System.Collections.Generic;
using UnityEngine;

namespace Manager
{
    /// <summary>
    /// ศูนย์รวมการแจ้งเตือนของโทรศัพท์ — การ์ดบนหน้าโฮม และจุดแดงบนไอคอนแอป
    ///
    /// แยกออกมาจาก FriendListController เพราะหน้าโฮมไม่ควรต้องรู้จักระบบแชท
    /// วันหลังถ้ากฎข้อใหม่หรือแอปอื่นอยากเด้งแจ้งเตือนบ้าง ก็แค่เรียก Push() เหมือนกัน
    ///
    /// ตัวนี้ไม่มี UI — PhoneHomeBinder เป็นคนวาด
    /// </summary>
    public class PhoneNotificationCenter : MonoBehaviour
    {
        [Header("การแสดงเวลา")]
        [Tooltip("เด้งมาไม่เกินกี่นาที (เวลาในเกม) ถึงจะเขียนว่า \"ตอนนี้\" แทนเวลาจริง")]
        [SerializeField] private int recentMinutes = 5;

        [SerializeField] private string recentLabel = "ตอนนี้";

        [Tooltip("✓ = เด้งอันใหม่แล้วอันเก่าของผู้ส่งคนเดิมถูกแทนที่ ไม่กองซ้อนกัน")]
        [SerializeField] private bool replacePerSource = true;

        /// <summary>รายการแจ้งเตือนเปลี่ยน — UI วาดใหม่ได้เลย</summary>
        public event Action OnNotificationsChanged;

        private readonly List<PhoneNotification> _items = new List<PhoneNotification>();

        /// <summary>ใหม่สุดอยู่หน้าสุด</summary>
        public IReadOnlyList<PhoneNotification> Items => _items;

        public bool HasAny => _items.Count > 0;

        /// <summary>จำนวนแจ้งเตือนค้างของแอปนั้น — เอาไปใส่จุดแดงบนไอคอน</summary>
        public int CountForApp(int appIndex)
        {
            int n = 0;
            foreach (var item in _items) if (item.appIndex == appIndex) n++;
            return n;
        }

        /// <summary>
        /// เพิ่มการแจ้งเตือน — เวลาจะถูกประทับจากนาฬิกาในเกมให้เองถ้าไม่ได้ใส่มา
        /// </summary>
        public void Push(PhoneNotification notification)
        {
            if (notification == null) return;

            if (notification.hour == 0 && notification.minute == 0 && TimeManager.instance != null)
            {
                notification.hour   = TimeManager.instance.currentHour;
                notification.minute = TimeManager.instance.currentMinute;
            }

            if (replacePerSource && notification.source != null)
                _items.RemoveAll(i => ReferenceEquals(i.source, notification.source));

            _items.Insert(0, notification);
            OnNotificationsChanged?.Invoke();
        }

        /// <summary>ทางลัดสำหรับเคสที่ใช้บ่อยที่สุด — ข้อความใหม่จาก contact</summary>
        public void Push(string title, string body, Sprite icon, int appIndex, object source)
            => Push(new PhoneNotification
            {
                title = title, body = body, icon = icon, appIndex = appIndex, source = source
            });

        /// <summary>ผู้เล่นเปิดอ่านแล้ว — เอาการแจ้งเตือนของเจ้าของนั้นออก</summary>
        public void ClearFor(object source)
        {
            if (source == null) return;
            if (_items.RemoveAll(i => ReferenceEquals(i.source, source)) > 0)
                OnNotificationsChanged?.Invoke();
        }

        /// <summary>เคลียร์ทั้งแอป — ใช้ตอนผู้เล่นเปิดแอปนั้นขึ้นมาเฉยๆ</summary>
        public void ClearApp(int appIndex)
        {
            if (_items.RemoveAll(i => i.appIndex == appIndex) > 0)
                OnNotificationsChanged?.Invoke();
        }

        public void ClearAll()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            OnNotificationsChanged?.Invoke();
        }

        /// <summary>"ตอนนี้" หรือ "22:47" — ตัดสินจากว่ามันค้างมานานแค่ไหนแล้ว</summary>
        public string TimeLabel(PhoneNotification notification)
        {
            if (notification == null) return string.Empty;
            if (TimeManager.instance == null) return $"{notification.hour:00}:{notification.minute:00}";

            int elapsed = TimeManager.instance.CurrentTotalMinutes - notification.TotalMinutes;
            return elapsed >= 0 && elapsed <= recentMinutes
                 ? recentLabel
                 : $"{notification.hour:00}:{notification.minute:00}";
        }
    }
}
