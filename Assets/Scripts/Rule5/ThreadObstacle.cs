using UnityEngine;

namespace Rule5
{
    /// <summary>วิธีผ่านสิ่งกีดขวางบนสายสิญจน์</summary>
    public enum ObstacleKind
    {
        /// <summary>ต้องปล่อยมือแล้วเดินอ้อม — ผ้าแดงจุดจับใหม่จะไปรออีกฝั่งของสิ่งกีดขวาง (ถุงห่อศพ / โรงศพ / ศาลพระภูมิ)</summary>
        Detour,

        /// <summary>เดินต่อไม่ได้ ต้องกด E ค้างแก้ให้หลุดก่อน โดยไม่ต้องปล่อยมือ (สายสิญจน์พันกัน)</summary>
        Untangle
    }

    /// <summary>
    /// สิ่งกีดขวางบนสายสิญจน์ — วางไว้ใกล้เส้นตรงไหนก็ได้ ตอนเริ่มกฎมันจะหาตำแหน่งของตัวเองบนเส้นให้เอง
    /// (ฉายตำแหน่ง transform ลงบนเส้น) แล้วกันช่วง ±blockRadius เมตร
    ///
    /// ย้ายสิ่งกีดขวาง = ลาก GameObject นี้ ไม่ต้องกรอกระยะเอง
    /// </summary>
    public class ThreadObstacle : MonoBehaviour
    {
        [SerializeField] private ObstacleKind kind = ObstacleKind.Detour;

        [Tooltip("ช่วงที่กั้นบนเส้น (เมตร) — วัดจากจุดที่ฉายลงเส้นออกไปทั้งสองข้าง")]
        [SerializeField] private float blockRadius = 1.2f;

        [Tooltip("Detour: ผ้าแดงจุดจับใหม่จะอยู่เลยขอบสิ่งกีดขวางไปอีกเท่านี้ (เมตร)")]
        [SerializeField] private float regrabMargin = 0.6f;

        [Tooltip("Untangle: กด E ค้างกี่วินาทีถึงจะแก้หลุด")]
        [SerializeField] private float untangleDuration = 3f;

        [Tooltip("ผ่านแล้วให้เก็บสิ่งกีดขวางออกเลยไหม (เหมาะกับ Untangle — สายที่พันกันหายไป)")]
        [SerializeField] private bool clearAfterPass = false;

        [Tooltip("โมเดล/เอฟเฟกต์ที่จะปิดเมื่อ clearAfterPass — เว้นว่าง = ปิดทั้ง GameObject นี้")]
        [SerializeField] private GameObject visual;

        [Tooltip("ข้อความตอนเดินมาชน (เว้นว่างได้)")]
        [SerializeField] private string hint = "มีอะไรขวางอยู่… ต้องปล่อยมือแล้วเดินอ้อม";

        [Tooltip("ไกลจากเส้นเกินนี้ถือว่าไม่ได้อยู่บนสาย — จะถูกข้าม + เตือนใน Console")]
        [SerializeField] private float maxDistanceFromThread = 3f;

        public ObstacleKind Kind             => kind;
        public float        UntangleDuration => untangleDuration;
        public string       Hint             => hint;
        public bool         Cleared          { get; private set; }
        public bool         ClearAfterPass   => clearAfterPass;

        /// <summary>ระยะบนเส้นที่เริ่มกั้น / เลิกกั้น (ยังไม่ wrap — Walker เป็นคนคิด loop)</summary>
        public float BlockStart  { get; private set; }
        public float BlockEnd    { get; private set; }
        public float RegrabPoint => BlockEnd + regrabMargin;

        public bool IsValid { get; private set; }

        /// <summary>หาตำแหน่งตัวเองบนเส้น — Walker เรียกตอนเริ่มกฎ</summary>
        public void Bind(SacredThreadPath path)
        {
            Cleared = false;
            float center = path.ClosestDistanceAlong(transform.position, out float off);

            IsValid = off <= maxDistanceFromThread;
            if (!IsValid)
            {
                Debug.LogWarning($"[Rule5] สิ่งกีดขวาง '{name}' อยู่ห่างสาย {off:0.0} ม. (เกิน {maxDistanceFromThread}) — ข้าม", this);
                return;
            }

            BlockStart = center - blockRadius;
            BlockEnd   = center + blockRadius;
            if (visual != null) visual.SetActive(true); else gameObject.SetActive(true);
        }

        /// <summary>ผู้เล่นผ่านไปแล้ว (อ้อมสำเร็จ / แก้หลุด)</summary>
        public void MarkPassed()
        {
            Cleared = true;
            if (!clearAfterPass) return;
            if (visual != null) visual.SetActive(false); else gameObject.SetActive(false);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = kind == ObstacleKind.Untangle ? new Color(1f, 0.5f, 0f) : Color.magenta;
            Gizmos.DrawWireSphere(transform.position, blockRadius);
        }
    }
}
