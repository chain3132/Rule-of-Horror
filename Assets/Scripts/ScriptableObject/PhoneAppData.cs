using Enum;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace ScriptableObject
{
    [CreateAssetMenu(menuName = "Phone/AppData")]

    public class PhoneAppData : UnityEngine.ScriptableObject
    {
        public PhoneState openState;
    }
}
