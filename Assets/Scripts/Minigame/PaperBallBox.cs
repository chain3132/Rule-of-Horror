using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ลังกระดาษที่มีหนังสือพิมพ์ขยำอยู่ข้างใน — ต้นทางของมินิเกมปาขยะ
///
/// มินิเกมนี้ไม่บังคับและไม่มีผลกับกฎใดๆ มีไว้ให้ผู้เล่นคลายเครียดตอนช่วง Relax เท่านั้น
/// พอเข้าโหมด Tension ทุกครั้ง ลังกับก้อนกระดาษที่เกลื่อนพื้นจะหายไปหมด แล้วกลับมาตอน Relax
///
/// ทำไมต้องหายตอน Tension (ไม่ใช่แค่เรื่องบรรยากาศ):
/// กฎหลายข้อยิง Physics.Raycast แบบไม่ใส่ LayerMask แล้วเทียบ hit.collider == เป้า
/// (LeverPuzzleController.IsLookingAtLever, PuzzleButton.IsLookingAt) ก้อนกระดาษที่ตกค้างอยู่
/// หน้าตู้ไฟจะบังเรย์เงียบๆ แล้วตู้ไฟจะซ่อมไม่ได้โดยไม่มี error อะไรให้เห็นเลย
/// การล้างก้อนทิ้งตอนเข้ากฎจึงเป็นการกันเคสนั้นไปด้วย ไม่ใช่แค่ความสวยงาม
///
/// ตัวนี้ถือ "บัญชี" ของมินิเกมทั้งหมด: เหลือในลังกี่ก้อน ก้อนไหนอยู่ในโลกตอนนี้
/// เพดานจำนวนก้อนในโลกคือ maxLiveBalls — เกินแล้วก้อนเก่าสุดจะถูกเก็บคืนลังเอง
/// (ไม่งั้นเล่นไปสิบนาทีพื้นบ้านจะเต็มไปด้วยกระดาษ แล้ว physics ก็หนักขึ้นเรื่อยๆ)
/// </summary>
public class PaperBallBox : MonoBehaviour
{
    /// <summary>ลังทุกใบที่อยู่ในซีนตอนนี้ — PaperBallThrower วนหาเป้าจากลิสต์นี้ ไม่ต้อง Find ทุกเฟรม</summary>
    public static readonly List<PaperBallBox> Active = new List<PaperBallBox>();

    // ─────────────────────────── Inspector ───────────────────────────

    [Header("ลูกกระดาษ")]
    [Tooltip("prefab ของก้อนกระดาษที่จะเกิดตอนผู้เล่นหยิบ")]
    [SerializeField] private PaperBall ballPrefab;

    [Tooltip("จุดที่ก้อนใหม่โผล่ (ปากลัง) — เว้นว่าง = ใช้ตำแหน่งของลังเอง")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("ก้อนกระดาษ \"ที่โชว์อยู่ในลัง\" (แค่ของตกแต่ง ไม่มีฟิสิกส์)\n" +
             "จำนวนในลิสต์ = จำนวนก้อนที่ลังมี หยิบไปก้อนนึงก็ปิดไปอันนึง")]
    [SerializeField] private List<GameObject> ballsInBox = new List<GameObject>();

    [Tooltip("ใช้ตอนไม่ได้ใส่ ballsInBox — ลังมีกี่ก้อน")]
    [SerializeField] private int startCount = 6;

    [Tooltip("✓ = หยิบได้ไม่จำกัด ลังไม่พร่อง ก้อนในลังโชว์เต็มตลอด (ค่าเริ่มต้น)\n" +
             "✗ = ลังมีเท่าที่นับได้ หยิบไปก้อนนึงก็พร่องไปอันนึง")]
    [SerializeField] private bool infinite = true;

    [Tooltip("ให้มีก้อนลอยอยู่ในโลกได้มากสุดกี่ก้อน เกินแล้วก้อนเก่าสุดจะถูกเก็บคืนลัง")]
    [SerializeField] private int maxLiveBalls = 6;

    [Header("การหยิบ")]
    [Tooltip("ยืนห่างจากลังได้ไม่เกินกี่เมตรถึงจะหยิบได้")]
    [SerializeField] private float reachDistance = 2.2f;

