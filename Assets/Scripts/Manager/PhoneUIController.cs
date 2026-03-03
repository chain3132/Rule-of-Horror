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
        }

        private void HideAll()
        {
            homePanel.SetActive(false);
            //friendListPanel.SetActive(false);
            //chatPanel.SetActive(false);
        }
    }
}
