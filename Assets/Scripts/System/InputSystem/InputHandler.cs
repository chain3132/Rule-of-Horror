using System;
using Manager;
using MoreMountains.Tools;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InputSystem
{
    public class InputHandler : MMSingleton<MonoBehaviour>
    {
        #region SerializeFields
        
        [SerializeField]
        private PlayerController playerController;
        
        #endregion
        
        #region Fields
        private InputAction _moveAction,_lookAction,_phoneAction,_chatAction,_flashLightAction,_clockAction,_interactAction,_rightClickAction;
        public event Action OnPhoneToggle;
        public event Action OnSetTime;
        public event Action<int> OnAppKeyPressed;
        public event Action OnBackPressed, OnInteractPressed, OnRightClickPressed;
        

        #endregion

        #region liefecycle

        protected override void Awake()
        {
            base.Awake();

            _moveAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Movement");
            _lookAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Look");
            _phoneAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Phone");
            _chatAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Chatting");
            _flashLightAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("FlashLight");
            _clockAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Clock");
            _interactAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Interact");
            _rightClickAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("RightClick");
        }
        private void OnEnable()
        {
            _phoneAction.performed += HandlePhonePerformed;
            _chatAction.performed += ChattingPerformed;
            _flashLightAction.performed += FlashLightPerformed;
            _clockAction.performed += ClockPerformed;
            _interactAction.performed += InteractPerformed;
            _rightClickAction.performed += RightClickPerformed;
            //_setTimeAction.performed += SetTime;
        }

        private void OnDisable()
        {
            _phoneAction.performed -= HandlePhonePerformed;
            _chatAction.performed -= ChattingPerformed;
            _flashLightAction.performed -= FlashLightPerformed;
            _clockAction.performed -= ClockPerformed;
            _interactAction.performed -= InteractPerformed;
            _rightClickAction.performed -= RightClickPerformed;
           // _setTimeAction.performed -= SetTime;
        }


        private void Update()
        {
            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            Vector2 lookInput = _lookAction.ReadValue<Vector2>();

            playerController.Move(moveInput);
            playerController.Look(lookInput);

            
        }

        #endregion

        #region Methods

        private void HandlePhonePerformed(InputAction.CallbackContext ctx)
        {
            OnPhoneToggle?.Invoke();
        }
        private void ChattingPerformed(InputAction.CallbackContext ctx)
        {
            OnAppKeyPressed?.Invoke(0);
        }
        private void FlashLightPerformed(InputAction.CallbackContext ctx)
        {
            OnAppKeyPressed?.Invoke(1);
        }
        private void ClockPerformed(InputAction.CallbackContext ctx)
        {
            OnAppKeyPressed?.Invoke(2);
        }
        private void SetTime(InputAction.CallbackContext ctx)
        {
            OnSetTime?.Invoke();
        }
        private void InteractPerformed(InputAction.CallbackContext ctx)
        {
            OnInteractPressed?.Invoke();
        }
        private void RightClickPerformed(InputAction.CallbackContext ctx)
        {
            OnRightClickPressed?.Invoke();
        }

        /// <summary>Returns the raw look (mouse delta) value from the New Input System.</summary>
        public Vector2 GetLookInput() => _lookAction.ReadValue<Vector2>();

        #endregion
        
        
    }
}
