using Manager;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rule4
{
    /// <summary>
    /// เครื่องมือ dev สำหรับเทส Rule 4 โดยไม่ต้องเล่น Rule 1-3 ให้จบก่อน
    ///
    /// วิธีใช้:
    ///   1. แปะ component นี้ไว้บน GameObject ไหนก็ได้ใน scene แล้วลาก Rule4 ใส่ช่อง rule4
    ///   2. กด Play → กด Spacebar ข้าม intro → รอให้ตัวละครนั่งลงเสร็จ
    ///   3. กด F4 → เข้า Rule 4 ทันที
    ///
    /// ทำไมถึงไม่ชนกับกฎอื่น:
    ///   Rule4.RuleFlow() สั่ง IsPauseTime(true) เป็นบรรทัดแรก นาฬิกาเลยหยุดเดิน
    ///   RuleManager.CheckRules ทำงานตอน OnTimeChanged เท่านั้น → Rule 2/3 ไม่มีทาง trigger แทรก
    ///   ส่วน Rule 1 ตั้ง startHour = 23 อยู่แล้ว (trigger จากแชทเท่านั้น) จึงไม่ยุ่งด้วย
    ///
    /// ตัว component จะหยุดนาฬิกาให้ตั้งแต่ Start ด้วย กันกรณีมัวหาปุ่มอยู่แล้ว Rule 2 ชิงเริ่มก่อน
    /// </summary>
    public class Rule4DevSkip : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("ลาก GameObject ที่มี component Rule4 มาใส่")]
        [SerializeField] private RuleSystem.Rule.Rule4 rule4;

        [Tooltip("ปิดตัวนี้เมื่อจะส่งเกมจริง — ปิดแล้วปุ่มลัดทั้งหมดจะไม่ทำงาน")]
        [SerializeField] private bool enableDevSkip = true;

        [Header("Hotkeys")]
        [Tooltip("ปุ่มกระโดดเข้า Rule 4")]
        [SerializeField] private Key jumpKey = Key.F4;

        [Tooltip("ปุ่มเปิด/ปิดเส้น debug ชี้ไปยังตุ๊กตาที่เหลือ (ดูใน Scene view)")]
        [SerializeField] private Key toggleDollLinesKey = Key.F5;

        [Header("Clock")]
        [Tooltip("หยุดนาฬิกาทันทีที่เริ่มเกม — กัน Rule 2/3 ชิง trigger ระหว่างที่ยังไม่ได้กด F4")]
        [SerializeField] private bool freezeClockOnStart = true;

        [Tooltip("เวลาที่จะตั้งให้ก่อนเริ่ม Rule 4 (ปกติ 21:59 = ก่อนหน้าต่างของ Rule 4 พอดี)")]
        [SerializeField] private int jumpHour = 21;
        [SerializeField] private int jumpMinute = 59;

        [Header("Debug")]
        [Tooltip("วาดเส้นจากผู้เล่นไปยังตุ๊กตาที่ยังไม่ถูกเก็บ — จำเป็นมากตอนที่ FMOD ยังไม่มีเสียงตุ๊กตา\n" +
                 "ต้องเปิด Gizmos ใน Scene view ถึงจะเห็น")]
        [SerializeField] private bool showDollLines;

        // ─────────────────────────── Lifecycle ───────────────────────────

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!enableDevSkip) return;

            if (rule4 == null)
            {
                Debug.LogError("[Rule4DevSkip] ยังไม่ได้ลาก Rule4 ใส่ช่อง rule4 ใน Inspector", this);
                return;
            }

            if (freezeClockOnStart && TimeManager.instance != null)
            {
                TimeManager.instance.IsPauseTime(true);
                Debug.Log($"[Rule4DevSkip] หยุดนาฬิกาไว้แล้ว — กด Spacebar ข้าม intro แล้วกด {jumpKey} เพื่อเข้า Rule 4");
            }
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!enableDevSkip) return;

            // ย้ำหยุดนาฬิกาทุกเฟรมจนกว่าจะเข้า Rule 4
            // สั่งครั้งเดียวใน Start() ไม่พอ เพราะ CutsceneManager และ GameModeController.BlinkRoutine
            // สั่ง IsPauseTime(false) ทีหลังเสมอ พอเข้า Rule 4 แล้ว Rule4 เป็นคนคุมเอง
            if (freezeClockOnStart && rule4 != null && !rule4.ruleActive && TimeManager.instance != null)
                TimeManager.instance.IsPauseTime(true);

            if (Keyboard.current == null) return;

            if (Keyboard.current[jumpKey].wasPressedThisFrame) JumpToRule4();

            if (Keyboard.current[toggleDollLinesKey].wasPressedThisFrame)
            {
                showDollLines = !showDollLines;
                Debug.Log($"[Rule4DevSkip] เส้น debug ตุ๊กตา: {(showDollLines ? "เปิด" : "ปิด")}");
                if (showDollLines) LogDollPositions();
            }

            if (showDollLines) DrawDollLines();
