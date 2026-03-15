using System.Collections.Generic;
using Enum;
using ScriptableObject;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Manager
{
    public class PhoneAppController : MonoBehaviour
    {
        [SerializeField] private List<PhoneAppData> apps;
        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;
        
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private PhoneUIController uiController;

        [SerializeField] private SerializedDictionary<int, string> _ruleTimeSetting;

        public void OpenAppByIndex(int index)
        {
            if (index < 0 || index >= apps.Count) return;

            var app = apps[index];
            phoneSystem.ChangeState(app.openState);
        }
        public PhoneState GetStateByIndex(int index)
        {
            switch (index)
            {
                case 0: return PhoneState.FriendList;
                case 1: return PhoneState.FlashLight;
                case 2: return PhoneState.Clock;
            }

            return PhoneState.AppSelection;
        }

        public void SetTimeApp()
        {
            if (phoneSystem.CurrentState != PhoneState.Clock){ return; }
            uiController.SetTimeText(_ruleTimeSetting.TryGetValue(0, out var timeText) ? timeText : "00:00");
        }
    }
}
