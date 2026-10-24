using UnityEngine;

namespace Rule6
{
    /// <summary>
    /// จุดที่ตัวละครในคัทซีนเดินไปยืน — ไม่มีพฤติกรรมอะไรตอนเล่น มีไว้วาดให้เห็นใน Scene view
    ///
    /// Transform เปล่ามองไม่เห็นในฉาก ลากไปวางในรถแทบไม่ได้เลย ตัวนี้วาดคนสูงเท่าตัวละคร
    /// (แคปซูล) พร้อมลูกศรทิศที่จะหันตอนเดินถึง — ขั้น Move To ใช้ทั้งตำแหน่งและทิศของจุดนี้
    /// </summary>
    public class CutsceneMarker : MonoBehaviour
    {
        [Tooltip("ความสูงของตัวละครที่วาด (เมตร) — ใช้ดูว่าหัวชนเพดานรถไหม")]
        [SerializeField] private float height = 1.7f;

        [SerializeField] private Color color = new Color(0.9f, 0.5f, 1f, 0.9f);

        private void OnDrawGizmos()
        {
            const float radius = 0.22f;

            Vector3 feet = transform.position;
            Vector3 head = feet + Vector3.up * height;

            Gizmos.color = color;
            Gizmos.DrawWireSphere(feet + Vector3.up * radius, radius);
            Gizmos.DrawWireSphere(head - Vector3.up * radius, radius);
            Gizmos.DrawLine(feet + transform.right * radius,  head + transform.right * radius);
            Gizmos.DrawLine(feet - transform.right * radius,  head - transform.right * radius);

            // ทิศที่จะหันตอนถึง — ระดับอก
            Vector3 chest = feet + Vector3.up * height * 0.75f;
            Gizmos.DrawRay(chest, transform.forward * 0.6f);
            Gizmos.DrawWireSphere(chest + transform.forward * 0.6f, 0.04f);
        }
    }
}
