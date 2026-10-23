using UnityEngine;

namespace Rule6
{
    /// <summary>
    /// วาดกล่องห้องโดยสารให้เห็นใน Scene view — ตัวนี้ไม่มีพฤติกรรมอะไรตอนเล่นเลย
    ///
    /// กล่องจริงคือ Transform ของ object นี้ (ขนาด = Scale, 1 = 1 เมตร) ส่วน Rule6Bus เป็นคน
    /// อ่านมันตอนเช็กว่าผู้เล่นลงจากรถแล้วหรือยัง แต่ object เปล่าไม่มีอะไรให้มอง ถ้าวาดกล่อง
    /// จากฝั่งรถ จะเห็นเฉพาะตอนเลือกตัวรถ แล้วพอคลิกตัวกล่องเพื่อจะ Scale กล่องจะหายไปพอดี
    /// เลยให้กล่องวาดตัวเอง
    ///
    /// วาดจางๆ ตลอดเวลา (เห็นว่ามันคลุมรถอยู่ตรงไหนโดยไม่ต้องเลือก) และวาดทึบขึ้นตอนเลือก
    /// (เห็นชัดว่ากินเข้าไปในโมเดลแค่ไหนตอนกำลังลาก)
    ///
    /// ไม่ใช้ BoxCollider แทน ถึงมันจะมีกล่องเขียวให้ฟรีก็ตาม — ต่อให้เป็น trigger มันก็ยัง
    /// ยิง OnTriggerEnter ใส่สคริปต์โซนทุกตัวที่รถวิ่งผ่าน
    /// </summary>
    public class BusCabinBounds : MonoBehaviour
    {
        private static readonly Color Line = new Color(1f, 0.55f, 0.15f, 0.9f);
        private static readonly Color Fill = new Color(1f, 0.55f, 0.15f, 0.12f);

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color  = new Color(Line.r, Line.g, Line.b, 0.35f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color  = Fill;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color  = Line;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
