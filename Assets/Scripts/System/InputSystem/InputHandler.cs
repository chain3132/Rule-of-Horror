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
        private InputAction _moveAction,_lookAction,_phoneAction;
        public event Action OnPhoneToggle;
        public event Action<int> OnAppKeyPressed;
        public event Action OnBackPressed;
        

        #endregion

        #region liefecycle

        protected override void Awake()
        {
            base.Awake();

            _moveAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Movement");
            _lookAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Look");
            _phoneAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Phone");
        }
        private void OnEnable()
        {
            _phoneAction.performed += HandlePhonePerformed;
        }

        private void OnDisable()
        {
            _phoneAction.performed -= HandlePhonePerformed;
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
            //GameManager._instance.ChangePlayerState(PlayerState.PlayerStates.OnTapInteraction);
        }

        #endregion
        
        
    }
}
