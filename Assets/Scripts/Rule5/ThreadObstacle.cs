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
    ///
    /// Bind() คำนวณตำแหน่งบนเส้นแต่ "ไม่โชว์ตัว" — ThreadObstacleSpawner เป็นคนเลือกจังหวะ Reveal()
    /// ตอนที่จุดนั้นอยู่นอกสายตาผู้เล่น จึงไม่มีใครเห็นมันโผล่
    /// </summary>
    public class ThreadObstacle : MonoBehaviour
    {
        [SerializeField] private ObstacleKind kind = ObstacleKind.Detour;

        [Tooltip("Blocked span along the thread, in metres, measured both ways from where this object projects onto the line.")]
        [SerializeField] private float blockRadius = 1.2f;

        [Tooltip("Detour only: how far past the blocked span the re-grab cloth is placed, in metres.")]
        [SerializeField] private float regrabMargin = 0.6f;

        [Tooltip("Untangle only: seconds of holding E needed to free the thread.")]
        [SerializeField] private float untangleDuration = 3f;

        [Tooltip("Remove the obstacle once the player is past it. Suits Untangle, where the tangle should disappear.")]
        [SerializeField] private bool clearAfterPass = false;

        [Tooltip("Model or effect to hide when the obstacle is shown/hidden. Leave empty to toggle this whole GameObject.")]
        [SerializeField] private GameObject visual;

        [Tooltip("Hint shown when the player walks into it. Optional.")]
        [SerializeField] private string hint = "มีอะไรขวางอยู่… ต้องปล่อยมือแล้วเดินอ้อม";

        [Tooltip("Further than this from the thread counts as not on it: the obstacle is skipped and a warning is logged.")]
        [SerializeField] private float maxDistanceFromThread = 3f;

        [Header("Physical blocker (Detour only)")]
        [Tooltip("Put a solid box across the thread so the player has to walk around instead of straight through. " +
                 "Letting go of the thread hands normal movement back, and nothing stops the player from walking " +
                 "through a model that has no collider of its own. Untick when the model already blocks the way properly.")]
        [SerializeField] private bool blockPlayerPhysically = true;

        [Tooltip("Height of that box, in metres.")]
        [SerializeField] private float blockerHeight = 2.2f;

        [Tooltip("Width of that box across the thread, in metres. Wide enough to stop the player squeezing past the thread side.")]
        [SerializeField] private float blockerWidth = 1.6f;

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

        /// <summary>true = โชว์ตัวอยู่แล้ว (ถูก Reveal แล้ว)</summary>
        public bool IsRevealed { get; private set; }

        // กล่องตันที่ระบบสร้างเองคร่อมเส้นสายสิญจน์ — ไม่ใช่ collider ของโมเดล
        private GameObject _blocker;

        /// <summary>ระยะที่หน้ากล่องร่นเข้าไปจากขอบเขตกั้นแต่ละข้าง (เมตร)</summary>
        private const float BlockerInset = 0.3f;

        /// <summary>จุดกลางของสิ่งกีดขวางบนเส้น (เมตรจากต้นสาย)</summary>
        public float BlockCenter => (BlockStart + BlockEnd) * 0.5f;

        /// <summary>
        /// หาตำแหน่งตัวเองบนเส้น — ไม่โชว์ตัว ต้องเรียก Reveal() ต่อ
        /// คืน false ถ้าวางไว้ห่างเส้นเกินไป (ตัวนั้นจะถูกข้าม)
        /// </summary>
        public bool Bind(SacredThreadPath path)
        {
            Cleared = false;
            float center = path.ClosestDistanceAlong(transform.position, out float off);

            IsValid = off <= maxDistanceFromThread;
            if (!IsValid)
            {
                Debug.LogWarning($"[Rule5] สิ่งกีดขวาง '{name}' อยู่ห่างสาย {off:0.0} ม. (เกิน {maxDistanceFromThread}) — ข้าม", this);
                return false;
            }

            BlockStart = center - blockRadius;
            BlockEnd   = center + blockRadius;
            EnsureBlocker(path);
            return true;
        }

        /// <summary>
        /// สร้าง/วางกล่องตันคร่อมเส้นตรงช่วงที่กั้น
        ///
        /// การกั้นด้วย BlockStart/BlockEnd เป็นเรื่องของ "ระยะบนเส้น" ซึ่งมีผลเฉพาะตอนที่ยังจับสายอยู่
        /// พอเป็น Detour มือจะหลุดทันที แล้วผู้เล่นได้การเดินปกติคืน — ถ้าโมเดลไม่มี collider
        /// ก็เดินทะลุตรงไปจับต่ออีกฝั่งได้เลย ไม่ต้องอ้อม กล่องนี้คือตัวกันเรื่องนั้น
        ///
        /// Untangle ไม่ต้องมี เพราะทางแก้คือกด E ค้าง ไม่ใช่เดินอ้อม
        /// </summary>
        private void EnsureBlocker(SacredThreadPath path)
        {
            if (!blockPlayerPhysically || kind != ObstacleKind.Detour || path == null)
            {
                if (_blocker != null) _blocker.SetActive(false);
                return;
            }

            if (_blocker == null)
            {
                _blocker = new GameObject(name + "_Blocker");
                _blocker.transform.SetParent(transform, false);
                _blocker.AddComponent<BoxCollider>();
            }

            // หักสเกลของพ่อออก กล่องจะได้มีขนาดเป็นเมตรจริงเสมอ ไม่ว่าโมเดลจะถูกย่อ/ขยายมาแค่ไหน
            Vector3 s = transform.lossyScale;
            _blocker.transform.localScale = new Vector3(
                Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
                Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
                Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);

            float center = BlockCenter;
            _blocker.transform.SetPositionAndRotation(
                path.GetGroundPoint(center),
                Quaternion.LookRotation(path.GetForward(center), Vector3.up));

            // สั้นกว่าช่วงที่กั้นอยู่ข้างละ BlockerInset — ผู้เล่นที่ถูกปล่อยมือตรงขอบเขตกั้นพอดี
            // จะได้ยืนห่างกล่องนิดหน่อย ไม่ไปจมอยู่ในกล่องแล้วโดน CharacterController ดันออกมั่วๆ
            var box = _blocker.GetComponent<BoxCollider>();
            box.isTrigger = false;
            box.size      = new Vector3(blockerWidth, blockerHeight,
                                        Mathf.Max(0.2f, (BlockEnd - BlockStart) - BlockerInset * 2f));
            box.center    = new Vector3(0f, blockerHeight * 0.5f, 0f);

            _blocker.SetActive(IsRevealed);
        }

        /// <summary>โชว์ตัว — Spawner เรียกตอนจุดนี้อยู่นอกสายตาผู้เล่น</summary>
        public void Reveal()
        {
            IsRevealed = true;
            if (visual != null) visual.SetActive(true); else gameObject.SetActive(true);
            if (_blocker != null) _blocker.SetActive(true);
        }

        /// <summary>ซ่อนตัว (ยังไม่ถึงคิวโผล่ / เคลียร์ตอนจบกฎ) — ยังกั้นทางไม่ได้</summary>
        public void Hide()
        {
            IsRevealed = false;
            if (_blocker != null) _blocker.SetActive(false);
            if (visual != null) visual.SetActive(false); else gameObject.SetActive(false);
        }

        /// <summary>ผู้เล่นผ่านไปแล้ว (อ้อมสำเร็จ / แก้หลุด)</summary>
        public void MarkPassed()
        {
            Cleared = true;
            if (clearAfterPass) Hide();
        }

        /// <summary>true = กั้นทางอยู่จริงตอนนี้ (โชว์ตัวแล้ว + ยังไม่ถูกผ่าน + วางถูกที่)</summary>
        public bool IsBlocking => IsValid && IsRevealed && !Cleared;

        private void OnDrawGizmos()
        {
            Gizmos.color = kind == ObstacleKind.Untangle ? new Color(1f, 0.5f, 0f) : Color.magenta;
            Gizmos.DrawWireSphere(transform.position, blockRadius);

            // กล่องกันตัวผู้เล่น — เห็นขนาดจริงตั้งแต่ตอนจัดฉาก (ตัวจริงสร้างตอนเริ่มกฎ)
            if (!blockPlayerPhysically || kind != ObstacleKind.Detour) return;
            Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * blockerHeight * 0.5f,
                                          transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(blockerWidth, blockerHeight,
                                Mathf.Max(0.2f, blockRadius * 2f - BlockerInset * 2f)));
        }
    }
}
