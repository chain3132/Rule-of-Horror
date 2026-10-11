using Enum;
using UnityEngine;

/// <summary>
/// ฐานของ "ผลจริง" ของรางวัลแต่ละอย่าง — RewardSystem แค่จดว่าใครได้อะไร ไม่ได้ทำผลเอง
///
/// ตัวนี้คอยดูสองอย่าง แล้วสั่ง Apply ใหม่ทุกครั้งที่อันใดอันหนึ่งขยับ:
///   • ผู้เล่นได้รางวัลนี้ไปแล้วหรือยัง (RewardSystem.OnRewardGranted)
///   • ตอนนี้โหมดไหน Relax หรือ Tension (GameModeController.OnModeChanged)
///
/// Apply ต้องเขียนแบบ "ตั้งค่าใหม่ทั้งหมดจากสองค่าที่ได้รับ" ไม่ใช่ "สลับจากของที่เป็นอยู่"
/// เพราะมันถูกเรียกซ้ำได้เรื่อยๆ — ตอนเปิดวัตถุ ตอนสลับโหมด ตอนได้รางวัล
/// </summary>
public abstract class RewardEffect : MonoBehaviour
{
    [Tooltip("ตัวนี้เป็นผลของรางวัลอะไร")]
    [SerializeField] protected RewardKind kind = RewardKind.None;

    private RewardSystem _reward;

    /// <summary>ผู้เล่นได้รางวัลนี้ไปแล้วไหม — อัปเดตทุกครั้งที่ Refresh</summary>
    protected bool IsOwned { get; private set; }

    /// <summary>โหมดที่มีผลอยู่ตอนนี้ — ยังไม่มี GameModeController ก็ถือว่า Relax</summary>
    protected static GameMode CurrentMode => GameModeController.instance != null
        ? GameModeController.instance.CurrentMode
        : GameMode.Relax;

    protected virtual void OnEnable()
    {
        GameModeController.OnModeChanged += HandleModeChanged;
        Bind();
        Refresh();
    }

    protected virtual void OnDisable()
    {
        GameModeController.OnModeChanged -= HandleModeChanged;

        if (_reward != null) _reward.OnRewardGranted -= HandleRewardGranted;
        _reward = null;
    }

    /// <summary>
    /// ตั้งค่าอีกรอบตอน Start
    ///
    /// OnEnable อาจวิ่งก่อน Awake ของ GameModeController ได้ (Unity สลับ Awake/OnEnable
    /// ของแต่ละออบเจกต์ ไม่ใช่ Awake ให้ครบทุกตัวก่อน) ตอนนั้น CurrentMode จะตอบ Relax เสมอ
    /// ซึ่งผิดถ้าซีนเริ่มขึ้นมาตอนที่โหมดเป็น Tension อยู่แล้ว
    /// </summary>
    protected virtual void Start() => Refresh();

    private void HandleModeChanged(GameMode mode) => Refresh();

    private void HandleRewardGranted(RewardKind granted)
    {
        if (granted == kind) Refresh();
    }

    /// <summary>อ่านสถานะล่าสุดแล้วสั่ง Apply — เรียกเองได้ถ้ามีอย่างอื่นไปแตะของในฉาก</summary>
    public void Refresh()
    {
        Bind();

        IsOwned = _reward != null && _reward.HasReward(kind);
        Apply(IsOwned, CurrentMode);
    }

    /// <summary>ตั้งสภาพของในฉากให้ตรงกับสถานะนี้ (ถูกเรียกซ้ำได้ ต้องให้ผลเหมือนกันทุกครั้ง)</summary>
    protected abstract void Apply(bool owned, GameMode mode);

    // หา RewardSystem แบบเลื่อนไปเรื่อยๆ จนเจอ — ลำดับ Awake ระหว่าง component ไม่การันตี
    // ถ้าดักแค่ใน OnEnable แล้วตอนนั้นยังไม่มีตัวในซีน จะไม่ได้ subscribe เลยตลอดอายุออบเจกต์
    private void Bind()
    {
        if (_reward != null) return;

        _reward = RewardSystem.instance;
        if (_reward != null) _reward.OnRewardGranted += HandleRewardGranted;
    }
}
