using System.Collections.Generic;
using Enum;
using ScriptableObject;
using UnityEngine;

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

        
        [SerializeField] private GameObject messagePanel;

        private Dictionary<PhoneState, GameObject> _stateMap;

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
    }
}
