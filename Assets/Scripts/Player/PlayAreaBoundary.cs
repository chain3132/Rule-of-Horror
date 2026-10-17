using System.HeartbeatSystem;
using Player;
using UnityEngine;

/// <summary>
/// ขอบเขตแผนที่ — เดินออกไปไม่ได้ ตัวละครจะหันหลังกลับเข้ามาเอง
///
/// ทำสองอย่างคู่กัน และต้องมีทั้งคู่:
///   • หนีบตำแหน่งไว้ในเขต — เป็นตัวการันตีว่า "ออกไม่ได้" จริง ไม่ว่าจะมุดด้วยวิธีไหน
///   • หมุนตัวกลับเข้ามา + ล็อกเมาส์ชั่วคราว — เป็นตัวบอกว่า "ตัวละครไม่ยอมไป" ไม่ใช่
///     "เกมมีกำแพงใส" ผู้เล่นที่ถูกหนีบเฉยๆ จะคิดว่าบั๊ก ผู้เล่นที่เห็นตัวเองหันกลับจะเข้าใจ
///
/// หันแล้วปล่อยมือเลย ไม่ได้ลากเดินกลับให้: พอหันหน้าเข้าในแล้ว ปุ่มเดินหน้าของผู้เล่นเอง
/// กลายเป็นทางกลับบ้านทันที การแย่งปุ่มเดินต่ออีกจะรู้สึกเหมือนเกมค้างมากกว่าเหมือนตัวละครกลัว
///
/// ไม่ทับกับกฎ 1: red zone ของ HeartbeatSystem คือ "ไปไกลกว่านี้แล้วตาย" ส่วนตัวนี้คือขอบโลก
/// ซึ่งอยู่ไกลออกไปอีก redZoneMargin เมตร ผู้เล่นจึงยังมีที่ให้เดินออกไปตายในกฎ 1 ได้ตามเดิม
///
/// Setup: แปะไว้ที่ object ไหนก็ได้ในซีน (แนะนำตัวเดียวกับ HeartbeatSystem) แล้วดู gizmo
/// สีฟ้าในซีนว่าเขตครอบที่ที่ต้องเดินไปถึงครบไหม
/// </summary>
public class PlayAreaBoundary : MonoBehaviour
{
    public enum AreaShape
    {
        /// <summary>ใช้ red zone ของ HeartbeatSystem เป็นฐาน แล้วเผื่อออกไปอีก — ไม่ต้องตั้งค่าอะไรเลย</summary>
        RedZonePlusMargin,
        Box,
        Circle,
    }

    // ─────────────────────────── Inspector ───────────────────────────

    [Header("รูปร่างของเขต")]
    [SerializeField] private AreaShape shape = AreaShape.RedZonePlusMargin;

    [Tooltip("เผื่อออกไปจากขอบ red zone กี่เมตร — ใช้เฉพาะโหมด Red Zone Plus Margin\n" +
             "ต้องมากกว่า 0 ไม่งั้นขอบจะทับ red zone พอดี แล้วกฎ 1 จะไม่มีที่ให้เดินออกไปตาย")]
    [SerializeField] private float redZoneMargin = 10f;

    [Tooltip("จุดกลางเขต (โหมด Box / Circle) — เว้นว่างจะใช้ตำแหน่งของ object นี้")]
    [SerializeField] private Transform areaCenter;

    [Tooltip("กว้าง x ลึก ของเขตสี่เหลี่ยม (เมตร)")]
    [SerializeField] private Vector2 boxSize = new Vector2(140f, 140f);

    [Tooltip("รัศมีของเขตวงกลม (เมตร)")]
    [SerializeField] private float radius = 70f;

    [Header("การหันกลับ")]
    [Tooltip("หมุนตัวกลับใช้เวลากี่วินาที — 0 = หันทันที (กระตุก)")]
    [SerializeField] private float turnDuration = 0.45f;

    [Tooltip("หันเสร็จแล้วดันเข้ามาในเขตอีกกี่เมตร — กันยืนคาเส้นแล้วทริกเกอร์ซ้ำทุกเฟรม")]
    [SerializeField] private float pushInside = 1.5f;

    [Header("บอกผู้เล่น")]
    [Tooltip("เสียงในหัวตอนถูกหันกลับ — เว้นว่าง = ไม่พูด")]
    [SerializeField] private string line = "ไปไกลกว่านี้ไม่ได้";

    [SerializeField] private float lineSeconds = 2f;

