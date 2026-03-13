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
        
        [SerializeField] private GameObject messagePanel;

        private Dictionary<PhoneState, GameObject> _stateMap;

        private void Awake()
        {
            _stateMap = new Dictionary<PhoneState, GameObject>
            {
                { PhoneState.AppSelection, homePanel },
                { PhoneState.FriendList, friendListPanel },
                { PhoneState.ChatView, chatPanel }
            };
        }

        public void UpdateState(PhoneState state)
        {
            HideAll();

            if (_stateMap.TryGetValue(state, out var panel))
            {
                panel.SetActive(true);
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
                    break;
                case PhoneState.FriendList:
                case PhoneState.ChatView:
                    homePanel.SetActive(false);
                    messagePanel.SetActive(true);
                    break;
                    
            }
        }

        private void HideAll()
        {
            friendListPanel.SetActive(false);
            chatPanel.SetActive(false);
        }
    }
}
