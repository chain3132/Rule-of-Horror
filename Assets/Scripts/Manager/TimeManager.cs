using System;
using UnityEngine;

namespace Manager
{
    public class TimeManager : MonoBehaviour
    {
        public float realSecondsPerGameMinute = 6f;

        public int currentHour = 19;
        public int currentMinute = 0;
        public int CurrentTotalMinutes => currentHour * 60 + currentMinute;
        private bool isRuleBlockingTime;

        [Tooltip("log ทุกครั้งที่มีคนสั่งหยุด/เดินนาฬิกา พร้อม stack trace — ไว้ไล่ว่าใครแอบปล่อยเวลาเดิน")]
        [SerializeField] private bool logPauseChanges;

        private float timer;

        public static event Action<int,int> OnTimeChanged;
        public static TimeManager instance;
        
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
        /// <summary>
        /// เวลาที่เอาไปโชว์ให้ผู้เล่นอ่าน — "00:10" ไม่ใช่ "24:10"
        ///
        /// นาฬิกาในนี้เดินเป็นเส้นตรงไม่วนรอบ (currentHour++ ไปเรื่อยๆ) โดยตั้งใจ: RuleManager
        /// กับ ScheduledMessage เทียบเวลาด้วย hour*60+minute ถ้าให้ชั่วโมงวนกลับเป็น 0 ตอน
        /// เที่ยงคืน กฎข้อ 6 (24:00-24:40) จะกลายเป็นเวลาที่ "น้อยกว่า" ทุกอย่างในคืนเดียวกัน
        /// แล้วลำดับทั้งคืนจะพัง
        ///
        /// ฝั่งที่ต้องวนรอบคือฝั่งแสดงผลเท่านั้น จึงแก้ที่นี่ที่เดียวแล้วให้ทุก UI เรียกผ่าน
        /// </summary>
        public static string Format(int hour, int minute) => $"{hour % 24:00}:{minute:00}";

        public void SetTime(int hour, int minute)
        {
            currentHour = hour;
            currentMinute = minute;
            OnTimeChanged?.Invoke(currentHour,currentMinute);
        }
        public void IsPauseTime(bool pause)
        {
            if (logPauseChanges && pause != isRuleBlockingTime)
                Debug.Log($"[TimeManager] {(pause ? "หยุด" : "เดิน")}นาฬิกา @ {currentHour:00}:{currentMinute:00}\n" +
                          new System.Diagnostics.StackTrace(1, false));

            isRuleBlockingTime = pause;
        }

        /// <summary>true = นาฬิกาถูกสั่งหยุดอยู่</summary>
        public bool IsPaused => isRuleBlockingTime;
        
        public bool CheckTime(int hour,int minute)
        {
            if(currentHour == hour && currentMinute == minute)
            {
                return true;
            }
            return false;
        }
        void Update()
        {
            if (isRuleBlockingTime) return;

            timer += Time.deltaTime;

            if (timer >= realSecondsPerGameMinute)
            {
                timer = 0;
                AddMinute();
            }
        }

        void AddMinute()
        {
            currentMinute++;

            if (currentMinute >= 60)
            {
                currentMinute = 0;
                currentHour++;
            }

            OnTimeChanged?.Invoke(currentHour,currentMinute);
        }
    }
}