    [Tooltip("หน่วงก่อนจะพูดซ้ำได้อีกครั้ง (วินาที) — กันบ่นรัวตอนผู้เล่นดันเส้นเล่น")]
    [SerializeField] private float lineCooldown = 6f;

    // ─────────────────────────── Runtime ───────────────────────────

    /// <summary>ปิดได้ชั่วคราวถ้าฉากไหนต้องพาผู้เล่นออกนอกเขตจริงๆ</summary>
    public bool Enforcing { get; set; } = true;

    private bool       _turning;
    private float      _turnTimer;
    private Quaternion _turnFrom;
    private Quaternion _turnTo;
    private bool       _lookWasReleasedByUs;
    private float      _nextLineTime;

    // ─────────────────────────── Loop ───────────────────────────

    // LateUpdate เพราะต้องตัดสินหลังจากที่ผู้เล่นเดินของเฟรมนี้ไปแล้ว
    // ถ้าเช็คใน Update จะช้าไปหนึ่งเฟรมเสมอ และจะเห็นตัวเองโผล่ออกนอกเขตแวบนึงทุกครั้ง
    private void LateUpdate()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;

        if (_turning)
        {
            AdvanceTurn(pc);
            return;
        }

        if (!Enforcing) return;

        // นั่งอยู่ออกนอกเขตไม่ได้ และตอนที่คนอื่นยึดการควบคุมไว้ (โทรศัพท์ / ตู้ไฟ / ฉากตาย)
        // ก็ไม่ใช่การเดินของผู้เล่น — เข้าไปแตะ SetLook ตอนนั้นจะเป็นการปลดล็อกแทนเจ้าของจริง
        if (pc.IsSitting() || !pc.CanMove || !pc.CanLook) return;

        Vector3 pos    = pc.transform.position;
        Vector3 inside = ClosestPointInside(pos);
        if (inside == pos) return;