    [Tooltip("ต้องเล็งมาทางลังในมุมไม่เกินกี่องศาถึงจะหยิบได้\n" +
             "มีไว้กัน E ไปชนกับของอื่นที่อยู่ใกล้กัน (จุดนั่ง ตู้ไฟ) — ไม่ได้เล็งก็ไม่ติด")]
    [SerializeField] private float lookAngle = 40f;

    [Header("โหมดตึงเครียด")]
    [Tooltip("✓ = ลังและก้อนทั้งหมดหายไปตอนเข้า Tension แล้วกลับมาตอน Relax")]
    [SerializeField] private bool hideInTension = true;

    [Tooltip("ตัวที่จะถูกปิดตอนซ่อน — เว้นว่าง = ปิด Renderer/Collider ของลูกๆ ทั้งหมดแทน\n" +
             "(ห้ามใส่ตัวลังเอง ไม่งั้นสคริปต์จะถูกปิดไปด้วยแล้วมันจะไม่กลับมาตอน Relax)")]
    [SerializeField] private GameObject visualRoot;

    [Tooltip("อย่างอื่นที่อยากให้หายไปพร้อมลัง เช่นถังขยะ")]
    [SerializeField] private List<GameObject> alsoHide = new List<GameObject>();

    [Header("Prompt ปุ่ม")]
    [SerializeField] private string promptId     = "minigame.trash.take";
    [SerializeField] private string promptKey    = "E";
    [SerializeField] private string promptAction = "หยิบกระดาษขยำ";

    // ─────────────────────────── State ───────────────────────────

    private readonly List<PaperBall> _live = new List<PaperBall>();

    private int        _inBox;
    private bool       _hidden;
    private Renderer[] _renderers;
    private Collider[] _colliders;

    /// <summary>ลังมีได้มากสุดกี่ก้อน (เท่ากับจำนวนก้อนตกแต่งถ้าใส่ไว้)</summary>
    private int Capacity => ballsInBox != null && ballsInBox.Count > 0 ? ballsInBox.Count : Mathf.Max(0, startCount);

    /// <summary>หยิบจากลังนี้ได้ตอนนี้ไหม</summary>
    public bool IsAvailable => !_hidden && enabled && (infinite || _inBox > 0);

    /// <summary>เหลือในลังกี่ก้อน (infinite = -1)</summary>
    public int Remaining => infinite ? -1 : _inBox;

    /// <summary>ก้อนที่อยู่ในโลกตอนนี้ (รวมก้อนที่ถืออยู่) — thrower วนหาก้อนที่ตกพื้นจากลิสต์นี้</summary>
    public IReadOnlyList<PaperBall> LiveBalls => _live;

    /// <summary>จุดที่ prompt ปุ่มไปเกาะ และจุดที่วัดระยะ/มุมเล็ง</summary>
    public Transform AimPoint => spawnPoint != null ? spawnPoint : transform;

