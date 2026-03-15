using System.Collections;
using System.Collections.Generic;
using Enum;
using ScriptableObject;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Manager
{
    public class PhoneUIController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject homePanel;
        [SerializeField] private GameObject friendListPanel;
        [SerializeField] private GameObject chatPanel;
        [SerializeField] private GameObject flashLightPanel;
        [SerializeField] private GameObject flashLightObj;
        [SerializeField] private GameObject clockPanel;
        [SerializeField] private TMP_Text timeInPhoneText;

        [SerializeField] private TMP_Text timeText;
        [SerializeField] private GameObject messagePanel;

        private Dictionary<PhoneState, GameObject> _stateMap;
        private bool isSignalJammed = false;
        private Coroutine glitchRoutine;

        private void Awake()
        {
            _stateMap = new Dictionary<PhoneState, GameObject>
            {
                { PhoneState.AppSelection, homePanel },
                { PhoneState.FriendList, friendListPanel },
                { PhoneState.ChatView, chatPanel },
                { PhoneState.FlashLight, flashLightObj },
                { PhoneState.Clock , null} 
            };
        }
        void OnEnable()
        {
            TimeManager.OnTimeChanged += UpdatePhoneTime;
        }

        void OnDisable()
        {
            TimeManager.OnTimeChanged -= UpdatePhoneTime;
        }
        
        void UpdatePhoneTime(int hour, int minute)
        {
            if (!isSignalJammed)
            {
                timeInPhoneText.text = $"{hour:00}:{minute:00}";
            }
        }
        public void SetSignalJam(bool jam)
        {
            isSignalJammed = jam;

            if (jam)
            {
                glitchRoutine = StartCoroutine(GlitchTime());
            }
            else
            {
                if (glitchRoutine != null)
                    StopCoroutine(glitchRoutine);

                int h = TimeManager.instance.currentHour;
                int m = TimeManager.instance.currentMinute;

                timeInPhoneText.text = $"{h:00}:{m:00}";
            }

    
        }
        string GetGlitchTime()
        {
            int h = Random.Range(0, 24);
            int m = Random.Range(0, 60);

            return $"{h:00}:{m:00}";
        }
        IEnumerator GlitchTime()
        {
            while (isSignalJammed)
            {
                timeInPhoneText.text = GetGlitchTime();
                yield return new WaitForSeconds(UnityEngine.Random.Range(0.05f, 0.15f));
            }
        }
        public void UpdateState(PhoneState state)
        {
            HideAll();

            if (_stateMap.TryGetValue(state, out var panel))
            {
                if (panel != null)
                {
                    panel.SetActive(true);
                }
                
            }

            ChangeHomePanel(state);
        }
        private void ChangeHomePanel(PhoneState state)
        {
            switch (state)
            {
                case PhoneState.AppSelection:
                    homePanel.SetActive(true);
                    messagePanel.SetActive(false);
                    flashLightPanel.SetActive(false);
                    clockPanel.SetActive(false);

                    break;
                case PhoneState.FriendList:
                case PhoneState.ChatView:
                    homePanel.SetActive(false);
                    flashLightPanel.SetActive(false);
                    clockPanel.SetActive(false);
                    messagePanel.SetActive(true);
                    break;
                case PhoneState.FlashLight:
                    homePanel.SetActive(false);
                    messagePanel.SetActive(false);
                    clockPanel.SetActive(false);
                    flashLightPanel.SetActive(true);
                    break;
                case PhoneState.Clock:
                    homePanel.SetActive(false);
                    messagePanel.SetActive(false);
                    flashLightPanel.SetActive(false);
                    clockPanel.SetActive(true);
                    break;
            }
        }

        private void HideAll()
        {
            friendListPanel.SetActive(false);
            chatPanel.SetActive(false);
            flashLightObj.SetActive(false);
        }

        public void SetTimeText(string text)
        {
            timeText.text = text;
        }
    }
}
