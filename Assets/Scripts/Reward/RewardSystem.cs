using System;
using System.Collections.Generic;
using Enum;
using Manager;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// รางวัลที่คนปริศนาให้เลือกหลังจบกฎแต่ละข้อ
///
/// กติกา:
///   • จบกฎแล้วได้เลือก 1 อย่างจาก 2 อย่างที่สุ่มมา (ตั้งจำนวนได้ที่ Options Per Offer)
///   • รางวัลทุกอย่างได้ครั้งเดียว รวมทั้งเทียน — ที่เลือกไปแล้วจะไม่กลับมาอยู่ในกองสุ่มอีก
///   • อันที่ขึ้นมาแต่ไม่ได้เลือก ยังอยู่ในกองสุ่มเต็มๆ มีสิทธิ์ออกมาในรอบถัดๆ ไป
///   • เทียนจุดทีเดียวทั้งบ้าน (ไม่ใช่ทีละเล่ม) และไม่จุดเองอีกแล้ว — มีทางเดียวคือผู้เล่นเลือกรางวัลนี้
///   • บางอย่างต้องรอถึงรอบที่กำหนดก่อนจะมีสิทธิ์ออก (วิทยุ = หลังผ่านกฎ 2 ขึ้นไป) ดู Available From Round
///
/// วิธีผูกกับแชท: ใน ConversationData ทำ node ของผู้เล่นแล้วติ๊ก Offers Reward
/// ตัวเลือกจะถูกสุ่มมาให้ตอนเล่น ทุกตัวเลือกไปต่อที่ Next Node เดียวกัน
/// ข้อความของ node ปลายทางใส่ {reward} ไว้ได้ จะถูกแทนด้วยชื่อรางวัลที่เพิ่งเลือก
///
/// ตัวนี้ไม่ได้ทำ "ผล" ของรางวัลเอง (แสง/เสียง/ควัน ต่างกันระหว่าง Relax กับ Tension)
/// มันแค่บอกว่าใครได้อะไรไปแล้ว แล้วเปิดของในฉากให้ตาม Objects To Enable หรือยิง On Granted
/// ระบบที่ทำผลจริงไปอ่าน HasReward() / ฟัง OnRewardGranted เอา — ดู RewardEffect
/// (เสาไฟ = RewardStreetLamp, หิ่งห้อย = RewardModeObjects)
/// </summary>
public class RewardSystem : MonoBehaviour
{
    // ─────────────────────────── Inner Types ───────────────────────────

    [Serializable]
    public class RewardEntry
    {
        public RewardKind kind = RewardKind.None;

        [Tooltip("ชื่อที่ผู้เล่นเห็น เช่น เทียน — ใช้แทน {reward} ในข้อความด้วย")]
        public string displayName = "";

        [Tooltip("ข้อความบนปุ่มตัวเลือก — เว้นว่าง = ใช้ชื่อรางวัล")]
        [TextArea(1, 2)]
        public string replyText = "";

        // รอบ = ครั้งที่เท่าไหร่ของการเลือกรางวัล (รอบ 1 = หลังจบกฎ 1, รอบ 2 = หลังจบกฎ 2 ...)
        //
        // นับจากจำนวนครั้งที่ผู้เล่นเลือกรางวัลไปแล้ว ไม่ได้ไปถามแอปกฎว่ารู้กฎไปกี่ข้อ
        // เพราะกฎถูกบันทึกตอนคนปริศนา "บอก" ไม่ใช่ตอนผู้เล่น "ผ่าน" — ถ้าไปนับจากตรงนั้น
        // วิทยุจะโผล่เร็วไปหนึ่งข้อทันทีที่กฎถัดไปถูกส่งมาก่อนจังหวะให้รางวัล

        [Tooltip("ออกได้ตั้งแต่การเลือกรางวัลครั้งที่เท่าไหร่\n" +
                 "0 หรือ 1 = ออกได้ตั้งแต่ครั้งแรก (หลังจบกฎ 1)\n" +
                 "2 = ต้องผ่านกฎ 2 ขึ้นไปก่อน (วิทยุ)")]
        public int availableFromRound;

        [Tooltip("✗ = ตัดออกจากกองสุ่มไปเลย (เช่นเส้นด้ายสีแดงที่ยังไม่ได้ทำ)")]
        public bool available = true;

        [Tooltip("ของในฉากที่จะถูกเปิดตอนได้รางวัลนี้ เช่นเสาไฟ วิทยุ หิ่งห้อย ธูป")]
        public List<GameObject> objectsToEnable = new List<GameObject>();

