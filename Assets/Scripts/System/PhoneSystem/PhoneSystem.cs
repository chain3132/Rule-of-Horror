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

        [SerializeField] private PlayerController playerController;
        [SerializeField] private GameObject phoneObject;
        [SerializeField] private PhoneAppController appController;
        [SerializeField] private PhoneUIController uiController;
        [SerializeField] private InputHandler input;
        [SerializeField] private ConversationRunner runner;

        
        private void OnEnable()
        {
            input.OnPhoneToggle += TogglePhone;
            input.OnAppKeyPressed += OpenAppByIndex;
            input.OnBackPressed += Back;
        }

        private void OnDisable()
        {
            input.OnPhoneToggle -= TogglePhone;
            input.OnAppKeyPressed -= OpenAppByIndex;
            input.OnBackPressed -= Back;
        }
        public void ChangeState(PhoneState newState)
        {
            CurrentState = newState;
            uiController.UpdateState(newState);
        }
        private void TogglePhone()
        {
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

        public void OpenAppByIndex(int index)
        {
            if (CurrentState != PhoneState.AppSelection) return;
            LockPlayer();
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
            playerController.SetMovement(true);
            playerController.SetLook(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