        BeginTurn(pc, pos, inside);
    }

    // ─────────────────────────── Turn ───────────────────────────

    private void BeginTurn(PlayerController pc, Vector3 pos, Vector3 inside)
    {
        Vector3 back = inside - pos;
        back.y = 0f;

        // หนีบก่อนเลย ไม่รอให้หมุนเสร็จ — ระหว่างหมุน (0.45 วิ) ถ้าไม่หนีบ ผู้เล่นที่กดเดินหน้า
        // ค้างอยู่จะไหลออกไปต่อได้อีกหลายเมตร แล้วพอหมุนเสร็จก็ยังอยู่นอกเขตเหมือนเดิม
        Vector3 push = back.sqrMagnitude > 0.0001f
            ? back + back.normalized * Mathf.Max(0f, pushInside)
            : Vector3.zero;

        pc.NudgeBy(push);

        if (back.sqrMagnitude < 0.0001f) return;   // อยู่ตรงขอบเป๊ะ ไม่รู้จะหันไปทางไหน

        _turnFrom = pc.transform.rotation;
        _turnTo   = Quaternion.LookRotation(back.normalized, Vector3.up);

        // ล็อกเมาส์ไว้ก่อน ไม่งั้น Look() จะหมุนตัวสู้กับการหมุนของเราในเฟรมเดียวกัน
        pc.SetLook(false);
        _lookWasReleasedByUs = true;

        _turning   = true;
        _turnTimer = 0f;

        if (!string.IsNullOrWhiteSpace(line) &&
            Time.time >= _nextLineTime &&
            PlayerDialogueUI.instance != null)
        {
            PlayerDialogueUI.instance.ShowLine(line, lineSeconds);
            _nextLineTime = Time.time + lineCooldown;
        }

        if (turnDuration <= 0f) AdvanceTurn(pc);
    }

    private void AdvanceTurn(PlayerController pc)
    {
        _turnTimer += Time.deltaTime;

        float k = turnDuration <= 0f ? 1f : Mathf.Clamp01(_turnTimer / turnDuration);

        // yaw ของผู้เล่นอยู่บน transform.rotation ตรงๆ ไม่มีตัวแปรจำค่าไว้ข้างใน
        // (Look() ใช้ transform.Rotate ซึ่งเป็นการหมุนแบบสัมพัทธ์) เขียนทับได้เลยไม่ต้อง sync
        pc.transform.rotation = Quaternion.Slerp(_turnFrom, _turnTo, Mathf.SmoothStep(0f, 1f, k));

        if (k < 1f) return;

        _turning = false;

        if (_lookWasReleasedByUs)
        {
            pc.SetLook(true);
            _lookWasReleasedByUs = false;
        }
    }

    /// <summary>
    /// ตัวละครถูกยึดการควบคุมไปกลางการหมุน (ตาย / เปิดโทรศัพท์ / ถูกปิด component)
    /// — ต้องคืน look ไม่งั้นผู้เล่นจะมองไม่ได้ตลอดกาล
    /// </summary>
    private void OnDisable()
    {
        _turning = false;
        if (!_lookWasReleasedByUs) return;

        _lookWasReleasedByUs = false;
        if (PlayerController.Instance != null) PlayerController.Instance.SetLook(true);
    }

    // ─────────────────────────── Shape ───────────────────────────

    /// <summary>จุดในเขตที่ใกล้ตำแหน่งนี้ที่สุด — คืนค่าเดิมถ้าอยู่ในเขตแล้ว (เทียบแค่ XZ)</summary>
    private Vector3 ClosestPointInside(Vector3 pos)
    {
        switch (shape)
        {
            case AreaShape.Circle:
            {
                Vector3 c = Center;
                Vector2 d = new Vector2(pos.x - c.x, pos.z - c.z);
                float   r = Mathf.Max(0.1f, radius);
                if (d.sqrMagnitude <= r * r) return pos;

                d = d.normalized * r;
                return new Vector3(c.x + d.x, pos.y, c.z + d.y);
            }

            case AreaShape.Box:
            {
                Vector3 c = Center;
                return ClampToRect(pos,
                    c.x - boxSize.x * 0.5f, c.x + boxSize.x * 0.5f,
                    c.z - boxSize.y * 0.5f, c.z + boxSize.y * 0.5f);
            }

            default:
            {
                var heart = HeartbeatSystem.instance;
                if (heart == null) return pos;   // ไม่มีโซนให้อ้าง = ไม่บังคับอะไร

                float m = Mathf.Max(0f, redZoneMargin);
                return ClampToRect(pos,
                    heart.redMinX - m, heart.redMaxX + m,
                    heart.redMinZ - m, heart.redMaxZ + m);
            }
        }
    }

    private static Vector3 ClampToRect(Vector3 pos, float minX, float maxX, float minZ, float maxZ)
    {
        float x = Mathf.Clamp(pos.x, minX, maxX);
        float z = Mathf.Clamp(pos.z, minZ, maxZ);

        return Mathf.Approximately(x, pos.x) && Mathf.Approximately(z, pos.z)
             ? pos
             : new Vector3(x, pos.y, z);
    }

    private Vector3 Center => areaCenter != null ? areaCenter.position : transform.position;

    // ─────────────────────────── Gizmo ───────────────────────────

    // เขตนี้ไม่มีอะไรมองเห็นได้ในเกมเลย ถ้าไม่วาดให้ดูในซีนก็ไม่มีทางรู้ว่ามันครอบถึงไหน
    // แล้วจะไปเจอตอนเล่นว่ามันบังทางที่กฎ 4/5 ต้องเดินไป
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.25f, 0.7f, 1f, 0.9f);

        switch (shape)
        {
            case AreaShape.Circle:
                DrawCircle(Center, Mathf.Max(0.1f, radius));
                break;

            case AreaShape.Box:
            {
                Vector3 c = Center;
                DrawRect(c.x - boxSize.x * 0.5f, c.x + boxSize.x * 0.5f,
                         c.z - boxSize.y * 0.5f, c.z + boxSize.y * 0.5f, c.y);
                break;
            }

            default:
            {
                var heart = HeartbeatSystem.instance != null
                    ? HeartbeatSystem.instance
                    : FindAnyObjectByType<HeartbeatSystem>();
                if (heart == null) return;

                float m = Mathf.Max(0f, redZoneMargin);
                DrawRect(heart.redMinX - m, heart.redMaxX + m,
                         heart.redMinZ - m, heart.redMaxZ + m, transform.position.y);
                break;
            }
        }
    }

    private static void DrawRect(float minX, float maxX, float minZ, float maxZ, float y)
    {
        Vector3 a = new Vector3(minX, y, minZ);
        Vector3 b = new Vector3(maxX, y, minZ);
        Vector3 c = new Vector3(maxX, y, maxZ);
        Vector3 d = new Vector3(minX, y, maxZ);

        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }

    private static void DrawCircle(Vector3 center, float r)
    {
        const int Steps = 48;
        Vector3 prev = center + new Vector3(r, 0f, 0f);

        for (int i = 1; i <= Steps; i++)
        {
            float a = i / (float)Steps * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
