using System;
using UnityEngine;

namespace Player
{
    public class PlayerController : MonoBehaviour
    {
        #region SerializeFields

        [Header("Movement Settings")]
        [SerializeField]
        private float moveSpeed = 5f;
        
        [Header("Look Settings")]
        [SerializeField] 
        private Transform cameraPivot;
        [SerializeField] 
        private float mouseSensitivity = 2f;
        [SerializeField] 
        private float lookClamp = 80f;
        [SerializeField] 
        private float sittingLookLimit = 60f;

        
        #endregion
        
        #region Fields
        
        private CharacterController _characterController;
        private float _xRotation;
        private bool _isSitting;
        private Vector3 _moveDirection;
        private float _sittingYaw;

        #endregion

        #region liefecycle

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            Cursor.visible = false;
        }

        #endregion

        #region Methods

        public void Move(Vector2 moveInput)
        {
            Vector3 move = transform.forward * moveInput.y + transform.right * moveInput.x;
            move = move * moveSpeed * Time.deltaTime;
            _characterController.Move(move);
        }
        public void Look(Vector2 lookInput)
        {
            float mouseX = lookInput.x * mouseSensitivity;
            float mouseY = lookInput.y * mouseSensitivity;

            _xRotation -= mouseY;
            _xRotation = Mathf.Clamp(_xRotation, -lookClamp, lookClamp);

            cameraPivot.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);

            if (_isSitting)
            {
                RotateWhileSitting(mouseX);
            }
            else
            {
                transform.Rotate(Vector3.up * mouseX);
            }
        }
        
        private void RotateWhileSitting(float mouseX)
        {
            _sittingYaw += mouseX;
            _sittingYaw = Mathf.Clamp(_sittingYaw, -sittingLookLimit, sittingLookLimit);

            cameraPivot.localRotation = Quaternion.Euler(_xRotation, _sittingYaw, 0f);
        }
        
        public void SetSitting(bool value)
        {
            _isSitting = value;
            _sittingYaw = 0f;
        }

        #endregion
        
        
    }
}