        [Tooltip("อย่างอื่นที่ต้องทำตอนได้รางวัลนี้ — ต่อเองใน Inspector ได้")]
        public UnityEvent onGranted;

        public string Label      => string.IsNullOrEmpty(displayName) ? kind.ToString() : displayName;
        public string ReplyLabel => string.IsNullOrEmpty(replyText)   ? Label           : replyText;
    }

    // ─────────────────────────── Inspector ───────────────────────────

    [Header("ต่อกับแชท")]
    [Tooltip("ตัวเดินบทสนทนา — เว้นว่าง = หาในซีนให้เอง")]
    [SerializeField] private ConversationRunner runner;

    [Tooltip("สุ่มมากี่ตัวเลือกต่อหนึ่งครั้ง")]
    [SerializeField] private int optionsPerOffer = 2;

    [Tooltip("คำที่จะถูกแทนด้วยชื่อรางวัลที่เพิ่งเลือก ในข้อความของ node ถัดไป")]
    [SerializeField] private string rewardPlaceholder = "{reward}";

    [Header("รางวัลทั้งหมด")]
    [SerializeField] private List<RewardEntry> rewards = new List<RewardEntry>();

    [Header("เครื่องมือทดสอบ — ต้องว่างตอนเล่นจริง")]
    [Tooltip("ล็อกว่าตัวเลือกที่โผล่มาต้องมีอะไรบ้าง — ว่าง = สุ่มตามปกติ\n" +
             "ใส่น้อยกว่าจำนวนตัวเลือกได้ ที่เหลือจะสุ่มเติมให้\n" +
             "ของที่ล็อกไว้ข้ามเงื่อนไขทุกข้อ (รอบ/ได้ไปแล้ว/เทียนหมด)\n" +
             "กดจากหน้าต่าง Rule of Horror/Reward/Reward Test Window จะง่ายกว่า")]
    [SerializeField] private List<RewardKind> forcedOffer = new List<RewardKind>();

    // ─────────────────────────── Static access ───────────────────────────

    private static RewardSystem _instance;

