using Enum;
using InputSystem;
using Manager;
using Player;
using ScriptableObject;
using UnityEngine;

namespace PhoneSystem
{
    public class PhoneSystem : MonoBehaviour
    {
        public PhoneState CurrentState { get; private set; }

        private bool _phoneLocked;

        [SerializeField] private PlayerController playerController;
        [SerializeField] private GameObject phoneObject;
        [SerializeField] private PhoneAppController appController;
        [SerializeField] private PhoneUIController uiController;
        [SerializeField] private InputHandler input;
        [SerializeField] private ConversationRunner runner;

        [Tooltip("ใส่ ChatUIController — ตอนมีตัวเลือกคำตอบค้างอยู่ ปุ่มตัวเลขจะกลายเป็นปุ่มเลือกคำตอบ")]
        [SerializeField] private ChatUIController chatUI;

        
        private void OnEnable()
        {
            input.OnPhoneToggle += TogglePhone;
            input.OnAppKeyPressed += OpenAppByIndex;
            input.OnBackPressed += Back;
            input.OnSetTime += appController.SetTimeApp;
        }

        private void OnDisable()
        {
            input.OnPhoneToggle -= TogglePhone;
            input.OnAppKeyPressed -= OpenAppByIndex;
            input.OnBackPressed -= Back;
            input.OnSetTime -= appController.SetTimeApp;
        }
        public void ChangeState(PhoneState newState)
        {
            CurrentState = newState;
            uiController.UpdateState(newState);
        }
        /// <summary>ล็อกไม่ให้ผู้เล่นเปิดโทรศัพท์ได้ (ใช้ตอนโทรศัพท์หาย เช่น Rule 1)</summary>
        public void LockPhone()
        {
            _phoneLocked = true;
            LowerPhone(); // บังคับซ่อน + set state = Hidden
        }

        /// <summary>ปลดล็อกให้ผู้เล่นเปิดโทรศัพท์ได้ตามปกติ</summary>
        public void UnlockPhone()
        {
            _phoneLocked = false;
        }

        private void TogglePhone()
        {
            if (_phoneLocked) return; // โทรศัพท์หายอยู่ → ห้ามเปิด

            if (CurrentState == PhoneState.Hidden)
                RaisePhone();
            else
                LowerPhone();
        }
        public void OpenChat()
        {
            ChangeState(PhoneState.ChatView);
        }
        public void Back()
        {
            switch (CurrentState)
            {
                case PhoneState.ChatView:
                    ChangeState(PhoneState.FriendList);
                    break;

                case PhoneState.FriendList:
                    ChangeState(PhoneState.AppSelection);
                    break;
                case PhoneState.FlashLight:
                    ChangeState(PhoneState.AppSelection);
                    break;
                case PhoneState.Clock:
                    ChangeState(PhoneState.AppSelection);
                    break;
                case PhoneState.RuleBook:
                    ChangeState(PhoneState.AppSelection);
                    break;

                case PhoneState.AppSelection:
                    LowerPhone();
                    break;
            }
        }
        public void OpenMessageApp()
        {
            ChangeState(PhoneState.FriendList);
        }
        public void RaisePhone()
        {
            phoneObject.SetActive(true);
            ChangeState(PhoneState.AppSelection);
        }

        /// <summary>
        /// กดปุ่มตัวเลขเพื่อเปิดแอป — ใช้ได้ตลอดตราบใดที่โทรศัพท์ยกอยู่ ไม่ต้องถอยกลับหน้าโฮมก่อน
        ///
        /// เดิมรับเฉพาะตอนอยู่หน้า AppSelection แต่ดีไซน์ใหม่ให้สลับแอปตรงๆ ได้
        /// (อยู่ในแอปกฎแล้วกด 1 = กลับไปแชท / อยู่ในแชทแล้วกด 3 = เปิดกฎ)
        /// ปุ่มตัวเลขจึงกลายเป็นปุ่มสลับแอป ไม่ใช่ปุ่มกดจากหน้าโฮมอย่างเดียว
        /// </summary>
        public void OpenAppByIndex(int index)
        {
            if (CurrentState == PhoneState.Hidden) return;

            // มีตัวเลือกคำตอบค้างอยู่ → เลข 1/2 คือการเลือกคำตอบ ไม่ใช่การสลับแอป
            // (ตามดีไซน์: ในหน้าแชท "1 2 เลือกคำตอบ" แต่ในหน้าอื่น "1 กลับไปแชท")
            // จอโทรศัพท์ถ่ายลง RenderTexture เมาส์คลิกปุ่มในนั้นไม่ได้ ปุ่มตัวเลขจึงเป็นทางเดียว
            if (chatUI != null && chatUI.AwaitingReply && chatUI.TrySelectReply(index)) return;

            PhoneState state = appController.GetStateByIndex(index);

            if (state != PhoneState.FlashLight)
            {
                LockPlayer();
            }
            else
            {
                UnlockPlayer();
            }
            appController.OpenAppByIndex(index);
        }

        private void LockPlayer()
        {
            playerController.SetMovement(false);
            playerController.SetLook(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void LowerPhone()
        {
            phoneObject.SetActive(false);
            UnlockPlayer();
            ChangeState(PhoneState.Hidden);
        }

        private void UnlockPlayer()
        {
            if (!playerController.IsSitting())
                playerController.SetMovement(true); 
            playerController.SetLook(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
