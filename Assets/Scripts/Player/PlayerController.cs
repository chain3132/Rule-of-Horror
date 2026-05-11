using System;
using System.Collections;
using UnityEngine;

namespace Player
{
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }
        public bool isBlockStanding = false; 
        #region SerializeFields
        [Header("Camera Height Settings")]
        [SerializeField] private float standingCameraHeight = 2.55f; 
        [SerializeField] private float sittingCameraHeight = 1.3f;
        
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

        [SerializeField] private GameObject meshPlayer;
        private float _verticalVelocity;
        private bool _isGrounded;
        private bool _isTransitioning; 
        
        #endregion
        
        #region Fields
        
        private CharacterController _characterController;
        private float _xRotation;
        private bool _isSitting;
        private Vector3 _moveDirection;
        private float _sittingYaw;
        private bool _canMove = true;
        private bool _canLook = true;
        private Vector2 _externalLookForce;
        private Vector3 _beforeSitPosition;
        private Quaternion _beforeSitRotation;
        public bool IsSitting() => _isSitting;


        #endregion

        #region liefecycle

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
            _characterController = GetComponent<CharacterController>();
            Cursor.visible = false;
        }
        
        private void Update()
        {
            if (!_isSitting && !_isTransitioning)
            {
                ApplyGravity();
            }
        }
        

        #endregion

        #region Methods
        

        public void StartSittingSequence(Transform target)
        {
            if (_isSitting || _isTransitioning) return;
            _beforeSitPosition = transform.position;
            _beforeSitRotation = transform.rotation;
            StartCoroutine(SitRoutine(target));
        }

        private IEnumerator SitRoutine(Transform target)
        {
            _isTransitioning = true;
            SetMovement(false);
            
            float duration = 0.7f; // ระยะเวลาการเปลี่ยนท่า
            float elapsed = 0f;
            
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            
            // ปิด CharacterController ชั่วคราวเพื่อให้ย้ายตำแหน่งได้แม่นยำ
            _characterController.enabled = false;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float curve = Mathf.SmoothStep(0, 1, t); 

                // ย้ายตำแหน่งไปยังจุดนั่ง
                transform.position = Vector3.Lerp(startPos, target.position, curve);
                // หมุนตัวละครให้ตรงกับ Target 
                transform.rotation = Quaternion.Slerp(startRot, target.rotation, curve);
                
                if (cameraPivot != null) 
                {
                    Vector3 camPos = cameraPivot.localPosition;
                    camPos.y = Mathf.Lerp(standingCameraHeight, sittingCameraHeight, curve);
                    cameraPivot.localPosition = camPos;
                }
                yield return null;
            }

            transform.position = target.position;
            transform.rotation = target.rotation;
            _characterController.enabled = true;

            // 2. เล่น Animation (ส่ง Parameter ไปที่ Animator)
            if (animator != null)
            {
                animator.SetTrigger("sit"); 
            }

            SetSitting(true);
            SetSittingPhysics(true);
            _isTransitioning = false;
        }

        public void StartStandingSequence()
        {
            if (!_isSitting || _isTransitioning) return;
            if (isBlockStanding) return;
            StartCoroutine(StandUpRoutine());
        }

        private IEnumerator StandUpRoutine()
        {
            _isTransitioning = true;
            SetSittingPhysics(false);
            _verticalVelocity = 0f;
            SetLook(false); // ล็อกหน้าจอชั่วคราวตอนกำลังยืดตัวขึ้น

            // 1. เล่น Animation ลุกขึ้น
            if (animator != null)
            {
                animator.SetTrigger("standUp"); // อย่าลืมสร้าง Trigger ชื่อ standUp ใน Animator
            }

            float duration = 0.6f; // ระยะเวลาการลุก
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float curve = Mathf.SmoothStep(0, 1, t);

                // 2. ค่อยๆ ยืดความสูงกล้อง (CameraRoot) กลับไปที่ 2.55
                if (cameraPivot != null)
                {
                    Vector3 camPos = cameraPivot.localPosition;
                    camPos.y = Mathf.Lerp(sittingCameraHeight, standingCameraHeight, curve);
                    cameraPivot.localPosition = camPos;
                }

                yield return null;
            }

            // Lock ค่าสุดท้ายให้เป๊ะ
            if (cameraPivot != null)
            {
                Vector3 finalPos = cameraPivot.localPosition;
                finalPos.y = standingCameraHeight; // กลับไปที่ 2.55
                cameraPivot.localPosition = finalPos;
            }

            _isSitting = false;
            _sittingYaw = 0f;
            SetSittingPhysics(false); // เปลี่ยน layer หลัง transition จบ
            SnapToGround();           // snap ไปยืนบนพื้นจริงก่อน gravity เริ่ม
            _verticalVelocity = 0f;   // reset velocity หลัง snap
            SetMovement(true); // ปลดล็อกให้เดินได้
            SetLook(true);     // ปลดล็อกให้หันหน้าได้ปกติ
            _isTransitioning = false;
            
            _characterController.enabled = false;

            transform.position = _beforeSitPosition;
            _characterController.enabled = true;
            _verticalVelocity = -2f;
        }
        public void ForceGroundSnap()
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.down, out hit, 5f))
            {
                float skin = _characterController.skinWidth;
                float bottomOffset = _characterController.height / 2f;

                Vector3 pos = transform.position;
                pos.y = hit.point.y + bottomOffset + skin;

                transform.position = pos;
            }
        }

        /// <summary>
        /// ยิง Raycast จากด้านบนลงมาหาพื้น แล้ว snap ตัวละครขึ้นไปยืนบนพื้นนั้น
        /// ป้องกันตัวอยู่ใต้ MeshCollider ตอนลุกจากการนั่ง
        /// </summary>
        private void SnapToGround()
        {
            // ยิงจาก 2m เหนือตำแหน่งปัจจุบัน ลงมา 5m เพื่อหาพื้นจริง
            Vector3 rayStart = transform.position + Vector3.up * 2f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 5f))
            {
                _characterController.enabled = false;
                transform.position = hit.point;   // วางเท้าบนพื้น
                _characterController.enabled = true;
            }
        }

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
            
            Vector2 finalInput = lookInput + _externalLookForce;
            float mouseX = finalInput.x * mouseSensitivity;
            float mouseY = finalInput.y * mouseSensitivity;

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
        public void SetExternalLookForce(Vector2 force)
        {
            _externalLookForce = force;
        }
        private void ApplyGravity()
        {
            if (!_characterController.enabled) return; // CC ปิดอยู่ (เช่น ตอน cutscene เดิน)

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
        private void SetSittingPhysics(bool isSitting)
        {
            // เปลี่ยน Layer ของตัวละคร (สมมติว่า Layer ปกติคือ 0: Default หรือ 3: Player)
            // และเปลี่ยนเป็น Layer ที่เราสร้างใหม่ (เช่น "PlayerSitting")
            if (isSitting)
            {
                gameObject.layer = LayerMask.NameToLayer("PlayerSitting");
                meshPlayer.layer = LayerMask.NameToLayer("PlayerSitting");
            }
            else
            {
                gameObject.layer = LayerMask.NameToLayer("Default"); // 
                meshPlayer.layer = LayerMask.NameToLayer("Default"); // 
            }
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

        // ─────────────── Rule 3 Death ───────────────

        /// <summary>
        /// Rule 3 death animation: camera tilts sideways as if the player is falling over.
        /// Disables look + movement for the duration. Call with StartCoroutine / yield return.
        /// </summary>
        public IEnumerator PlayDeathFallRoutine()
        {
            SetLook(false);
            SetMovement(false);

            float duration   = 1.2f;
            float t          = 0f;
            float targetRoll = -85f; // negative = fall to the left; flip sign for right

            while (t < duration)
            {
                t += Time.deltaTime;
                float normalized = t / duration;
                float curve      = Mathf.Pow(normalized, 1.5f); // starts slow, ends fast

                // Keep pitch/yaw from current sit position; only add death roll
                cameraPivot.localRotation = Quaternion.Euler(
                    _xRotation,
                    _sittingYaw,
                    Mathf.Lerp(0f, targetRoll, curve)
                );
                yield return null;
            }
        }

        /// <summary>
        /// Snap camera back to neutral sitting orientation after the death-blink sequence.
        /// Re-enables look so the player can interact with papers again.
        /// </summary>
        public void ResetCameraAfterDeath()
        {
            _xRotation  = 0f;
            _sittingYaw = 0f;
            cameraPivot.localRotation = Quaternion.identity;
            SetLook(true);
            // Movement stays disabled while sitting — StandUpRoutine will re-enable it
        }

        #endregion


    }
}