    public string PromptId     => promptId;
    public string PromptKey    => promptKey;
    public string PromptAction => promptAction;

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        _inBox     = Capacity;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _colliders = GetComponentsInChildren<Collider>(true);
    }

    private void OnEnable()
    {
        Active.Add(this);
        GameModeController.OnModeChanged += HandleModeChanged;

        // โหมดเปลี่ยนไปแล้วก่อนที่ลังจะ enable (เช่นโหลดซีนกลางกฎ) → ซิงก์ตามของจริงก่อนเลย
        var controller = GameModeController.instance;
        SetVisible(!hideInTension || controller == null || controller.CurrentMode == GameMode.Relax);
    }

    private void OnDisable()
    {
        Active.Remove(this);
        GameModeController.OnModeChanged -= HandleModeChanged;
    }

    // ─────────────────────────── Mode ───────────────────────────

    private void HandleModeChanged(GameMode mode)
    {
        if (!hideInTension) return;

        if (mode == GameMode.Tension)
        {
            // ยิงตอนตาปิดสนิท (ApplyMode อยู่กลาง BlinkRoutine) ผู้เล่นจึงไม่เห็นของหายไปหน้าตา
            ClearLiveBalls();
            SetVisible(false);
        }
        else
        {
            SetVisible(true);
        }
    }

    /// <summary>เก็บก้อนที่เกลื่อนพื้นทั้งหมด (รวมก้อนในมือ) แล้วเติมลังกลับให้เต็ม</summary>
    public void ClearLiveBalls()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            if (_live[i] != null) _live[i].Despawn();
        }

        _live.Clear();
        _inBox = Capacity;
        RefreshVisuals();
    }

    private void SetVisible(bool value)
    {
        _hidden = !value;

        if (visualRoot != null)
        {
            visualRoot.SetActive(value);
        }
        else
        {
            // ไม่ปิด GameObject ของตัวเอง — OnDisable จะถอด subscribe แล้วลังจะไม่กลับมาตอน Relax
            foreach (var renderer in _renderers)
                if (renderer != null) renderer.enabled = value;

            foreach (var collider in _colliders)
                if (collider != null) collider.enabled = value;
        }

        foreach (var extra in alsoHide)
            if (extra != null) extra.SetActive(value);

        RefreshVisuals();
    }

    // ─────────────────────────── หยิบ / คืน ───────────────────────────

    /// <summary>
    /// ผู้เล่นเล็งมาที่ลังนี้อยู่ไหม
    ///
    /// ตั้งใจไม่เช็กว่าลังยังมีก้อนเหลือไหม เพราะการ "เอาก้อนคืนลัง" ต้องเล็งลังเหมือนกัน
    /// และตอนถืออยู่ลังก็มักจะว่าง (ก้อนสุดท้ายอยู่ในมือ) — คนเรียกเช็ก IsAvailable เองถ้าจะหยิบ
    /// </summary>
    public bool IsAimedAt(Camera cam)
    {
        if (_hidden || !enabled || cam == null) return false;

        Vector3 toBox = AimPoint.position - cam.transform.position;
        if (toBox.sqrMagnitude > reachDistance * reachDistance) return false;

        // ตั้งใจไม่ใช้ Physics.Raycast เช็กว่ามีอะไรบัง — บทเรียนจากตู้ไฟ Rule 2 คือ raycast
        // แบบไม่ใส่ LayerMask จะไปโดน trigger/ของจุกจิกแล้วพังเงียบๆ หาสาเหตุไม่ได้
        // มินิเกมคลายเครียดไม่คุ้มกับความเสี่ยงนั้น ระยะ + มุมเล็งพอแล้ว
        return Vector3.Angle(cam.transform.forward, toBox) <= lookAngle;
    }

    /// <summary>หยิบก้อนใหม่จากลัง — คืน null ถ้าลังว่างหรือกำลังซ่อนอยู่</summary>
    public PaperBall TakeBall()
    {
        if (!IsAvailable || ballPrefab == null) return null;

        PruneDestroyed();

        // เต็มเพดานแล้ว → เก็บก้อนเก่าสุดที่ไม่ได้ถืออยู่คืนลังก่อน
        while (_live.Count >= Mathf.Max(1, maxLiveBalls))
        {
            if (!RecycleOldest()) return null;   // ทุกก้อนถูกถืออยู่ (ไม่ควรเกิด) — ไม่เกิดลูกใหม่
        }

        Transform at   = AimPoint;
        PaperBall ball = Instantiate(ballPrefab, at.position, Random.rotation);
        ball.Init(this);
        _live.Add(ball);

        if (!infinite) _inBox = Mathf.Max(0, _inBox - 1);
        RefreshVisuals();

        return ball;
    }

    /// <summary>เอาก้อนกลับเข้าลัง (ถังขยะกินลูกไป / ผู้เล่นเก็บคืน / เกินเพดาน)</summary>
    public void ReturnBall(PaperBall ball)
    {
        if (ball == null) return;

        _live.Remove(ball);
        ball.Despawn();

        if (!infinite) _inBox = Mathf.Min(Capacity, _inBox + 1);
        RefreshVisuals();
    }

    private bool RecycleOldest()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            if (_live[i] == null || _live[i].IsHeld) continue;

            ReturnBall(_live[i]);
            return true;
        }
        return false;
    }

    private void PruneDestroyed()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
            if (_live[i] == null) _live.RemoveAt(i);
    }

    private void RefreshVisuals()
    {
        if (ballsInBox == null) return;

        for (int i = 0; i < ballsInBox.Count; i++)
        {
            if (ballsInBox[i] == null) continue;
            ballsInBox[i].SetActive(!_hidden && (infinite || i < _inBox));
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(AimPoint.position, reachDistance);
    }
}
