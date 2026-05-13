using System.Collections;
using UnityEngine;

namespace Rule2
{
    /// <summary>
    /// ควบคุมคันโยกเดี่ยว 1 ตัว
    ///
    /// การใช้งาน (จาก LeverPuzzleController):
    ///   lever.SetRange(0.35f, 0.60f);   // กำหนด zone สีเขียว
    ///   lever.ResetToZero();            // รีเซ็ตตำแหน่ง
    ///   lever.SetHighlight(true);       // ไฮไลต์ว่าเป็นตัวที่กำลังปรับ
    ///   lever.Grab();                   // จับคันโยก (หยุด drift)
    ///   lever.Release();               // ปล่อยคันโยก
    ///   lever.StartDrift();            // เริ่มไหลลง (Phase 2, เรียกหลัง Release)
    ///   lever.StopDrift();             // หยุดไหล
    ///
    /// Setup:
    ///   1. ติด Collider (ไม่ต้อง IsTrigger) บน GameObject นี้ — ใช้ตรวจ Raycast click-to-grab
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LeverHandle : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Lever 3D")]
        [Tooltip("Transform ของคันโยกที่จะเลื่อนขึ้น-ลง (child ของ slot)")]
        [SerializeField] private Transform leverTransform;
        [Tooltip("Y local ที่ตรงกับ 0% (ล่างสุด)")]
        [SerializeField] private float leverYAtZero = -0.12f;
        [Tooltip("Y local ที่ตรงกับ 100% (บนสุด)")]
        [SerializeField] private float leverYAtFull =  0.12f;

        [Header("Green Zone Cube")]
        [Tooltip("Cube แสดงช่วงที่ยอมรับ — ต้องอยู่ใน parent เดียวกับ leverTransform\n" +
                 "เพื่อให้ local coordinate ตรงกัน\n" +
                 "ตั้ง scale.y = 1 ที่ความสูง 1 unit (Unity default cube)")]
        [SerializeField] private Transform greenZoneTransform;

        [Header("Value Label (Optional)")]
        [Tooltip("TMP_Text แสดง % ปัจจุบัน (ถ้าไม่ต้องการให้เว้นว่าง)")]
        [SerializeField] private TMPro.TMP_Text valueLabel;

        [Header("Highlight")]
        [Tooltip("Renderer ของตัวคันโยก (เปลี่ยนสีเพื่อไฮไลต์)")]
        [SerializeField] private Renderer leverRenderer;
        [SerializeField] private Color normalColor    = Color.white;
        [SerializeField] private Color activeColor    = Color.yellow;
        [SerializeField] private Color grabbedColor   = new Color(1f, 0.6f, 0f); // orange
        [SerializeField] private Color completedColor = Color.green;
        [SerializeField] private Color failColor      = Color.red;

        [Header("Phase 2 Drift")]
        [Tooltip("ความเร็วที่คันโยกไหลลงต่อวินาที (0–1 scale)\nแนะนำ 0.05–0.10")]
        [SerializeField] private float driftSpeed = 0.07f;

        [Header("Zone Entry Audio (optional FMOD event)")]
        [Tooltip("เสียงคลิก/ติ๊งเมื่อ lever เข้า zone สีเขียว\nเว้นว่างถ้าไม่ต้องการ")]
        [SerializeField] private string enterZoneFMODEvent = "";

        // ─────────────────────────── Public State ───────────────────────────

        public float Value    { get; private set; }   // 0–1
        public float RangeMin { get; private set; }
        public float RangeMax { get; private set; }

        /// <summary>true ถ้าค่าปัจจุบันอยู่ใน range ที่ยอมรับ</summary>
        public bool IsInRange  => Value >= RangeMin && Value <= RangeMax;
        public bool IsDrifting => _drifting;
        public bool IsGrabbed  => _grabbed;

        /// <summary>Collider ของคันโยก — ใช้โดย LeverPuzzleController เพื่อตรวจ Raycast</summary>
        public Collider LeverCollider { get; private set; }

        // ─────────────────────────── Private ───────────────────────────

        private bool      _drifting;
        private Coroutine _driftCoroutine;
        private bool      _wasInRange;
        private bool      _grabbed;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Awake()
        {
            // หา Collider บน root ก่อน ถ้าไม่มีให้ลองหาจาก children
            LeverCollider = GetComponent<Collider>()
                         ?? GetComponentInChildren<Collider>();
        }

        // ─────────────────────────── Public API ───────────────────────────

        [Header("Green Zone Minimum Width")]
        [Tooltip("ความกว้างขั้นต่ำของ green zone (0–1) — ป้องกัน zone แคบเกินจนเล่นไม่ได้\n" +
                 "ค่านี้จะ override ค่าที่ส่งมาจาก controller ถ้าแคบกว่านี้")]
        [SerializeField] private float minZoneWidth = 0.18f;

