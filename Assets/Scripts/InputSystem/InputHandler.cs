using System;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InputSystem
{
    public class InputHandler : MonoBehaviour
    {
        #region SerializeFields
        
        [SerializeField]
        private PlayerController playerController;
        
        #endregion
        
        #region Fields

        private InputAction _moveAction,_lookAction;

        #endregion

        #region liefecycle

        private void Awake()
        {
            _moveAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Movement");
            _lookAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Look");
        }

        private void Update()
        {
            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            Vector2 lookInput = _lookAction.ReadValue<Vector2>();

            playerController.Move(moveInput);
            playerController.Look(lookInput);

            
        }

        #endregion
    }
}
