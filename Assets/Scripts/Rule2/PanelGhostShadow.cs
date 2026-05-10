using System.Collections;
using UnityEngine;

namespace Rule2
{
    /// <summary>
    /// ติดไว้บน Prefab เงาผีข้างตู้ไฟ
    /// เมื่อผู้เล่นมองตรงมาในระยะใกล้พอ → เงาหายทันที
    /// </summary>
    public class PanelGhostShadow : MonoBehaviour
    {
        [Tooltip("มุมกล้อง (องศา) ที่นับว่า 'มองเจอ'")]
        [SerializeField] private float detectionAngle = 35f;

        [Tooltip("ระยะ (m) ที่นับว่า 'มองเจอ'")]
        [SerializeField] private float detectionDistance = 8f;

        [Tooltip("Layer ที่บังสายตา (Wall, Obstacle ฯลฯ) — ถ้าไม่ตั้งจะตรวจแค่มุม+ระยะ")]
        [SerializeField] private LayerMask occlusionMask;

        private bool _disappearing = false;

        void Update()
        {
            if (_disappearing || Camera.main == null) return;

            Vector3 camPos = Camera.main.transform.position;
            Vector3 dirToShadow = transform.position - camPos;
            float distance = dirToShadow.magnitude;

            // เกินระยะ → ไม่นับ
            if (distance > detectionDistance) return;

            // มุมกล้อง → ถ้าอยู่ใน cone → ถือว่ามองเจอ
            float angle = Vector3.Angle(Camera.main.transform.forward, dirToShadow);
            if (angle > detectionAngle) return;

            // ตรวจสิ่งกีดขวาง (ถ้า set occlusionMask ไว้)
            if (occlusionMask.value != 0)
            {
                if (Physics.Raycast(camPos, dirToShadow.normalized, distance, occlusionMask))
                    return; // มีกำแพงบัง → ยังไม่เจอ
            }

            // ผู้เล่นมองเจอ → หายไป
            StartCoroutine(DisappearRoutine());
        }

        IEnumerator DisappearRoutine()
        {
            _disappearing = true;

            // Optional: เล่น animation หายก่อน destroy
            // ถ้า prefab มี Animator ก็ SetTrigger("Disappear") แล้ว yield รอ

            Destroy(gameObject);
            yield break;
        }
    }
}