        /// <summary>กำหนด range สีเขียว (0–1) และอัปเดต cube visual</summary>
        public void SetRange(float min, float max)
        {
            RangeMin = Mathf.Clamp01(min);
            // บังคับขั้นต่ำตาม minZoneWidth — จะไม่แคบกว่านี้ไม่ว่าจะสุ่มได้เท่าไร
            RangeMax = Mathf.Clamp01(Mathf.Max(max, min + minZoneWidth));
            RefreshGreenZone();
        }

        /// <summary>ตั้งค่า (0–1) และเลื่อน lever transform</summary>
        public void SetValue(float v)
        {
            Value = Mathf.Clamp01(v);

            ApplyLeverPosition();
            UpdateValueLabel();

            // ตรวจ zone entry
            bool inRange = IsInRange;
            if (inRange && !_wasInRange && !string.IsNullOrWhiteSpace(enterZoneFMODEvent))
                FMODUnity.RuntimeManager.PlayOneShot(enterZoneFMODEvent, transform.position);
            _wasInRange = inRange;
        }

        public void ResetToZero() => SetValue(0f);

        // ─────────────────────────── Grab / Release ───────────────────────────

        /// <summary>จับคันโยก — หยุด drift และเปลี่ยนสีเป็น orange</summary>
        public void Grab()
        {
            _grabbed = true;
            StopDrift();
            if (leverRenderer != null)
                leverRenderer.material.color = grabbedColor;
        }

        /// <summary>ปล่อยคันโยก — เปลี่ยนสีกลับเป็น active (controller เรียก StartDrift เองถ้า phase 2)</summary>
        public void Release()
        {
            _grabbed = false;
            if (leverRenderer != null)
                leverRenderer.material.color = activeColor;
        }

        // ─────────────────────────── Highlight ───────────────────────────

        public void SetHighlight(bool active)
        {
            if (leverRenderer == null) return;
            leverRenderer.material.color = active ? activeColor : normalColor;
        }

        public void SetCompletedVisual()
        {
            _grabbed = false;
            if (leverRenderer == null) return;
            leverRenderer.material.color = completedColor;
        }

        public void SetFailVisual()
        {
            _grabbed = false;
            if (leverRenderer == null) return;
            leverRenderer.material.color = failColor;
        }

        public void ResetVisual()
        {
            _grabbed = false;
            if (leverRenderer == null) return;
            leverRenderer.material.color = normalColor;
        }

        // ─────────────────────────── Phase 2 Drift ───────────────────────────

        public void StartDrift()
        {
            if (_grabbed) return;   // ไม่ drift ขณะจับอยู่
            if (_driftCoroutine != null) StopCoroutine(_driftCoroutine);
            _drifting       = true;
            _driftCoroutine = StartCoroutine(DriftRoutine());
        }

        public void StopDrift()
        {
            _drifting = false;
            if (_driftCoroutine != null)
            {
                StopCoroutine(_driftCoroutine);
                _driftCoroutine = null;
            }
        }

        private IEnumerator DriftRoutine()
        {
            while (_drifting && !_grabbed)
            {
                if (Value > 0f)
                    SetValue(Value - driftSpeed * Time.deltaTime);
                yield return null;
            }
            _drifting = false;
        }

        // ─────────────────────────── Private Helpers ───────────────────────────

        private void ApplyLeverPosition()
        {
            if (leverTransform == null) return;
            Vector3 pos = leverTransform.localPosition;
            pos.y       = Mathf.Lerp(leverYAtZero, leverYAtFull, Value);
            leverTransform.localPosition = pos;
        }

        private void RefreshGreenZone()
        {
            if (greenZoneTransform == null) return;

            // ── ใช้ Lerp เดียวกับ ApplyLeverPosition ──────────────────
            // การันตีว่า visual ตรงกับ IsInRange เสมอ
            float yBottom = Mathf.Lerp(leverYAtZero, leverYAtFull, RangeMin);
            float yTop    = Mathf.Lerp(leverYAtZero, leverYAtFull, RangeMax);
            float yMid    = (yBottom + yTop) * 0.5f;
            float height  = yTop - yBottom;   // ความสูงจริงของ zone ใน local units

            Vector3 pos = greenZoneTransform.localPosition;
            pos.y       = yMid;
            greenZoneTransform.localPosition = pos;

            // scale.y = ความสูงจริง (หน่วยเดียวกับ local space)
            // → Unity default cube scale.y=1 สูง 1 unit
            Vector3 scale = greenZoneTransform.localScale;
            scale.y       = Mathf.Max(height, 0.005f);
            greenZoneTransform.localScale = scale;
        }

        private void UpdateValueLabel()
        {
            if (valueLabel == null) return;
            valueLabel.text = $"{Mathf.RoundToInt(Value * 100f)}%";
        }
    }
}
