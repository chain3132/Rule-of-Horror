using System;
using System.Collections;
using UnityEngine;

namespace Player
{
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }
        // เริ่มเกมล็อกการลุกไว้ก่อน — ปลดล็อกเมื่อ Rule 1 เริ่ม (Rule1.StartPhoneDropped)
        public bool isBlockStanding = true;
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
        private float _moveSpeedMultiplier = 1f;
        private Vector2 _externalLookForce;
        private Vector3 _beforeSitPosition;
        private Quaternion _beforeSitRotation;
        private bool _hasSatOnce;

        // จำกัดมุมหัน (yaw) รอบทิศที่กำหนด — Rule 5 ใช้ล็อกให้มองได้แค่ 180° ตอนจับสายสิญจน์
        private bool  _yawLimitActive;
        private float _yawLimitCenter;
        private float _yawLimitHalf;

        public bool IsSitting() => _isSitting;

        /// <summary>
        /// ผู้เล่นคุมตัวเองได้อยู่ไหม — ระบบที่พาตัวผู้เล่นต้องเช็คก่อนแตะ SetLook/SetMovement
        ///
        /// ถ้ามีคนอื่นยึดการควบคุมไว้แล้ว (โทรศัพท์ / puzzle ตู้ไฟ / กะพริบตา / ฉากตาย)
        /// แล้วเราไปสั่ง SetLook(true) ตอนจบงานของเรา จะเป็นการปลดล็อกแทนเจ้าของจริง
        /// </summary>
        public bool CanMove => _canMove;
        public bool CanLook => _canLook;

        /// <summary>
        /// true เมื่อผู้เล่นเคยนั่งลงอย่างน้อยหนึ่งครั้งในรอบนี้ — BeforeSitPosition จึงเชื่อถือได้
        /// ถ้ายังไม่เคยนั่ง (เช่นข้าม intro มาด้วย dev skip) ค่ายังเป็น Vector3.zero
        /// </summary>
        public bool       HasSatOnce        => _hasSatOnce;

        /// <summary>
        /// จุดที่ผู้เล่นยืนอยู่ก่อนนั่งลงครั้งล่าสุด — หน้าม้านั่งในศาลา
        /// StandUpRoutine ส่งผู้เล่นกลับที่นี่อยู่แล้วตอนลุกปกติ จึงการันตีว่ายืนได้จริง
        /// </summary>
        public Vector3    BeforeSitPosition => _beforeSitPosition;
        public Quaternion BeforeSitRotation => _beforeSitRotation;


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
        

        /// <summary>
        /// ย้ายจุดที่จะกลับไปยืนตอนลุก มาเป็นจุดที่นั่งอยู่ตอนนี้
        ///
        /// StandUpRoutine จบด้วย transform.position = _beforeSitPosition เสมอ คือ "ลุกแล้ว
        /// กลับไปยืนที่เดิมก่อนนั่ง" ซึ่งถูกสำหรับม้านั่งที่อยู่กับที่ แต่พังทันทีถ้าที่นั่งนั้น
        /// เคลื่อนที่: ผู้เล่นนั่งในรถที่ปลายถนน รถพามาจอดหน้าศาลา พอลุกจะถูกดีดกลับไปยืน
        /// อยู่ปลายถนนที่เดิม ห่างจากรถเป็นสิบเมตร
        ///
        /// ใครที่พาผู้เล่นนั่งไปกับของที่เคลื่อนที่ต้องเรียกตัวนี้ก่อนปล่อยให้ลุก (BusRide ทำให้แล้ว)
        /// </summary>
        public void RebaseStandUpPosition() => RebaseStandUpPosition(transform.position);

        /// <summary>
        /// เหมือนตัวบน แต่กำหนดจุดที่จะไปยืนเองได้
        ///
        /// ที่นั่งในรถมีเบาะและพนักล้อมอยู่ ถ้าลุกตรงจุดที่นั่งพอดี แคปซูลของ CharacterController
        /// (สูง 2.5) จะไปโผล่ซ้อนกับเบาะแล้วผู้เล่นจะขยับไม่ได้เลย — ต้องให้ลุกออกไปที่ทางเดิน
        ///
        /// ใส่แค่ตำแหน่ง ไม่มีการหมุน เพราะ StandUpRoutine ไม่ได้เขียน rotation ตอนจบ
        /// (ผู้เล่นหันหน้าไว้ทางไหนตอนนั่ง ก็ยืนหันทางนั้น ซึ่งถูกแล้ว)
        /// </summary>
        public void RebaseStandUpPosition(Vector3 worldPosition)
        {
            _beforeSitPosition = worldPosition;
            _beforeSitRotation = transform.rotation;
        }

        public void StartSittingSequence(Transform target)
        {
            if (_isSitting || _isTransitioning) return;
            _beforeSitPosition = transform.position;
            _beforeSitRotation = transform.rotation;
            _hasSatOnce        = true;
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

            // ย้ายตัวไปจุดยืนพร้อมกับที่กล้องยืดขึ้น ไม่ใช่กระโดดไปทีเดียวตอนจบ
            //
            // SitRoutine เลื่อนเข้าเบาะใน 0.7 วิ แต่ตอนลุกของเดิมเขียนตำแหน่งทีเดียวในเฟรมสุดท้าย
            // ที่ม้านั่งศาลามองไม่ออกเพราะสองจุดห่างกันไม่ถึงเมตร แต่ที่นั่งในรถ จุดยืนอยู่ทางเดินข้างเบาะ (Stand Pose)
            // กล้องจะยืดขึ้นตรงเบาะแล้วกระตุกไปข้างๆ หนึ่งเฟรม
            //
            // ปิด CharacterController ตลอดช่วงนี้เหมือน SitRoutine — ต้องให้ตัวผ่านพนักเบาะออกมาได้
            // ถ้าชนมันจะค้างอยู่ในเบาะ ซึ่งเป็นอาการเดิมที่กำลังแก้
            Vector3 standTarget = _beforeSitPosition;
            Vector3 startPos    = transform.position;
            _characterController.enabled = false;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float curve = Mathf.SmoothStep(0, 1, t);

                transform.position = Vector3.Lerp(startPos, standTarget, curve);

                // 2. ค่อยๆ ยืดความสูงกล้อง (CameraRoot) กลับไปที่ 2.55
                if (cameraPivot != null)
                {
                    Vector3 camPos = cameraPivot.localPosition;
                    camPos.y = Mathf.Lerp(sittingCameraHeight, standingCameraHeight, curve);
                    cameraPivot.localPosition = camPos;
                }

                yield return null;
            }

            transform.position = standTarget;
            _characterController.enabled = true;

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

            // snap หลังย้ายตัวเสร็จแล้ว ไม่ใช่ก่อน — ของเดิม snap ตอนยังนั่งอยู่ที่เบาะ
            // แล้วค่อยย้าย พื้นที่ไปเกาะจึงเป็นพื้นใต้เบาะ ไม่ใช่พื้นตรงที่ยืนจริง
            SnapToGround();
            _verticalVelocity = -2f;   // ให้ gravity เกาะพื้นทันที ไม่ลอยค้างหนึ่งเฟรม
            SetMovement(true); // ปลดล็อกให้เดินได้
            SetLook(true);     // ปลดล็อกให้หันหน้าได้ปกติ
            _isTransitioning = false;
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

        /// <summary>
        /// ย้ายผู้เล่นไปตำแหน่งใหม่ทันที — ใช้ตอนจอดำ (กฎจบ / ตาย) ไม่ใช่ขณะเล่น
        ///
        /// ต้องปิด CharacterController ก่อนเขียน transform.position ไม่งั้น CC จะถือว่าเป็นการ
        /// เดินทะลุ collider แล้วดึงกลับที่เดิมในเฟรมถัดไป — เขียนตำแหน่งตรงๆ จึงไม่พอ
        ///
        /// snap ลงพื้นหลังย้ายด้วย เพราะจุดเป้าหมายเป็นค่าที่เก็บไว้ตอนไหนก็ไม่รู้
        /// ถ้าพื้นตรงนั้นสูงกว่าเดิมจะจมอยู่ใต้ mesh, ถ้าต่ำกว่าจะลอยแล้วร่วงลงมา
        /// </summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            _characterController.enabled = false;
            transform.position = position;
            transform.rotation = rotation;
            _characterController.enabled = true;

            SnapToGround();
            _verticalVelocity = -2f;   // ให้ gravity เกาะพื้นทันที ไม่ลอยค้างหนึ่งเฟรม
        }

        /// <summary>
        /// ดันตัวผู้เล่นไปตาม delta ที่ให้มา (world space) โดยไม่สนว่า input สั่งอะไร
        ///
        /// ผ่าน CharacterController.Move ไม่ใช่เขียน transform.position ตรงๆ เพราะต้องให้มันชน
        /// กำแพง/ชนพื้นตามปกติ — ขอบเขตแผนที่ดันกลับเข้ามาแล้วทะลุเข้าไปในก้อนหินไม่ได้
        ///
        /// ไม่เช็ค _canMove ด้วย: ตอนที่ต้องดันผู้เล่นกลับที่มักเป็นตอนที่เพิ่งล็อกการเดินไว้
        /// </summary>
        public void NudgeBy(Vector3 worldDelta)
        {
            if (!_characterController.enabled) return;
            _characterController.Move(worldDelta);
        }

        public void Move(Vector2 moveInput)
        {
            if (!_canMove) return;

            Vector3 move = transform.forward * moveInput.y + transform.right * moveInput.x;
            move *= moveSpeed * _moveSpeedMultiplier;
            
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
                if (_yawLimitActive) ClampYawToLimit();
            }
        }

        /// <summary>
        /// จำกัดให้หันซ้าย/ขวาได้ไม่เกิน halfAngle องศาจาก centerYaw (world, องศา)
        /// Rule 5 เรียกทุกเฟรมด้วยทิศของสายสิญจน์ตรงที่ผู้เล่นยืนอยู่ → มองไปข้างหลังไม่ได้
        /// ไม่มีผลตอนนั่ง (ตอนนั่งมี sittingLookLimit อยู่แล้ว) — ต้องเรียก ClearYawLimit() เองเมื่อเลิกใช้
        /// </summary>
        public void SetYawLimit(float centerYaw, float halfAngle)
        {
            _yawLimitActive = true;
            _yawLimitCenter = centerYaw;
            _yawLimitHalf   = Mathf.Max(0f, halfAngle);
            if (!_isSitting) ClampYawToLimit();
        }

        public void ClearYawLimit() => _yawLimitActive = false;

        private void ClampYawToLimit()
        {
            float delta = Mathf.DeltaAngle(_yawLimitCenter, transform.eulerAngles.y);
            if (Mathf.Abs(delta) <= _yawLimitHalf) return;

            delta = Mathf.Clamp(delta, -_yawLimitHalf, _yawLimitHalf);
            transform.rotation = Quaternion.Euler(0f, _yawLimitCenter + delta, 0f);
        }

        /// <summary>
        /// ขยับตัวละครด้วย delta โลกตรงๆ (แนวราบ) — ใช้โดยระบบที่คุมตำแหน่งเอง เช่น เดินตามสายสิญจน์ใน Rule 5
        /// ทำงานได้แม้ SetMovement(false) อยู่ (นั่นคือจุดประสงค์: ปิดเดินอิสระ แล้วให้ระบบภายนอกพาเดินแทน)
        /// แรงโน้มถ่วงยังทำงานตามปกติจาก ApplyGravity()
        /// </summary>
        public void MoveExternal(Vector3 worldDelta, bool walking)
        {
            if (_characterController == null || !_characterController.enabled) return;

            worldDelta.y = 0f;
            _characterController.Move(worldDelta);
            if (animator != null) animator.SetBool("walk", walking);
        }

        /// <summary>
        /// หันตัว + กล้องไปมองจุดในโลกทันที (ทั้ง yaw และ pitch) — ใช้บังคับให้ผู้เล่นมองผีตอน jumpscare
        /// ควรเรียกหลัง SetLook(false) ไม่งั้น Look() จะดึงกลับตามเมาส์ในเฟรมถัดไป
        /// </summary>
        public void LookAtWorldPoint(Vector3 point)
        {
            if (cameraPivot == null) return;

            Vector3 dir = point - cameraPivot.position;
            if (dir.sqrMagnitude < 0.0001f) return;

            float yaw   = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            _xRotation  = Mathf.Clamp(pitch, -lookClamp, lookClamp);

            if (_isSitting)
            {
                _sittingYaw = Mathf.Clamp(Mathf.DeltaAngle(transform.eulerAngles.y, yaw),
                                          -sittingLookLimit, sittingLookLimit);
                cameraPivot.localRotation = Quaternion.Euler(_xRotation, _sittingYaw, 0f);
            }
            else
            {
                transform.rotation        = Quaternion.Euler(0f, yaw, 0f);
                cameraPivot.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
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

        /// <summary>
        /// ซิงก์สถานะการหันของ PlayerController ให้ตรงกับทิศที่ cameraPivot ชี้อยู่จริงตอนนี้
        ///
        /// ระบบภายนอก (เช่น LookDistortionSystem.StartFocus) เขียน cameraPivot.rotation ตรงๆ
        /// โดยที่ _xRotation / yaw ของตัวละครไม่เปลี่ยน พอปลดล็อก Look() จะดีดกลับมุมเดิมทันที
        /// เรียกเมธอดนี้ก่อนปลดล็อก เพื่อให้กล้องอยู่ที่เดิมแบบไม่มีอาการเด้ง
        /// </summary>
        public void SyncLookToCameraDirection()
        {
            if (cameraPivot == null) return;

            Vector3 forward = cameraPivot.forward;
            float yaw   = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg,
                                      -lookClamp, lookClamp);

            _xRotation = pitch;

            if (_isSitting)
            {
                // ตอนนั่ง yaw อยู่บน cameraPivot (จำกัดช่วง) ไม่ใช่บนตัวละคร
                _sittingYaw = Mathf.Clamp(Mathf.DeltaAngle(transform.eulerAngles.y, yaw),
                                          -sittingLookLimit, sittingLookLimit);
                cameraPivot.localRotation = Quaternion.Euler(_xRotation, _sittingYaw, 0f);
            }
            else
            {
                transform.rotation        = Quaternion.Euler(0f, yaw, 0f);
                cameraPivot.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
            }
        }

        /// <summary>
        /// ตัวคูณความเร็วเดิน (1 = ปกติ) — Rule 4 ใช้หน่วงให้เดินช้ามากตอนกลั้นหายใจ
        /// ระบบที่เซ็ตค่านี้ต้องคืนเป็น 1 เองเสมอ
        /// </summary>
        public void SetMoveSpeedMultiplier(float value)
        {
            _moveSpeedMultiplier = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Pitch pivot (child of the yaw-bearing player transform). External systems may drive
        /// its world rotation ONLY while look is disabled via SetLook(false) — the puzzle never
        /// writes _xRotation / body yaw, so restoring the pivot's saved rotation stays consistent.
        /// </summary>
        public Transform CameraPivot => cameraPivot;

        /// <summary>
        /// ความสูงกล้องตอนนั่ง (local Y ของ cameraPivot) — ระดับตาของคนนั่ง
        ///
        /// เปิดออกมาให้ gizmo ของจุดนั่งวาดระดับตาได้ตรงกับของจริง จุดนั่งทุกที่ในเกมเป็นการ
        /// วาง "ราก" ของตัวละคร ไม่ใช่วางสะโพก (SitRoutine เขียน transform.position = target
        /// .position เป๊ะๆ) ซึ่งมองไม่ออกเลยใน Scene view ว่าสูงเท่าไรจึงจะไม่ลอย
        /// </summary>
        public float SittingCameraHeight => sittingCameraHeight;

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
