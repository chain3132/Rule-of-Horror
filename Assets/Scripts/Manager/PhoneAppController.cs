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
        /// <summary>
        /// ปุ่มตัวเลขอันนี้เปิดแอปอะไร — อ่านจากลิสต์ apps ใน Inspector ไม่ฮาร์ดโค้ดแล้ว
        ///
        /// เดิมเป็น switch ที่ผูกไว้ว่าปุ่ม 3 = นาฬิกา ซึ่งชนกับดีไซน์ใหม่ที่ปุ่ม 3 = แอปกฎ
        /// (เวลาย้ายไปอยู่บนแถบสถานะถาวรแล้ว ไม่ต้องมีแอปแยก)
        /// ย้ายมาอ่านจาก PhoneAppData.openState แทน จะสลับลำดับแอปทีหลังก็ไม่ต้องแก้โค้ด
        /// </summary>
        public PhoneState GetStateByIndex(int index)
        {
            if (apps != null && index >= 0 && index < apps.Count && apps[index] != null)
                return apps[index].openState;

            return PhoneState.AppSelection;
        }

        public void SetTimeApp()
        {
            if (phoneSystem.CurrentState != PhoneState.Clock){ return; }
            uiController.SetTimeText(_ruleTimeSetting.TryGetValue(0, out var timeText) ? timeText : "00:00");
        }
    }
}
