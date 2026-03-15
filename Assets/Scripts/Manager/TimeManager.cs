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
        void OnEnable()
        {
            TimeManager.OnTimeChanged += CheckTime;
            
        }
        void OnDisable()
        {
            TimeManager.OnTimeChanged -= CheckTime;
        }
        void CheckTime(int hour,int minute)
        {
            if(hour == 19 && minute == 0)
            {
                //StartRule();
            }
        }
        void Update()
        {
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
