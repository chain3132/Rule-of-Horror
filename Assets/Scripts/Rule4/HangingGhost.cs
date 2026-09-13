using System;
using UnityEngine;

namespace Rule4
{
    /// <summary>
    /// ผีผูกคอตายที่โผล่ตอน Rule 4 เริ่ม — ไม่ไล่ ไม่ทำอะไร แค่แขวนอยู่ให้เห็น
    /// (Animator บน prefab เล่น BeforeChase → Hang เองตั้งแต่ Entry ไม่ต้องสั่งอะไรจากตรงนี้)
    ///
    /// พอผู้เล่นวางตุ๊กตาตัวแรกสำเร็จ Rule4 จะเรียก Vanish() → ตัวผีหายไป เหลือแต่เชือกแขวนอยู่
    /// </summary>
    public class HangingGhost : MonoBehaviour
    {
        [Tooltip("ส่วน 'ตัวผี' ที่จะหายไปตอน Vanish — เว้นว่างได้ จะปิด renderer ทุกตัวที่ไม่ใช่เชือกแทน")]
        [SerializeField] private GameObject body;

        [Tooltip("เชือก — ส่วนที่ยังอยู่หลังผีหายไป (เว้นว่างได้ถ้าเชือกเป็น object แยกใน scene)")]
        [SerializeField] private GameObject rope;

        private Animator animator;

        public bool HasVanished { get; private set; }

        public void Start()
        {
            animator = GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger("isHanging");
            }
        }

        /// <summary>ผีหายไป เหลือแต่เชือก — เรียกซ้ำไม่มีผล</summary>
        public void Vanish()
        {
            if (HasVanished) return;
            HasVanished = true;

            
            if (animator != null) animator.enabled = false;

            if (body != null)
            {
                body.SetActive(false);
                return;
            }

            // ไม่ได้ระบุ body → ซ่อนทุก renderer ยกเว้นที่อยู่ใต้เชือก
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (rope != null && r.transform.IsChildOf(rope.transform)) continue;
                r.enabled = false;
            }
        }
    }
}