#endif
        }

        // ─────────────────────────── Jump ───────────────────────────

        /// <summary>เริ่ม Rule 4 ทันที (เรียกจากปุ่ม หรือจาก context menu ก็ได้)</summary>
        [ContextMenu("Jump To Rule 4")]
        public void JumpToRule4()
        {
            if (rule4 == null)
            {
                Debug.LogError("[Rule4DevSkip] ยังไม่ได้ลาก Rule4 ใส่ช่อง rule4 ใน Inspector", this);
                return;
            }

            if (rule4.ruleActive)
            {
                Debug.LogWarning("[Rule4DevSkip] Rule 4 ทำงานอยู่แล้ว — ไม่ต้องกดซ้ำ", this);
                return;
            }

            // ตั้งเวลาให้อยู่ก่อนหน้าต่างของ Rule 4 พอดี
            // (Rule4.EndRule จะ SetTime(22,39) ตอนจบ — ตั้งไว้แบบนี้ลำดับเวลาจะไม่เพี้ยน)
            if (TimeManager.instance != null)
            {
                TimeManager.instance.SetTime(jumpHour, jumpMinute);
                TimeManager.instance.IsPauseTime(true);
            }

            var pc = PlayerController.Instance;
            if (pc == null)
            {
                Debug.LogError("[Rule4DevSkip] ไม่เจอ PlayerController ใน scene", this);
                return;
            }

            if (!pc.IsSitting())
            {
                // RuleFlow มี WaitUntil(PlayerIsSitting) อยู่แล้ว — สั่งไปเลย เดี๋ยวมันรอเอง
                Debug.LogWarning("[Rule4DevSkip] ตัวละครยังไม่ได้นั่ง — Rule 4 จะรอจนกว่าจะนั่ง " +
                                 "(เดินไปที่ศาลาแล้วกด E)", this);
            }

            rule4.StartRule();
            Debug.Log("[Rule4DevSkip] เริ่ม Rule 4 แล้ว", this);
        }

        // ─────────────────────────── Doll Debug ───────────────────────────

        private void LogDollPositions()
        {
            if (rule4 == null || rule4.ActiveDolls == null) return;

            foreach (var d in rule4.ActiveDolls)
            {
                if (d == null || !d.gameObject.activeSelf) continue;
                Debug.Log($"[Rule4DevSkip] ตุ๊กตา ({d.Sound}) อยู่ที่ {d.transform.position}", d);
            }
        }

        /// <summary>
        /// วาดเส้นจากผู้เล่นไปยังตุ๊กตาที่ยังเหลือ + ไปยังผี
        /// จำเป็นตอนที่ FMOD ยังไม่มี event เสียงตุ๊กตา เพราะตุ๊กตาจะเงียบสนิทจนหาไม่เจอ
        /// </summary>
        private void DrawDollLines()
        {
            var pc = PlayerController.Instance;
            if (pc == null || rule4 == null) return;

            Vector3 from = pc.transform.position + Vector3.up;

            if (rule4.ActiveDolls != null)
            {
                foreach (var d in rule4.ActiveDolls)
                {
                    if (d == null || !d.gameObject.activeSelf) continue;
                    Debug.DrawLine(from, d.transform.position, ColorForSound(d.Sound));
                }
            }

            var ghost = rule4.ActiveGhost;
            if (ghost != null)
                Debug.DrawLine(from, ghost.transform.position, Color.magenta);
        }

        private static Color ColorForSound(DollSound sound) => sound switch
        {
            DollSound.Cry   => Color.cyan,
            DollSound.Laugh => Color.yellow,
            DollSound.Hum   => Color.green,
            DollSound.Call  => new Color(1f, 0.5f, 0f), // ส้ม
            _               => Color.white              // Cough
        };
    }
}