    /// <summary>ตัวในซีน — หาให้เองถ้ายังไม่มีใครเซ็ต (ลำดับ Awake ระหว่าง component ไม่การันตี)</summary>
    public static RewardSystem instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<RewardSystem>();
            return _instance;
        }
    }

    // ─────────────────────────── Events / State ───────────────────────────

    /// <summary>ผู้เล่นเพิ่งได้รางวัลนี้ — ระบบที่ทำผลจริงของรางวัลฟังตัวนี้</summary>
    public event Action<RewardKind> OnRewardGranted;

    /// <summary>รางวัลล่าสุดที่เพิ่งได้ — ใช้แทน {reward} ในข้อความ</summary>
    public RewardKind LastGranted { get; private set; } = RewardKind.None;

    private string _lastGrantedLabel = "";

    private readonly HashSet<RewardKind> _claimed = new HashSet<RewardKind>();

    private int _roundsDone;

    /// <summary>ตอนนี้เป็นการเลือกรางวัลครั้งที่เท่าไหร่ (เริ่มที่ 1 = หลังจบกฎ 1)</summary>
    public int Round => _roundsDone + 1;

    // ตัวเลือกที่สุ่มไว้แล้วของแต่ละ node — กันผู้เล่นปิดแชทแล้วเปิดใหม่เพื่อสุ่มใหม่
    // (ใช้ตัว ChatNode เป็นคีย์ได้เลย เพราะมันเป็นออบเจกต์เดิมที่อยู่ใน ConversationData)
    private readonly Dictionary<ChatNode, List<RewardEntry>> _offers
        = new Dictionary<ChatNode, List<RewardEntry>>();

    // ─────────────────────────── Lifecycle ───────────────────────────

    private void Awake()
    {
        if (_instance == null) _instance = this;
        if (runner == null) runner = FindAnyObjectByType<ConversationRunner>();
    }

    private void OnEnable()
    {
        if (runner == null) return;

        runner.ReplyOverride  = BuildOffer;
        runner.OnReplySelected += HandleReplySelected;
    }

    private void OnDisable()
    {
        if (runner == null) return;

        if (runner.ReplyOverride == BuildOffer) runner.ReplyOverride = null;
        runner.OnReplySelected -= HandleReplySelected;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// เติมรายการรางวัลให้ตอนเพิ่ง Add Component (Unity เรียก Reset ให้ครั้งเดียว)
    ///
    /// มีไว้เพื่อไม่ต้องพิมพ์ชื่อไทยทีละช่องเอง — ของที่ต้องทำต่อคือลากของในฉาก
    /// (เสาไฟ/วิทยุ/หิ่งห้อย/ธูป) ไปใส่ Objects To Enable ของแต่ละอัน
    /// เส้นด้ายสีแดงไม่ได้ใส่ไว้ตามที่สั่ง — เพิ่มเองทีหลังได้ ค่าใน enum มีอยู่แล้ว
    /// </summary>
    private void Reset()
    {
        runner  = FindAnyObjectByType<ConversationRunner>();
        rewards = new List<RewardEntry>
        {
            new RewardEntry { kind = RewardKind.Candle,     displayName = "เทียน"   },
            new RewardEntry { kind = RewardKind.StreetLamp, displayName = "เสาไฟ"   },
            new RewardEntry { kind = RewardKind.Radio,      displayName = "วิทยุ",   availableFromRound = 2 },
            new RewardEntry { kind = RewardKind.Firefly,    displayName = "หิ่งห้อย" },
            new RewardEntry { kind = RewardKind.Incense,    displayName = "ธูป"      },
        };
    }

    // ─────────────────────────── ถาม/ตอบกับแชท ───────────────────────────

    /// <summary>
    /// สร้างตัวเลือกของ node ที่ติดธง Offers Reward (null = ไม่ใช่ node แบบนั้น ใช้ของในแอสเซตไป)
    ///
    /// คืนลิสต์ว่างได้ แปลว่าไม่เหลืออะไรให้สุ่มแล้ว — ตัวเดินบทสนทนาจะข้ามไป Next Node ให้เอง
    /// </summary>
    private List<ReplyOption> BuildOffer(ChatNode node)
    {
        if (node == null || !node.offersReward) return null;

        if (!_offers.TryGetValue(node, out var picks))
        {
            picks = Draw(optionsPerOffer);
            _offers[node] = picks;
        }

        var options = new List<ReplyOption>(picks.Count);
        foreach (var entry in picks)
            options.Add(new ReplyOption { replyText = entry.ReplyLabel, nextNode = node.nextNode });

        return options;
    }

    private void HandleReplySelected(ChatNode node, int index, ReplyOption reply)
    {
        if (node == null || !node.offersReward) return;
        if (!_offers.TryGetValue(node, out var picks)) return;

        // ใช้ไปแล้วทิ้งเลย — ถ้ามีทางกลับมา node นี้อีกจะได้สุ่มใหม่ ไม่ใช่เสนอของที่ได้ไปแล้ว
        _offers.Remove(node);

        if (index < 0 || index >= picks.Count) return;

        // นับรอบตรงนี้ ไม่ใช่ตอน Grant สำเร็จ — รอบคือ "ผู้เล่นเลือกไปกี่ครั้ง" ไม่ใช่ "ได้ของไปกี่ชิ้น"
        // กดอันที่ไม่มีผลอะไรก็ยังถือว่าผ่านกฎข้อนั้นมาแล้ว รอบถัดไปวิทยุต้องมีสิทธิ์ออก
        _roundsDone++;

        Grant(picks[index]);
    }

    // ─────────────────────────── สุ่ม ───────────────────────────

    /// <summary>
    /// สุ่มรางวัลที่ยังเลือกได้มา count อย่าง (ไม่ซ้ำกันในรอบเดียว)
    ///
    /// ของที่ล็อกไว้ใน Forced Offer ได้ที่ก่อน ที่เหลือค่อยสุ่มเติมให้ครบ
    /// </summary>
    public List<RewardEntry> Draw(int count)
    {
        int take  = Mathf.Max(0, count);
        var picks = TakeForced(take);

        var pool = new List<RewardEntry>();
        foreach (var entry in rewards)
            if (IsDrawable(entry) && !picks.Contains(entry)) pool.Add(entry);

        int fill = Mathf.Min(take - picks.Count, pool.Count);

        // Fisher–Yates เฉพาะส่วนหน้าที่จะหยิบ ไม่ต้องสับทั้งกอง
        for (int i = 0; i < fill; i++)
        {
            int swap = UnityEngine.Random.Range(i, pool.Count);
            (pool[i], pool[swap]) = (pool[swap], pool[i]);
            picks.Add(pool[i]);
        }

        return picks;
    }

    /// <summary>
    /// หยิบของที่ถูกล็อกไว้มาก่อน — ว่างเสมอถ้าไม่ได้ล็อกอะไร
    ///
    /// ตั้งใจไม่ผ่าน IsDrawable เพราะจุดประสงค์คือเทสของที่ปกติยังไม่มีสิทธิ์ออก
    /// (วิทยุตั้งแต่รอบแรก หรือของที่เพิ่งได้ไปแล้วอยากดูซ้ำ)
    /// </summary>
    private List<RewardEntry> TakeForced(int count)
    {
        var picks = new List<RewardEntry>();
        if (forcedOffer == null || forcedOffer.Count == 0) return picks;

        foreach (var kind in forcedOffer)
        {
            if (picks.Count >= count) break;
            if (kind == RewardKind.None) continue;

            var entry = Find(kind);
            if (entry == null)
            {
                Debug.LogWarning($"[Reward] ล็อก {kind} ไว้ แต่ไม่มีรายการของมันในลิสต์ Rewards", this);
                continue;
            }

            if (!picks.Contains(entry)) picks.Add(entry);
        }

        if (picks.Count > 0)
            Debug.LogWarning($"[Reward] กำลังล็อกตัวเลือกอยู่ — {string.Join(" / ", picks.ConvertAll(e => e.Label))} " +
                             "ล้าง Forced Offer ก่อนเทสของจริง", this);

        return picks;
    }

    private RewardEntry Find(RewardKind kind)
    {
        foreach (var entry in rewards)
            if (entry != null && entry.kind == kind) return entry;

        return null;
    }

    /// <summary>
    /// รางวัลนี้ยังมีสิทธิ์ถูกสุ่มออกมาไหม
    ///
    /// ตัดเฉพาะอันที่ "ได้ไปแล้ว" ไม่ได้ตัดอันที่ "เคยขึ้นมาแต่ไม่ได้เลือก" — อันที่ไม่ได้เลือก
    /// ยังอยู่ในกองสุ่มเต็มๆ จะได้ไม่หายไปจากเกมทั้งที่ผู้เล่นไม่เคยได้มันเลย
    /// </summary>
    private bool IsDrawable(RewardEntry entry)
    {
        if (entry == null || !entry.available || entry.kind == RewardKind.None) return false;
        if (_claimed.Contains(entry.kind))                                      return false;
        if (Round < entry.availableFromRound)                                   return false;

        // เทียนจุดครบทุกเล่มแล้วก็ไม่ต้องเสนอ เลือกไปก็ไม่มีอะไรเกิดขึ้น
        // (ปกติไม่เกิด เพราะเลือกทีเดียวจุดหมดแล้วถูกตัดด้วย _claimed อยู่ดี
        //  แต่กันกรณีมีใครไปจุดเทียนล่วงหน้าจากที่อื่น)
        if (entry.kind == RewardKind.Candle && GameModeController.instance != null)
            return GameModeController.instance.UnlitCandleCount > 0;

        return true;
    }

    // ─────────────────────────── ให้รางวัล ───────────────────────────

    /// <summary>ให้รางวัลตรงๆ โดยไม่ผ่านการสุ่ม (dev tool / เงื่อนไขพิเศษ)</summary>
    public bool Grant(RewardKind kind)
    {
        var entry = Find(kind);
        if (entry != null) return Grant(entry);

        Debug.LogWarning($"[Reward] ไม่มีรายการของรางวัล {kind} ใน RewardSystem", this);
        return false;
    }

    private bool Grant(RewardEntry entry)
    {
        if (entry == null) return false;

        if (entry.kind == RewardKind.Candle && !LightCandles()) return false;

        _claimed.Add(entry.kind);

        foreach (var obj in entry.objectsToEnable)
            if (obj != null) obj.SetActive(true);

        entry.onGranted?.Invoke();

        LastGranted       = entry.kind;
        _lastGrantedLabel = entry.Label;

        Debug.Log($"[Reward] ผู้เล่นเลือก {entry.Label} (รอบที่ {_roundsDone})", this);

        OnRewardGranted?.Invoke(entry.kind);
        return true;
    }

    /// <summary>จุดเทียนทั้งบ้านทีเดียว — false = ไม่มีเทียนให้จุด รางวัลนี้จึงไม่ถือว่าได้ไป</summary>
    private bool LightCandles()
    {
        var mode = GameModeController.instance;
        if (mode == null)
        {
            Debug.LogWarning("[Reward] ไม่เจอ GameModeController — จุดเทียนให้ไม่ได้", this);
            return false;
        }

        int lit = mode.LightUnlitCandles();
        if (lit > 0)
        {
            Debug.Log($"[Reward] จุดเทียนให้ {lit} เล่ม", this);
            return true;
        }

        Debug.LogWarning("[Reward] เทียนจุดครบทุกเล่มแล้ว ไม่มีอะไรให้จุดเพิ่ม", this);
        return false;
    }

    // ─────────────────────────── ถามสถานะ ───────────────────────────

    /// <summary>ผู้เล่นเคยได้รางวัลนี้ไปแล้วไหม — ระบบที่ทำผลจริงของรางวัลใช้ตัวนี้ถาม</summary>
    public bool HasReward(RewardKind kind) => _claimed.Contains(kind);

    /// <summary>รางวัลที่ได้ไปแล้วทั้งหมด</summary>
    public IReadOnlyCollection<RewardKind> Claimed => _claimed;

    // ─────────────────────────── เครื่องมือทดสอบ ───────────────────────────
    // ทั้งหมดในส่วนนี้มีไว้ให้หน้าต่าง Rule of Horror/Reward/Reward Test Window เรียก
    // เกมจริงไม่ได้ใช้ — แต่ไม่ได้ตัดออกจากบิลด์ เพราะ Draw ต้องอ่าน forcedOffer อยู่ดี

    /// <summary>รายการรางวัลทั้งหมดตามที่ตั้งไว้</summary>
    public IReadOnlyList<RewardEntry> Catalog => rewards;

    /// <summary>ตัวเลือกที่ถูกล็อกไว้ตอนนี้</summary>
    public IReadOnlyList<RewardKind> ForcedOffer => forcedOffer;

    /// <summary>สุ่มมากี่ตัวเลือกต่อครั้ง</summary>
    public int OptionsPerOffer => optionsPerOffer;

    /// <summary>รางวัลนี้มีสิทธิ์ถูกสุ่มออกมาตอนนี้ไหม (ไม่นับการล็อก)</summary>
    public bool CanBeDrawn(RewardKind kind) => IsDrawable(Find(kind));

    /// <summary>ล็อกตัวเลือกที่จะโผล่มา — ไม่ส่งอะไรมาเลย = ล้างล็อก</summary>
    public void SetForcedOffer(params RewardKind[] kinds)
    {
        forcedOffer.Clear();
        if (kinds != null) forcedOffer.AddRange(kinds);

        // ตัวเลือกที่สุ่มค้างไว้ของ node ที่ยังไม่ได้กด ต้องทิ้ง
        // ไม่งั้นล็อกไม่ทันรอบที่เปิดแชทค้างไว้อยู่ ต้องรอรอบถัดไปถึงจะเห็นผล
        _offers.Clear();
    }

    /// <summary>
    /// ล้างทุกอย่างกลับเป็นเหมือนเพิ่งเริ่มเกม — ของที่ได้ไปแล้ว รอบ ตัวเลือกที่ค้างอยู่
    ///
    /// ดับเทียนให้ด้วย ไม่งั้นรางวัลเทียนจะไม่ถูกเสนออีกเลยทั้งที่สถานะล้างแล้ว
    /// (IsDrawable ของเทียนดูจำนวนเล่มที่ยังไม่ติด ไม่ได้ดูแค่ _claimed)
    /// </summary>
    public void ClearProgress()
    {
        _claimed.Clear();
        _offers.Clear();
        _roundsDone       = 0;
        LastGranted       = RewardKind.None;
        _lastGrantedLabel = "";

        if (GameModeController.instance != null)
            GameModeController.instance.ExtinguishAllCandles();

        // ผลของรางวัลจำสถานะไว้เอง ต้องสั่งให้อ่านใหม่ ไม่งั้นเสาไฟยังสว่างอยู่ทั้งที่ล้างแล้ว
        foreach (var effect in FindObjectsByType<RewardEffect>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None))
            effect.Refresh();

        Debug.Log("[Reward] ล้างสถานะรางวัลทั้งหมดแล้ว", this);
    }

    // ─────────────────────────── ข้อความ ───────────────────────────

    /// <summary>
    /// แทน {reward} ในข้อความด้วยชื่อรางวัลที่ผู้เล่นเพิ่งเลือก
    ///
    /// เช็กวงเล็บปีกกาก่อนไปหา instance — ข้อความส่วนใหญ่ไม่มี placeholder
    /// จะได้ไม่ต้องวิ่งหา component ทุกครั้งที่มีฟองข้อความใหม่
    /// </summary>
    public static string FormatText(string message)
    {
        if (string.IsNullOrEmpty(message) || message.IndexOf('{') < 0) return message;

        var self = instance;
        if (self == null || string.IsNullOrEmpty(self.rewardPlaceholder)) return message;

        // ชื่อรางวัลแปลตอนแทน ไม่ใช่ตอนเลือก — เก็บไว้เป็นต้นฉบับ ประวัติแชทที่วาดใหม่หลังเปลี่ยน
        // ภาษาจะได้ชื่อรางวัลเป็นภาษาใหม่ด้วย
        return message.Replace(self.rewardPlaceholder, L10n.T(self._lastGrantedLabel));
    }
}
