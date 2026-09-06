using System;
using InputSystem;
using Manager;
using Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rule4
{
    /// <summary>
    /// ระบบกลั้นหายใจของ Rule 4 (กด Left Shift ค้าง)
    ///
    /// ระหว่างกลั้น: ผีมองไม่เห็นผู้เล่น แต่เดินได้ช้ามาก และหน้าจอค่อยๆ แย่ลงตามเวลา
    ///   5 วิ  → เริ่มซีดเป็นสีเทา
    ///   9 วิ  → เริ่มเบลอ
    ///   15 วิ → เริ่มโยกไปมา
    ///   ตลอดช่วง → หน้ามืดขึ้นเรื่อยๆ ตั้งแต่ darkenStart (vignette หุบ + ภาพหรี่)
    ///   ครบ maxHoldDuration → บังคับหายใจออกแรง
    ///
    /// พอปล่อย (ปล่อยเองหรือถูกบังคับก็ตาม): เอฟเฟกต์ค่อยๆ จางหายภายใน cooldownDuration
    /// พร้อมเสียงหายใจหอบ และกลั้นใหม่ไม่ได้จนกว่าจะครบ — cooldown เท่ากันทุกกรณี
    ///
    /// การแก้ค่าใน VolumeProfile เป็นการแก้ asset ที่แชร์กัน — cache ค่าเดิมไว้ตอนใช้ครั้งแรก
    /// แล้วคืนค่าทุกทางออก (หยุดกลั้น / จบกฎ / OnDisable) เหมือนที่ Rule3 ทำกับ vignette
    /// </summary>
    public class BreathSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputHandler inputHandler;
        [Tooltip("Transform ของ Camera (ลูกของ CameraPivot) — ใช้ใส่ค่าโยกโดยไม่ชนกับ PlayerController.Look()")]
        [SerializeField] private Transform cameraTransform;

        [Tooltip("ภาพ/โมเดลมือปิดจมูกที่โผล่มาบังด้านล่างจอระหว่างกลั้นหายใจ" +"ใส่ GameObject ที่ปิดไว้อยู่แล้ว — ระบบจะเปิด/ปิดให้เอง")]
        [SerializeField] private GameObject holdBreathHandVisual;

        [Tooltip("(optional) Animator ของมือ — จะ SetBool ตามชื่อด้านล่างตอนกลั้น/ปล่อย")]
        [SerializeField] private Animator handAnimator;
        [SerializeField] private string   handHoldingBoolName = "isHolding";

        [Header("Timing (วินาที)")]
        [Tooltip("เริ่มซีดเป็นสีเทา")]
        [SerializeField] private float grayStart = 5f;
        [Tooltip("เริ่มเบลอ")]
        [SerializeField] private float blurStart = 9f;
        [Tooltip("เริ่มโยกไปมา")]
        [SerializeField] private float swayStart = 15f;
        [Tooltip("เริ่มหน้ามืด — ขอบจอค่อยๆ หุบเข้ามา พร้อมภาพหรี่ลง")]
        [SerializeField] private float darkenStart = 5f;
        [Tooltip("กลั้นได้นานสุดก่อนถูกบังคับหายใจออก — ควรมากกว่า swayStart เล็กน้อยเพื่อให้เห็นจังหวะโยก")]
        [SerializeField] private float maxHoldDuration = 18f;
        [Tooltip("ต้องรอกี่วินาทีถึงจะกลั้นได้อีกครั้ง")]
        [SerializeField] private float cooldownDuration = 5f;

        [Header("Movement")]
        [Tooltip("ตัวคูณความเร็วเดินระหว่างกลั้นหายใจ (ยิ่งน้อยยิ่งช้า)")]
        [SerializeField] private float slowMoveMultiplier = 0.3f;

        [Header("Effect Strength")]
        [Tooltip("ค่า saturation ตอนซีดสุด (-100 = ขาวดำ)")]
        [SerializeField] private float maxDesaturation = -85f;
        [Tooltip("focusDistance ตอนเบลอสุด (ยิ่งใกล้ ยิ่งเบลอทั้งจอ)")]
        [SerializeField] private float blurFocusDistance = 0.15f;
        [Tooltip("aperture ตอนเบลอสุด (ยิ่งน้อย ยิ่งเบลอแรง)")]
        [SerializeField] private float blurAperture = 1.2f;
        [Tooltip("องศาการโยกสูงสุด")]
        [SerializeField] private float maxSwayAngle = 6f;
        [Tooltip("ความถี่การโยก")]
        [SerializeField] private float swaySpeed = 1.6f;

        [Header("Faint (หน้ามืด)")]
        [Tooltip("ความเข้ม vignette ตอนหน้ามืดสุด (ค่าเดิมใน profile ปกติ ~0.2)")]
        [SerializeField] private float maxVignetteIntensity = 0.75f;

        [Tooltip("smoothness ของ vignette ตอนหน้ามืดสุด — ยิ่งใกล้ 1 ยิ่งกินพื้นที่จอ")]
        [SerializeField] private float maxVignetteSmoothness = 1f;

        [Tooltip("postExposure ตอนหน้ามืดสุด (EV ยิ่งติดลบยิ่งมืด)\n"
                 + "อย่าลงเยอะเกินจนมองทางไม่เห็น ผู้เล่นยังต้องเดินหนีผีได้")]
        [SerializeField] private float maxExposureDarkening = -3f;

        // ── Runtime ──
        private bool  _ruleActive;
        private bool  _holding;
        private float _holdTimer;
        private float _cooldownTimer;

        // ช่วง "ค่อยๆ ฟื้น" หลังปล่อยการกลั้น — กินเวลาเท่ากับ cooldownDuration พอดี
        // เก็บ _recoverFromT ไว้เพื่อเดินเส้นโค้งเอฟเฟกต์ถอยหลังจากจุดที่ปล่อย ไม่ใช่ตัดจบทันที
        private bool  _recovering;
        private float _recoverTimer;
        private float _recoverFromT;

        private ColorAdjustments _colorAdjustments;
        private DepthOfField     _depthOfField;
        private bool             _profileCached;
        private float            _origSaturation;
        private float            _origFocusDistance;
        private float            _origAperture;
        private Vignette         _vignette;
        private float            _origVignetteIntensity;
        private float            _origVignetteSmoothness;
        private float            _origPostExposure;

        private Quaternion _origCamLocalRot = Quaternion.identity;
        private bool       _camRotCached;

        /// <summary>true = กำลังกลั้นหายใจอยู่ (ผีมองไม่เห็น)</summary>
        public bool IsHolding => _holding;

        /// <summary>ยิงเมื่อผู้เล่นกลั้นไม่ไหวจนถูกบังคับหายใจออก — Rule4 ใช้สั่งให้ผีวิ่งเข้ามา</summary>
        public event Action OnForcedExhale;

        // ─────────────────────────── Lifecycle ───────────────────────────

        /// <summary>เปิดใช้ระบบกลั้นหายใจ — เรียกตอน Rule 4 เริ่มเล่นจริง</summary>
        public void BeginRule()
        {
            _ruleActive    = true;
            _holdTimer     = 0f;
            _cooldownTimer = 0f;
            CacheCameraRotation();
        }

        /// <summary>ปิดระบบ + คืนค่าทุกอย่างกลับเป็นปกติ (จบกฎ / ตาย)</summary>
        public void EndRuleCleanup()
        {
            _ruleActive = false;
            if (_holding) ReleaseBreath(forced: false, silent: true);
            _recovering = false;
            RestoreProfile();
            RestoreCameraRotation();
            SetHandVisual(false);
            AudioManager.instance.StopBreathRecover();
            if (PlayerController.Instance != null) PlayerController.Instance.SetMoveSpeedMultiplier(1f);
        }

        // กันค่าค้างใน asset ตอนกด Stop ใน Editor กลางคัน
        private void OnDisable()
        {
            _recovering = false;
            RestoreProfile();
            RestoreCameraRotation();
            SetHandVisual(false);
        }

        /// <summary>เปิด/ปิดภาพมือปิดจมูกที่บังด้านล่างจอ</summary>
        private void SetHandVisual(bool visible)
        {
            if (holdBreathHandVisual != null) holdBreathHandVisual.SetActive(visible);
            if (handAnimator != null && !string.IsNullOrEmpty(handHoldingBoolName))
                handAnimator.SetBool(handHoldingBoolName, visible);
        }

        private void Update()
        {
            if (!_ruleActive) return;

            if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

            if (_recovering) UpdateRecovery();

            bool wantHold = inputHandler != null && inputHandler.IsHoldBreathHeld();

            if (_holding)
            {
                _holdTimer += Time.deltaTime;
                ApplyEffects(_holdTimer);
                AudioManager.instance.SetBreathStrain(Mathf.Clamp01(_holdTimer / maxHoldDuration));

                if (_holdTimer >= maxHoldDuration)      ReleaseBreath(forced: true,  silent: false);
                else if (!wantHold)                     ReleaseBreath(forced: false, silent: false);
            }
            else if (wantHold && _cooldownTimer <= 0f)
            {
                StartHolding();
            }
        }

        // ─────────────────────────── Hold / Release ───────────────────────────

        private void StartHolding()
        {
            _holding    = true;
            _holdTimer  = 0f;
            _recovering = false;   // ตัดช่วงฟื้นทิ้ง ค่า _orig* ที่ cache ไว้ยังใช้ได้อยู่

            CacheProfile();
            CacheCameraRotation();

            SetHandVisual(true);

            if (PlayerController.Instance != null) PlayerController.Instance.SetMoveSpeedMultiplier(slowMoveMultiplier);
            AudioManager.instance.StartHoldBreath();
        }

        private void ReleaseBreath(bool forced, bool silent)
        {
            float heldFor = _holdTimer;   // ต้องเก็บก่อน reset — ใช้เป็นจุดตั้งต้นของการค่อยๆ ฟื้น

            _holding   = false;
            _holdTimer = 0f;

            // cooldown เท่ากันทุกกรณี ไม่ว่าจะกลั้นสั้นหรือกลั้นจนถูกบังคับหายใจออก
            _cooldownTimer = cooldownDuration;

            SetHandVisual(false);
            if (PlayerController.Instance != null) PlayerController.Instance.SetMoveSpeedMultiplier(1f);

            if (silent)
            {
                // จบกฎ / ตาย — คืนค่าทันที ไม่ต้องมีช่วงฟื้น
                _recovering = false;
                RestoreProfile();
                RestoreCameraRotation();
                AudioManager.instance.StopBreathRecover();
                return;
            }

            // อาการหน้ามืดค่อยๆ จางหายภายใน cooldownDuration แทนที่จะดีดกลับทันที
            _recovering   = true;
            _recoverFromT = heldFor;
            _recoverTimer = cooldownDuration;

            AudioManager.instance.StopHoldBreath();
            AudioManager.instance.StartBreathRecover();

            if (forced)
            {
                AudioManager.instance.PlayForcedExhale();
                OnForcedExhale?.Invoke();   // → ผีวิ่งเข้ามาหาผู้เล่น
            }
        }

        /// <summary>
        /// ค่อยๆ คลายเอฟเฟกต์กลับสู่ปกติตลอดช่วง cooldown
        /// วิธีคือเดินค่า t ของ ApplyEffects ถอยหลังจากจุดที่ปล่อยกลับไปหา 0
        /// ทำให้เส้นโค้งทุกอย่าง (เทา / เบลอ / หน้ามืด / โยก) คลายพร้อมกันอย่างเป็นธรรมชาติ
        /// </summary>
        private void UpdateRecovery()
        {
            _recoverTimer -= Time.deltaTime;

            float k = cooldownDuration > 0f ? Mathf.Clamp01(_recoverTimer / cooldownDuration) : 0f;
            ApplyEffects(_recoverFromT * k);

            if (_recoverTimer > 0f) return;

            _recovering = false;
            RestoreProfile();
            RestoreCameraRotation();
            AudioManager.instance.StopBreathRecover();   // การันตีว่าเสียงหายใจจบพร้อม cooldown
        }

        // ─────────────────────────── Effects ───────────────────────────

        private void ApplyEffects(float t)
        {
            // 1) ซีดเป็นสีเทา
            if (_colorAdjustments != null)
            {
                float gray = Mathf.InverseLerp(grayStart, maxHoldDuration, t);
                _colorAdjustments.saturation.value = Mathf.Lerp(_origSaturation, maxDesaturation, gray);
            }

            // 2) เบลอ
            if (_depthOfField != null)
            {
                float blur = Mathf.InverseLerp(blurStart, maxHoldDuration, t);
                _depthOfField.focusDistance.value = Mathf.Lerp(_origFocusDistance, blurFocusDistance, blur);
                _depthOfField.aperture.value      = Mathf.Lerp(_origAperture,      blurAperture,      blur);
            }

            // 3) หน้ามืด — vignette หุบเข้ามาเป็น tunnel vision พร้อมหรี่ทั้งภาพ
            //    ใช้สองอย่างคู่กันถึงจะได้ความรู้สึก "จะเป็นลม" ไม่ใช่แค่จอมืดเฉยๆ
            float faint = Mathf.InverseLerp(darkenStart, maxHoldDuration, t);

            if (_vignette != null)
            {
                _vignette.intensity.value  = Mathf.Lerp(_origVignetteIntensity,  maxVignetteIntensity,  faint);
                _vignette.smoothness.value = Mathf.Lerp(_origVignetteSmoothness, maxVignetteSmoothness, faint);
            }

            if (_colorAdjustments != null)
                _colorAdjustments.postExposure.value = Mathf.Lerp(_origPostExposure, maxExposureDarkening, faint);

            // 4) โยกไปมา
            if (cameraTransform != null && _camRotCached)
            {
                float sway = Mathf.InverseLerp(swayStart, maxHoldDuration, t) * maxSwayAngle;
                if (sway > 0f)
                {
                    float phase = t * swaySpeed;
                    cameraTransform.localRotation = _origCamLocalRot * Quaternion.Euler(
                        Mathf.Sin(phase * 1.3f) * sway * 0.4f,
                        0f,
                        Mathf.Sin(phase)        * sway
                    );
                }
                else
                {
                    // ต้องเขียนกลับด้วย ไม่งั้นตอนค่อยๆ ฟื้น กล้องจะค้างมุมเอียงสุดท้ายไว้
                    cameraTransform.localRotation = _origCamLocalRot;
                }
            }
        }

        // ─────────────────────────── Profile cache / restore ───────────────────────────

        private void CacheProfile()
        {
            if (_profileCached) return;
            if (GameModeController.instance == null || GameModeController.instance.globalVolume == null) return;

            VolumeProfile profile = GameModeController.instance.globalVolume.profile;
            profile.TryGet(out _colorAdjustments);
            profile.TryGet(out _depthOfField);
            profile.TryGet(out _vignette);

            _origSaturation    = _colorAdjustments != null ? _colorAdjustments.saturation.value    : 0f;
            _origPostExposure  = _colorAdjustments != null ? _colorAdjustments.postExposure.value  : 0f;
            _origFocusDistance = _depthOfField     != null ? _depthOfField.focusDistance.value     : 10f;
            _origAperture      = _depthOfField     != null ? _depthOfField.aperture.value          : 5.6f;

            _origVignetteIntensity  = _vignette != null ? _vignette.intensity.value  : 0.2f;
            _origVignetteSmoothness = _vignette != null ? _vignette.smoothness.value : 0.2f;

            _profileCached     = true;
        }

        private void RestoreProfile()
        {
            if (!_profileCached) return;

            if (_colorAdjustments != null)
            {
                _colorAdjustments.saturation.value   = _origSaturation;
                _colorAdjustments.postExposure.value = _origPostExposure;
            }
            if (_depthOfField != null)
            {
                _depthOfField.focusDistance.value = _origFocusDistance;
                _depthOfField.aperture.value      = _origAperture;
            }
            if (_vignette != null)
            {
                _vignette.intensity.value  = _origVignetteIntensity;
                _vignette.smoothness.value = _origVignetteSmoothness;
            }
            _profileCached = false; // ให้ cache ใหม่รอบหน้า (ค่าอาจเปลี่ยนตาม mode)
        }

        private void CacheCameraRotation()
        {
            if (cameraTransform == null || _camRotCached) return;
            _origCamLocalRot = cameraTransform.localRotation;
            _camRotCached    = true;
        }

        private void RestoreCameraRotation()
        {
            if (!_camRotCached || cameraTransform == null) return;
            cameraTransform.localRotation = _origCamLocalRot;
            _camRotCached = false;
        }
    }
}
