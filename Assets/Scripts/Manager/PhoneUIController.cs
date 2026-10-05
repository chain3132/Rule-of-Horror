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
        [Tooltip("แอปกฎ (ปุ่ม 3) — ที่เก็บกฎที่คนปริศนาส่งมา")]
        [SerializeField] private GameObject ruleBookPanel;
        [SerializeField] private TMP_Text timeInPhoneText;

        [SerializeField] private TMP_Text timeText;
        [SerializeField] private GameObject messagePanel;

        private Dictionary<PhoneState, GameObject> _stateMap;
        private bool isSignalJammed = false;
        private Coroutine glitchRoutine;

        /// <summary>แผงระดับแอปทั้งหมด — เปิดได้ทีละตัวเท่านั้น (ดู ChangeHomePanel)</summary>
        private GameObject[] _homePanels;

        private void Awake()
        {
            _stateMap = new Dictionary<PhoneState, GameObject>
            {
                { PhoneState.AppSelection, homePanel },
                { PhoneState.FriendList, friendListPanel },
                { PhoneState.ChatView, chatPanel },
                { PhoneState.FlashLight, flashLightObj },
                { PhoneState.Clock , null},
                { PhoneState.RuleBook , null}
            };

            _homePanels = new[] { homePanel, messagePanel, flashLightPanel, clockPanel, ruleBookPanel };
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
        /// <summary>
        /// เปิดแผงของแอปที่กำลังใช้อยู่ แล้วปิดที่เหลือ
        ///
        /// เดิมเป็น switch ที่แต่ละ case ต้องไล่ SetActive(false) ให้แผงอื่นเองทุกตัว —
        /// พอเพิ่มแอปกฎเข้ามาเป็นตัวที่ 5 วิธีนั้นต้องแก้ทุก case และลืมตัวใดตัวหนึ่งเมื่อไร
        /// จะได้แผงซ้อนกันแบบหาสาเหตุยาก เลยเปลี่ยนเป็นบอกแค่ว่า "ตัวไหนควรเปิด" ตัวเดียว
        /// </summary>
        private void ChangeHomePanel(PhoneState state)
        {
            GameObject active = state switch
            {
                PhoneState.AppSelection                       => homePanel,
                PhoneState.FriendList or PhoneState.ChatView  => messagePanel,
                PhoneState.FlashLight                         => flashLightPanel,
                PhoneState.RuleBook                           => ruleBookPanel,
                _                                             => null,
            };

            foreach (var panel in _homePanels)
                if (panel != null) panel.SetActive(panel == active);
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
