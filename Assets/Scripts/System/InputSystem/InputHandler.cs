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
        private InputAction _rulePrevAction, _ruleNextAction;   // เหมือนกัน ไม่มีก็อ่าน A/D ตรงๆ
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
            _rulePrevAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("RulePrev");
            _ruleNextAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("RuleNext");
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

        /// <summary>
        /// A / D ตอนเปิดแอปกฎ = พลิกไปกฎข้อก่อนหน้า / ข้อถัดไป
        ///
        /// ใช้ action ชื่อ "RulePrev"/"RuleNext" ถ้ามีใน asset ไม่มีก็อ่าน A/D ตรงๆ เหมือน Pray
        /// RuleBookController เป็นคนกันเองว่าจะรับเฉพาะตอนเปิดแอปกฎอยู่ ไม่งั้นมันจะไปกินปุ่มเดิน
        /// </summary>
        public bool WasRulePrevPressed()
        {
            if (_rulePrevAction != null) return _rulePrevAction.triggered;
            return Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame;
        }

        /// <inheritdoc cref="WasRulePrevPressed"/>
        public bool WasRuleNextPressed()
        {
            if (_ruleNextAction != null) return _ruleNextAction.triggered;
            return Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame;
        }

        // ── เลื่อนรายการในโทรศัพท์ ────────────────────────────────────────
        // จอโทรศัพท์ถ่ายผ่านกล้องลง RenderTexture ตัวชี้เมาส์บนจอจริงจึงไปโดนปุ่มในนั้นไม่ได้เลย
        // ทุกอย่างในโทรศัพท์ต้องกดด้วยคีย์บอร์ดล้วน — ชุดนี้คือปุ่มเลื่อนขึ้น/ลง/ตกลง

        /// <summary>W หรือลูกศรขึ้น — เลื่อนขึ้นในรายชื่อ</summary>
        public bool WasNavUpPressed()
        {
            if (Keyboard.current == null) return false;
            return Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame;
        }

        /// <summary>S หรือลูกศรลง — เลื่อนลงในรายชื่อ</summary>
        public bool WasNavDownPressed()
        {
            if (Keyboard.current == null) return false;
            return Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame;
        }

        /// <summary>
        /// Enter — เปิดสิ่งที่เลือกอยู่ในโทรศัพท์
        ///
        /// ตั้งใจไม่ใช้ E ถึงแม้จะเป็นปุ่มยืนยันที่คุ้นกว่า เพราะ E ยิง OnInteractPressed ทั่วเกม
        /// ซึ่งกฎต่างๆ ฟังอยู่ — เปิดโทรศัพท์ขณะจับสายสิญจน์แล้วกด E เลือกรายชื่อ มือจะหลุดจากสายไปด้วย
        /// ถ้าอยากได้ E จริงๆ ต้องกันที่ต้นทางก่อน (ให้ InputHandler ไม่ยิง OnInteractPressed
        /// ตอนโทรศัพท์เปิดอยู่) แล้วค่อยเพิ่ม _interactAction.triggered เข้ามาตรงนี้
        /// </summary>
        public bool WasConfirmPressed()
            => Keyboard.current != null &&
               (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);

        /// <summary>ล้อเมาส์ — ใช้เลื่อนแชท (ล้อยังใช้ได้ถึงจะคลิกไม่ได้)</summary>
        public float GetScrollDelta()
            => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        #endregion
        
        
    }
}
