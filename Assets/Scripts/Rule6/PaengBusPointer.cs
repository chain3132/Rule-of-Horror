using UnityEngine;

namespace Rule6
{
    /// <summary>
    /// แป้ง — ยืนอยู่หน้าศาลาตลอดกฎข้อ 6 และชี้ไปที่รถคันที่ 2 ตอนมันมาจอด
    ///
    /// นี่คือเบาะแสเดียวที่เกมให้ตรงๆ ว่าคันไหนคือทางรอด ผู้เล่นที่อ่านเรื่องของแป้งมาจะเชื่อ
    /// ส่วนผู้เล่นที่ไม่รู้เรื่องจะเห็นรถเก่าไฟสลัวที่ไม่มีคนนั่ง แล้วคิดว่าคันที่ 1 น่าขึ้นกว่า
    /// การชี้จึงต้องเห็นชัด แต่ต้องไม่มีข้อความกำกับ — ถ้าบอกเป็นตัวหนังสือว่า "ขึ้นคันนี้"
    /// กับดักของกฎจะหมดความหมายทันที
    ///
    /// ตัวนี้ไม่รู้จักรถคันไหนเลย Rule6 เป็นคนบอกว่าให้ชี้ที่ไหนและเมื่อไหร่
    /// </summary>
    public class PaengBusPointer : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("ท่าทาง")]
        [Tooltip("Animator ของแป้ง — เว้นว่างได้ถ้าใช้วิธีเปิด/ปิดโมเดลแขนที่ชี้แทน")]
        [SerializeField] private Animator animator;

        [Tooltip("Trigger ตอนเริ่มชี้")]
        [SerializeField] private string pointTrigger = "point";

        [Tooltip("Trigger ตอนเลิกชี้ (กลับไปยืนเฉยๆ)")]
        [SerializeField] private string idleTrigger = "idle";

        [Tooltip("โมเดล/แขนที่โผล่มาเฉพาะตอนชี้ — ทางเลือกสำหรับคนที่ยังไม่มีอนิเมชั่น\n" +
                 "ใส่คู่กับ Animator ได้ ไม่ขัดกัน")]
        [SerializeField] private GameObject pointingVisual;

        // ─────────────────────────── การหัน ───────────────────────────

        [Header("การหัน")]
        [Tooltip("✓ = หันตัวไปทางรถตอนชี้ ไม่งั้นจะชี้ไปข้างหลังตัวเองได้ถ้ารถมาจอดอีกฝั่ง")]
        [SerializeField] private bool faceTargetWhenPointing = true;

        [Tooltip("จุดที่แป้งหันไปตอนยืนเฉยๆ — ปกติใส่ม้านั่งที่ศาลา เพื่อให้เหมือนมองผู้เล่นอยู่\n" +
                 "เว้นว่าง = ไม่หันเลย ใช้ท่าที่วางไว้ใน scene")]
        [SerializeField] private Transform idleFaceTarget;

        [Tooltip("ความเร็วหันตัว (องศา/วินาที) — ช้าไว้ให้ดูเหมือนคนที่ไม่รีบ")]
        [SerializeField] private float turnSpeed = 90f;

        // ─────────────────────────── Runtime ───────────────────────────

        private Transform _faceTarget;
        private bool      _pointing;

        /// <summary>true = กำลังชี้รถอยู่ (ใช้โดย Rule6DevSkip)</summary>
        public bool IsPointing => _pointing;

        // ─────────────────────────── API ───────────────────────────

        /// <summary>กฎเริ่ม — แป้งโผล่มายืนหน้าศาลา</summary>
        public void Appear()
        {
            gameObject.SetActive(true);
            StopPointing();
        }

        /// <summary>กฎจบ / บทจบเล่นแล้ว — เก็บแป้งออก</summary>
        public void Disappear()
        {
            StopPointing();
            gameObject.SetActive(false);
        }

        /// <summary>ชี้ไปที่จุดหนึ่ง (ประตูรถคันที่ 2)</summary>
        public void PointAt(Transform target)
        {
            if (target == null) { StopPointing(); return; }

            _pointing   = true;
            _faceTarget = faceTargetWhenPointing ? target : idleFaceTarget;

            if (pointingVisual != null) pointingVisual.SetActive(true);
            if (animator != null && !string.IsNullOrEmpty(pointTrigger))
                animator.SetTrigger(pointTrigger);
        }

        /// <summary>เลิกชี้ — รถคันนั้นออกไปแล้ว</summary>
        public void StopPointing()
        {
            _pointing   = false;
            _faceTarget = idleFaceTarget;

            if (pointingVisual != null) pointingVisual.SetActive(false);
            if (animator != null && !string.IsNullOrEmpty(idleTrigger))
                animator.SetTrigger(idleTrigger);
        }

        // ─────────────────────────── Update ───────────────────────────

        private void Update()
        {
            if (_faceTarget == null) return;

            // หันแค่แกน yaw — รถจอดอยู่ระดับเดียวกับพื้น เงยหน้าตามจะดูเหมือนคอหัก
            Vector3 flat = _faceTarget.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(flat.normalized, Vector3.up),
                turnSpeed * Time.deltaTime);
        }
    }
}
