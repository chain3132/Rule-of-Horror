using System.Collections.Generic;
using ScriptableObject;
using UnityEngine;

namespace Manager
{
    public class PhoneAppController : MonoBehaviour
    {
        [SerializeField] private List<PhoneAppData> apps;
        [SerializeField] private PhoneSystem.PhoneSystem phoneSystem;

        public void OpenAppByIndex(int index)
        {
            if (index < 0 || index >= apps.Count) return;

            var app = apps[index];
            phoneSystem.ChangeState(app.openState);
        }
    }
}
