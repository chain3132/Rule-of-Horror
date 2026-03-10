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

        
        [Header("Gravity Settings")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float groundCheckDistance = 0.2f;
        
        [Header("Animation Settings")]
        [SerializeField] private Animator animator;
        private float _verticalVelocity;
        private bool _isGrounded;
        #endregion
        
        #region Fields
        
        private CharacterController _characterController;
        private float _xRotation;
        private bool _isSitting;
        private Vector3 _moveDirection;
        private float _sittingYaw;
        private bool _canMove = true;
        private bool _canLook = true;

        #endregion

        #region liefecycle

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            Cursor.visible = false;
        }
        
        private void Update()
        {
            ApplyGravity();
        }
        

        #endregion

        #region Methods

        public void Move(Vector2 moveInput)
        {
            if (!_canMove) return;

            Vector3 move = transform.forward * moveInput.y + transform.right * moveInput.x;
            move *= moveSpeed;
            
            _characterController.Move(move * Time.deltaTime);
            animator.SetBool("walk", moveInput.magnitude > 0.1f);
        }
        public void Look(Vector2 lookInput)
        {
            if (!_canLook) return;
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
        private void ApplyGravity()
        {
            _isGrounded = _characterController.isGrounded;

            if (_isGrounded && _verticalVelocity < 0)
            {
                _verticalVelocity = -2f; 
            }

            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 gravityMove = Vector3.up * _verticalVelocity * Time.deltaTime;
            _characterController.Move(gravityMove);
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
        public void SetMovement(bool value)
        {
            _canMove = value;
        }

        public void SetLook(bool value)
        {
            _canLook = value;
        }
        #endregion
        
        
    }
}
