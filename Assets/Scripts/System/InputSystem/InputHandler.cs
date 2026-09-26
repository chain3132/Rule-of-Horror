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
        private InputAction _moveAction,_lookAction,_phoneAction,_chatAction,_flashLightAction,_clockAction,_interactAction,_rightClickAction,_holdBreathAction;
        private InputAction _prayAction;   // ไม่บังคับต้องมีใน asset — ไม่มีก็อ่าน Space ตรงๆ
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
            _holdBreathAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("HoldBreath");
            _prayAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Pray");   // null ได้ ไม่ต้องมีก็ได้
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

        /// <summary>ค่าเดินดิบ (WASD) — Rule 5 อ่านเอาแค่แกน y (W) ตอนเดินตามสายสิญจน์</summary>
        public Vector2 GetMoveInput() => _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>true ตราบใดที่ยังกดคลิกขวาค้างอยู่ (Rule 4 — จ้องผีแบบกดค้าง)</summary>
        public bool IsRightClickHeld() => _rightClickAction != null && _rightClickAction.IsPressed();

        /// <summary>true ตราบใดที่ยังกด E ค้างอยู่ (Rule 5 — แก้สายสิญจน์ที่พันกัน)</summary>
        public bool IsInteractHeld() => _interactAction != null && _interactAction.IsPressed();

        /// <summary>
        /// เฟรมนี้เพิ่งกดปุ่ม "สวดมนต์" (Rule 5 — บอกว่าจะเลิกจับสายกลับไปนั่งแล้ว)
        /// ใช้ action ชื่อ "Pray" ถ้ามีใน asset — ยังไม่มีก็อ่าน Space จากคีย์บอร์ดตรงๆ ไปก่อน
        /// จะได้ไม่ต้องไปแก้ .inputactions ก่อนถึงจะเทสได้ (วันไหนเพิ่ม action ชื่อนี้ มันจะสลับไปใช้เอง)
        /// </summary>
        public bool WasPrayPressed()
        {
            if (_prayAction != null) return _prayAction.triggered;
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        /// <summary>true ตราบใดที่ยังกดปุ่มกลั้นหายใจค้างอยู่ (Left Shift — Rule 4)</summary>
        public bool IsHoldBreathHeld() => _holdBreathAction != null && _holdBreathAction.IsPressed();

        #endregion
        
        
    }
}
