using System.Collections;
using System.Collections.Generic;
using InputSystem;
using Manager;
using Player;
using Rule4;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RuleSystem.Rule
{
    /// <summary>
    /// กฎข้อ 4 (22:00 – 22:40) — "ศาลพระภูมิว่างเปล่า ต้องหาตุ๊กตากลับมาวางให้ครบ"
    ///
    /// Flow:
    ///   1. ผู้เล่นนั่ง → blink เข้า Tension → เริ่มเล่น
    ///   2. ตุ๊กตา dollCount ตัวถูกสุ่มวางบนพื้น หาเจอได้ด้วยการฟังเสียงอย่างเดียว (ร้องไห้ / หัวเราะ / ฮัมเพลง)
    ///      ถ้ามีตัวไหนสุ่มอยู่ใต้ศาลา จะมีเงาโผล่ออกมาให้เห็นก่อน
    ///   3. เก็บทีละตัว → เดินไปวางที่ศาลพระภูมิ → ศาลย้ายที่ทุกครั้งที่เก็บได้ → ทำซ้ำจนครบ 5
    ///   4. ผี: เริ่มกฎ → มีผีผูกคอตายแขวนอยู่ (HangingGhost) ไม่ไล่ แค่ให้เห็น
    ///           วางตุ๊กตาตัวที่ 1 สำเร็จ → ตัวผีหายไป เหลือแต่เชือก
    ///           เก็บตุ๊กตาตัวที่ 3 ขึ้นมือ → spawn ผีไล่ (Rule4Ghost) ที่จุดไกล แล้วไล่ยาวจนจบกฎ
    ///        - กดคลิกขวาค้าง = จ้อง → ผีช้าลง แต่ขยับไม่ได้ (จ้องนานเท่าไรก็ได้ ไม่ตาย)
    ///        - กด Shift ค้าง = กลั้นหายใจ → ผีมองไม่เห็น หยุดรออยู่กับที่ แต่เดินช้ามาก + จอซีด/เบลอ/โยก
    ///        - ผีเข้าใกล้เกินไป = ตายทันที
    /// </summary>
    public class Rule4 : RuleBase
    {
        // ── References ──────────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private InputHandler inputHandler;
        [SerializeField] private BreathSystem breathSystem;
        [SerializeField] private StareSystem  stareSystem;
        [SerializeField] private SpiritHouse  spiritHouse;

        // ── Dolls ───────────────────────────────────────────────────────
        [Header("Dolls")]
        [Tooltip("Prefab ตัว logic ของตุ๊กตา (มี Doll.cs) — ไม่ต้องมี mesh ในตัว โมเดลมาจาก dollVariants")]
        [SerializeField] private Doll dollPrefab;

        [Tooltip("ตุ๊กตาแต่ละแบบ: โมเดลที่โผล่บนพื้น + ช่องบนศาลที่จะเปิดเมื่อวางสำเร็จ" +
                 "ต้องมีอย่างน้อยเท่ากับ dollCount — ระบบจะสับแล้วเลือกมา dollCount แบบทุกรอบ")]
        [SerializeField] private DollVariant[] dollVariants;

        [Tooltip("จำนวนตุ๊กตาที่ต้องเก็บ")]
        [SerializeField] private int dollCount = 6;

        [Tooltip("จุดสุ่มวางตุ๊กตาทั่วแมพ — ต้องมีอย่างน้อยเท่ากับ dollCount")]
        [SerializeField] private Transform[] dollSpawnPoints;

        [Tooltip("จุดสุ่มที่อยู่ 'ใต้ศาลา' — ถ้าตุ๊กตาลงจุดพวกนี้จะมีเงาโผล่มาเป็นใบ้")]
        [SerializeField] private Transform[] pavilionSpawnPoints;

        // ── Spirit House ────────────────────────────────────────────────
        [Header("Spirit House")]
        [Tooltip("จุดที่ศาลพระภูมิจะย้ายไป — สุ่มใหม่ทุกครั้งที่ผู้เล่นเก็บตุ๊กตาได้")]
        [SerializeField] private Transform[] shrineSpawnPoints;

        [Tooltip("ศาลต้องโผล่ห่างจากจุดที่เก็บตุ๊กตาอย่างน้อยกี่เมตร — บังคับให้ผู้เล่นต้องเดิน" + "ถ้าไม่มีจุดไหนไกลพอ จะเลือกจุดที่ไกลที่สุดแทน")]
        [SerializeField] private float minShrineDistanceFromDoll = 25f;

        // ── Ghost ───────────────────────────────────────────────────────
        [Header("Ghost")]
        [Tooltip("ผีที่ออกมาไล่หลังเก็บตุ๊กตาตัวแรก")]
        [SerializeField] private Rule4Ghost ghostPrefab;

        [Tooltip("จุด spawn ผี — จะเลือกจุดที่ไกลจากผู้เล่นที่สุด")]
        [SerializeField] private Transform[] ghostSpawnPoints;

        [Tooltip("ผีไล่จะออกมาตอนผู้เล่น 'เก็บ' ตุ๊กตาตัวที่เท่าไรขึ้นมือ (สเปก = 3)")]
        [SerializeField] private int chaseStartOnPickup = 3;

        // ── Hanging Ghost ───────────────────────────────────────────────
        [Header("Hanging Ghost (ผีผูกคอ)")]
        [Tooltip("prefab ผีผูกคอ — โผล่ตั้งแต่เริ่มกฎ ไม่ไล่")]
        [SerializeField] private HangingGhost hangingGhostPrefab;

        [Tooltip("จุดที่ผีผูกคอแขวนอยู่")]
        [SerializeField] private Transform hangingPoint;

        [Tooltip("ตัวผีหายไป (เหลือแต่เชือก) หลังผู้เล่นวางตุ๊กตาสำเร็จกี่ตัว (สเปก = 1)")]
        [SerializeField] private int hangingVanishAfterPlaced = 1;

        // ── Street Lamps ────────────────────────────────────────────────
        [Header("Street Lamps")]
        [Tooltip("ตัวคุมเสาไฟกระพริบตอนเริ่มกฎ — เว้นว่างได้ถ้ายังไม่ได้ทำ")]
        [SerializeField] private StreetLampFlicker lampFlicker;

        // ── Pavilion Shadow ─────────────────────────────────────────────
        [Header("Pavilion Shadow (ใบ้ตุ๊กตาใต้ศาลา)")]
        [SerializeField] private GameObject pavilionShadowPrefab;
        [SerializeField] private Transform  pavilionShadowPoint;
        [SerializeField] private float      shadowDelay    = 4f;
        [SerializeField] private float      shadowDuration = 2.5f;

        // ── Hints ───────────────────────────────────────────────────────
        [Header("Hints")]
        [TextArea(2, 4)]
        [Tooltip("ใส่ {0} ตรงที่ต้องการให้แทนด้วยจำนวนตุ๊กตา (dollCount) — จะได้ไม่ต้องแก้ข้อความเวลาเปลี่ยนจำนวน")]
        [SerializeField] private string introHint = "ศาลพระภูมิว่างเปล่า…\nหาตุ๊กตา {0} ตัวกลับมาวางให้ครบ";

        [Tooltip("แสดง introHint กี่วินาทีก่อนซ่อน")]
        [SerializeField] private float introHintDuration = 6f;

        // ── Audio ───────────────────────────────────────────────────────
        [Header("Audio")]
        [Tooltip("เก็บตุ๊กตาตัวที่เท่าไรถึงเริ่มสุ่มเสียงบรรยากาศ (สเปก = 1)")]
        [SerializeField] private int ambientStingerStartOnPickup = 1;

        [Tooltip("เก็บตุ๊กตาตัวที่เท่าไรถึงเล่นฆ้อง + อีกา แล้วหยุดเพลงพื้นหลัง (สเปก = 2)")]
        [SerializeField] private int gongOnPickup = 2;

        [Tooltip("เว้นระยะระหว่างการสุ่มเสียงบรรยากาศแต่ละครั้ง (วินาที)")]
        [SerializeField] private float ambientStingerMinGap = 6f;
        [SerializeField] private float ambientStingerMaxGap = 10f;

        [Tooltip("โอกาสที่แต่ละรอบจะมีเสียงจริง (0–1) — ที่เหลือคือ 'อาจจะไม่มี'")]
        [Range(0f, 1f)]
        [SerializeField] private float ambientStingerChance = 0.75f;

        // ── Game Over ───────────────────────────────────────────────────
        [Header("Game Over Reset")]
        [Tooltip("เวลาที่ rewind กลับไปหลังตาย (ก่อน 22:00)")]
        [SerializeField] private int gameOverResetHour   = 21;
        [SerializeField] private int gameOverResetMinute = 55;

        // ── Runtime ─────────────────────────────────────────────────────
        private readonly List<Doll>       _dolls   = new List<Doll>();
        private readonly List<GameObject> _shadows = new List<GameObject>();

        // คิวที่สับไว้ตอนเริ่มกฎ แล้วแจกทีละตัว — ตุ๊กตาโผล่ทีละตัว ไม่ใช่พร้อมกันหมด
        private readonly List<Transform>   _spawnQueue   = new List<Transform>();
        private readonly List<DollSound>   _soundQueue   = new List<DollSound>();
        private readonly List<DollVariant> _variantQueue = new List<DollVariant>();
        private int _dollsSpawned;

        // variant ที่ไม่ถูกสุ่มมาให้หาในรอบนี้ — ถือว่า "วางอยู่บนศาลแล้ว" โชว์ตั้งแต่เริ่มกฎ
        private readonly List<DollVariant> _prePlacedVariants = new List<DollVariant>();

        private Rule4Ghost   _ghost;
        private HangingGhost _hangingGhost;
        private Doll         _carriedDoll;
        private int          _dollsPlaced;
        private int          _dollsPickedUp;   // นับตุ๊กตาที่ถูกหยิบขึ้นมือ — ตัวที่ chaseStartOnPickup จะปลุกผีไล่

        // สถานะเสียงตามจังหวะกฎ
        private bool      _backgroundStarted;     // เพลงพื้นหลังเริ่มแล้ว (ตอนลุกจากศาลาครั้งแรก)
        private bool      _ghostBackgroundPlaying;
        private Coroutine _ambientStingerRoutine;
        private int        _pickupFrame = -1;
        private bool       _wasHoldingBreath;
        private bool       _gameplayActive;
        private bool       _isEnding;

        /// <summary>true = ผู้เล่นถือตุ๊กตาอยู่ (เก็บได้ทีละตัว)</summary>
        public bool IsCarryingDoll => _carriedDoll != null;

        /// <summary>
        /// true = วางตุ๊กตาได้จริงตอนนี้
        /// Doll กับ SpiritHouse subscribe OnInteractPressed ตัวเดียวกัน ถ้าตุ๊กตาอยู่ในระยะศาลพอดี
        /// กด E ครั้งเดียวจะยิงทั้งสองตัว → เก็บแล้ววางทันทีในเฟรมเดียว จึงต้องกันไว้ 1 เฟรม
        /// </summary>
        public bool CanPlaceDoll => _carriedDoll != null && Time.frameCount != _pickupFrame;

        /// <summary>ตุ๊กตาที่ถืออยู่ — SpiritHouse ใช้อ่าน ShrineSlotIndex ตอนวาง</summary>
        public Doll CarriedDoll => _carriedDoll;

        /// <summary>ตุ๊กตาที่ spawn อยู่ในรอบนี้ — ใช้โดย Rule4DevSkip เพื่อวาดเส้น debug</summary>
        public IReadOnlyList<Doll> ActiveDolls => _dolls;

        /// <summary>ผีของรอบนี้ (null ถ้ายังไม่ spawn) — ใช้โดย Rule4DevSkip</summary>
        public Rule4Ghost ActiveGhost => _ghost;

        // ════════════════════════════════════════════════════════════════
        #region Lifecycle
        // ════════════════════════════════════════════════════════════════

        public override void StartRule()
        {
            base.StartRule();
            StartCoroutine(RuleFlow());
        }

        bool PlayerIsSitting()  => PlayerController.Instance.IsSitting();
        bool PlayerEyesOpened() => GameModeController.instance.IsEyesOpen;

        IEnumerator RuleFlow()
        {
            TimeManager.instance.IsPauseTime(true);

            yield return new WaitUntil(PlayerIsSitting);
            PlayerController.Instance.isBlockStanding = true;

            GameModeController.instance.BlinkToMode(GameMode.Tension);
            yield return null;                                      // ให้ BlinkRoutine เซ็ต IsEyesOpen = false ก่อน
            yield return new WaitUntil(() => !PlayerEyesOpened());   // รอตาปิดสนิท
            yield return new WaitUntil(PlayerEyesOpened);            // รอตาเปิดสนิทหลัง transition

            StartGameplay();
        }

        protected override void UpdateRule()
        {
            if (!_gameplayActive) return;

            AudioManager.instance.UpdateHeartbeat();
            UpdateBackgroundMusic();

            if (breathSystem == null) return;

            bool holding = breathSystem.IsHolding;

            // ผีจะหยุดรอถ้าผู้เล่นกำลังกลั้นหายใจ
            if (_ghost != null) _ghost.IsPlayerHidden = holding;

            // เสียงผี 2 ตัว — ยิงครั้งเดียวตอนสถานะเปลี่ยน (edge) ไม่ใช่ทุกเฟรม
            // ปล่อย = ครอบคลุมทั้งปล่อยเองและถูกบังคับหายใจออก
            if (holding != _wasHoldingBreath)
            {
                _wasHoldingBreath = holding;

                // เล่นเฉพาะตอนผีไล่อยู่จริง — ช่วงที่มันยืนเป็นป้าย / ถูกซ่อน / ยังไม่ spawn
                // การกลั้นหายใจไม่มีความหมายอะไรกับผี เสียงที่ดังขึ้นมาเลยกลายเป็นบั๊กที่คนเล่นงง
                if (IsGhostChasing())
                {
                    Vector3 pos = _ghost.transform.position;
                    if (holding) AudioManager.instance.PlayGhostOnBreathHold(pos);
                    else         AudioManager.instance.PlayGhostOnBreathRelease(pos);
                }
            }
        }

        /// <summary>
        /// เพลงพื้นหลังผูกกับ "อยู่ที่ศาลาหรือเปล่า":
        ///   ลุกออกจากศาลาครั้งแรก → เพลงพื้นหลัง Rule 4 (เล่นจนเก็บตุ๊กตาตัวที่ gongOnPickup)
        ///   กลับมานั่งระหว่างที่เพลงผีไล่เล่นอยู่ → หยุดเพลงผี
        /// </summary>
        void UpdateBackgroundMusic()
        {
            bool sitting = PlayerIsSitting();

            if (!_backgroundStarted && !sitting)
            {
                _backgroundStarted = true;
                AudioManager.instance.StartRule4Background();
            }

            if (_ghostBackgroundPlaying && sitting)
            {
                _ghostBackgroundPlaying = false;
                AudioManager.instance.StopRule4GhostBackground();
            }
        }

        /// <summary>
        /// เสียงบรรยากาศสุ่ม — ทุก 6-10 วิ ทอยว่าจะมีเสียงไหม (ambientStingerChance)
        /// ตัว event ใน FMOD เป็น Multi Instrument 4 เสียง จึงได้ "สุ่ม 1 ใน 4 หรือไม่มีเลย" ตามสเปก
        /// </summary>
        IEnumerator AmbientStingerRoutine()
        {
            while (_gameplayActive)
            {
                yield return new WaitForSeconds(Random.Range(ambientStingerMinGap, ambientStingerMaxGap));
                if (!_gameplayActive) yield break;

                if (Random.value < ambientStingerChance)
                    AudioManager.instance.PlayRule4AmbientStinger();
            }
        }

        void StopAmbientStingers()
        {
            if (_ambientStingerRoutine == null) return;
            StopCoroutine(_ambientStingerRoutine);
            _ambientStingerRoutine = null;
        }

        /// <summary>ผีโผล่อยู่และอยู่ในโหมดไล่ — เงื่อนไขเดียวที่เสียงกลั้น/ปล่อยหายใจของผีควรดัง</summary>
        bool IsGhostChasing()
            => _ghost != null
            && _ghost.gameObject.activeInHierarchy
            && _ghost.Mode == GhostMode.Chase;

        public override void EndRule()
        {
            if (_isEnding) return;
            _isEnding       = true;
            _gameplayActive = false;

            CleanupGameplay();

            PlayerController.Instance.isBlockStanding = false;
            TimeManager.instance.SetTime(22, 39);
            TimeManager.instance.IsPauseTime(false);
            GameModeController.instance.BlinkToMode(GameMode.Relax);

            base.EndRule();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Gameplay Start
        // ════════════════════════════════════════════════════════════════

        void StartGameplay()
        {
            _isEnding       = false;
            _dollsPlaced    = 0;
            _dollsPickedUp  = 0;
            _carriedDoll    = null;
            _backgroundStarted      = false;
            _ghostBackgroundPlaying = false;
            _gameplayActive = true;
            _wasHoldingBreath = false;

            // ปลดล็อกการลุกก่อนเสมอ — ถ้าโค้ดข้างล่างเกิด throw (เช่น reference ใน Inspector ว่าง
            // หรือ FMOD event หาย) ผู้เล่นจะได้ไม่ติดอยู่ในท่านั่งจนเล่นต่อไม่ได้
            PlayerController.Instance.isBlockStanding = false;

            // ต้อง pause ซ้ำตรงนี้ — GameModeController.BlinkRoutine สั่ง IsPauseTime(false)
            // ทุกครั้งที่ตาเปิดสุด ค่าที่ RuleFlow ตั้งไว้ตอนต้นจึงถูกล้างไปแล้ว
            // Rule 4 ไม่ได้ใช้เวลาเป็นตัวจับจังหวะ กฎจบเมื่อวางตุ๊กตาครบเท่านั้น
            TimeManager.instance.IsPauseTime(true);

            if (!PrepareDollQueue()) return;
            SpawnNextDoll();

            if (spiritHouse != null)
            {
                spiritHouse.Setup(this, inputHandler);   // ResetSlots ข้างใน — ต้องเรียกก่อนเปิดช่อง

                // ช่องของตุ๊กตาที่ไม่ได้หายไปในรอบนี้ โชว์ไว้เลย ศาลจะได้ดู "ขาดไปบางตัว" ไม่ใช่ว่างทั้งหมด
                foreach (var v in _prePlacedVariants)
                    spiritHouse.RevealSlot(v.shrineSlotIndex, v.name);

                // ตำแหน่งเริ่มต้น: ห่างจากจุดที่ผู้เล่นนั่งอยู่
                spiritHouse.RelocateTo(PickShrinePointAwayFrom(PlayerController.Instance.transform.position));
            }

            if (breathSystem != null)
            {
                breathSystem.OnForcedExhale -= HandleForcedExhale;   // กัน subscribe ซ้ำตอน retry
                breathSystem.OnForcedExhale += HandleForcedExhale;
                breathSystem.BeginRule();
            }
            if (stareSystem != null) stareSystem.BeginRule();

            // ผีผูกคอโผล่ตั้งแต่ต้น — แค่แขวนให้เห็น ผีไล่จริงค่อยมาตอนเก็บตุ๊กตาตัวที่ chaseStartOnPickup
            SpawnHangingGhost();

            // เพลงพื้นหลังยังไม่เริ่ม — รอให้ผู้เล่นลุกออกจากศาลาก่อน (เช็คใน UpdateRule)
            AudioManager.instance.ResetHeartbeatLevel();

            // เสาไฟทั่วแมพกระพริบไปตลอดจนจบกฎ — CleanupGameplay เป็นคน Stop()
            if (lampFlicker != null) lampFlicker.Play();

            if (PlayerDialogueUI.instance != null)
            {
                // แทน {0} ด้วยจำนวนตุ๊กตาจริง — ใช้ Replace ไม่ใช่ string.Format
                // เพราะข้อความที่พิมพ์เอง อาจมี { } หลุดมาแล้ว Format จะ throw
                // PlayerDialogueUI fade เองเมื่อ hold ครบ — ไม่ต้องมี coroutine คอยซ่อน
                // (ถ้ามี จะไปลบ hint ของตุ๊กตาที่เดินไปเจอภายใน introHintDuration ทิ้ง)
                PlayerDialogueUI.instance.ShowLine(introHint.Replace("{0}", dollCount.ToString()),
                                                   introHintDuration);
            }
        }

        /// <summary>
        /// สับจุด spawn / เสียง / โมเดล ไว้ล่วงหน้าเป็นคิว dollCount ใบ แต่ยังไม่ Instantiate
        /// ตุ๊กตาจะถูกปล่อยทีละตัวผ่าน SpawnNextDoll() — ผู้เล่นเจอทีละตัว ไม่ใช่โผล่พร้อมกันหมด
        /// </summary>
        bool PrepareDollQueue()
        {
            ClearDolls();

            _spawnQueue.Clear();
            _soundQueue.Clear();
            _variantQueue.Clear();
            _dollsSpawned = 0;

            var pool = new List<Transform>();
            if (dollSpawnPoints     != null) pool.AddRange(dollSpawnPoints);
            if (pavilionSpawnPoints != null) pool.AddRange(pavilionSpawnPoints);
            pool.RemoveAll(p => p == null);

            if (pool.Count < dollCount)
            {
                Debug.LogError($"[Rule4] จุด spawn ตุ๊กตามีแค่ {pool.Count} จุด แต่ต้องการ {dollCount} — เพิ่มจุดใน Inspector", this);
                return false;
            }

            // Fisher-Yates
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            // เสียงของตุ๊กตาต้องไม่ซ้ำกัน และสับใหม่ทุกรอบ — สับไพ่ชุดเสียงแล้วแจกทีละใบ
            var sounds = new List<DollSound>((DollSound[])System.Enum.GetValues(typeof(DollSound)));
            for (int i = sounds.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (sounds[i], sounds[j]) = (sounds[j], sounds[i]);
            }

            // ตุ๊กตาแต่ละตัวหน้าตาไม่เหมือนกัน — สับชุด variant แล้วแจกทีละแบบ ไม่ให้ซ้ำในรอบเดียว
            var variants = BuildShuffledVariants();
            if (variants == null) return false;

            for (int i = 0; i < dollCount; i++)
            {
                _spawnQueue.Add(pool[i]);
                _soundQueue.Add(sounds[i % sounds.Count]);   // ถ้าตุ๊กตามากกว่าจำนวนเสียง ค่อยวนใช้ซ้ำ
                _variantQueue.Add(variants[i]);
            }

            // ที่เหลือจากการสุ่ม (เช่น มี 8 แบบ สุ่มมาหา 6) = ตุ๊กตาที่ยังอยู่บนศาลตามปกติ
            _prePlacedVariants.Clear();
            for (int i = dollCount; i < variants.Count; i++)
                _prePlacedVariants.Add(variants[i]);

            return true;
        }

        /// <summary>
        /// ปล่อยตุ๊กตาตัวถัดไปจากคิว — เรียกตอนเริ่มกฎ 1 ครั้ง แล้วเรียกอีกทุกครั้งที่วางสำเร็จ
        /// คืน Doll ที่เพิ่ง spawn (null ถ้าคิวหมดแล้ว)
        /// </summary>
        Doll SpawnNextDoll()
        {
            if (_dollsSpawned >= _spawnQueue.Count) return null;

            Transform point = _spawnQueue[_dollsSpawned];
            var doll = Instantiate(dollPrefab, point.position, point.rotation);
            doll.Setup(this, inputHandler, _soundQueue[_dollsSpawned], _variantQueue[_dollsSpawned]);
            _dolls.Add(doll);
            _dollsSpawned++;

            // ตัวนี้ไปโผล่ใต้ศาลา → ปล่อยเงาออกมาใบ้ก่อนผู้เล่นไปเก็บ
            if (IsPavilionPoint(point)) StartCoroutine(PavilionShadowRoutine());

            return doll;
        }

        /// <summary>
        /// สับชุด variant แล้วคืนมา dollCount ตัว — พร้อมเช็คว่า Inspector ตั้งค่ามาถูก
        /// คืน null ถ้าตั้งค่าไม่ครบ (จะได้ไม่ spawn ตุ๊กตาที่ไม่มีโมเดล/ไม่มีช่องบนศาล)
        /// </summary>
        List<DollVariant> BuildShuffledVariants()
        {
            if (dollVariants == null || dollVariants.Length < dollCount)
            {
                int have = dollVariants != null ? dollVariants.Length : 0;
                Debug.LogError($"[Rule4] dollVariants มีแค่ {have} แบบ แต่ต้องการ {dollCount} — " +
                               "เพิ่มใน Inspector (1 แบบ = 1 โมเดล + 1 ช่องบนศาล)", this);
                return null;
            }

            var list = new List<DollVariant>(dollVariants);
            list.RemoveAll(v => v == null);

            // เตือนถ้ามี variant ชี้ช่องบนศาลซ้ำกัน — จะทำให้ตุ๊กตาตัวหลังวางแล้วไม่มีอะไรโผล่
            var seen = new HashSet<int>();
            foreach (var v in list)
            {
                if (!seen.Add(v.shrineSlotIndex))
                    Debug.LogWarning($"[Rule4] variant '{v.name}' ใช้ shrineSlotIndex " +
                                     $"{v.shrineSlotIndex} ซ้ำกับตัวอื่น — ช่องบนศาลจะโผล่ไม่ครบ", this);
            }

            if (list.Count < dollCount)
            {
                Debug.LogError($"[Rule4] dollVariants มีช่องว่าง (null) เหลือใช้ได้ {list.Count} " +
                               $"แต่ต้องการ {dollCount}", this);
                return null;
            }

            // Fisher-Yates
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        bool IsPavilionPoint(Transform point)
        {
            if (pavilionSpawnPoints == null) return false;
            foreach (var p in pavilionSpawnPoints)
                if (p == point) return true;
            return false;
        }

        IEnumerator PavilionShadowRoutine()
        {
            if (pavilionShadowPrefab == null || pavilionShadowPoint == null) yield break;

            yield return new WaitForSeconds(shadowDelay);
            if (!_gameplayActive) yield break;

            var shadow = Instantiate(pavilionShadowPrefab,
                                     pavilionShadowPoint.position,
                                     pavilionShadowPoint.rotation);
            _shadows.Add(shadow);

            yield return new WaitForSeconds(shadowDuration);

            if (shadow != null)
            {
                _shadows.Remove(shadow);
                Destroy(shadow);
            }
        }

        /// <summary>
        /// เลือกจุดวางศาลที่ "ห่างจากตุ๊กตา" ตาม design — ผู้เล่นต้องเดินไกลไปหาศาลเสมอ
        /// สุ่มจากจุดที่ห่างเกิน minShrineDistanceFromDoll ถ้าไม่มีเลยก็เอาจุดที่ไกลที่สุด
        /// </summary>
        Transform PickShrinePointAwayFrom(Vector3 dollPosition)
        {
            if (shrineSpawnPoints == null || shrineSpawnPoints.Length == 0)
            {
                Debug.LogWarning("[Rule4] ยังไม่ได้ใส่ shrineSpawnPoints — ศาลจะไม่ย้ายที่", this);
                return null;
            }

            Transform current  = spiritHouse != null ? spiritHouse.transform : null;
            Transform farthest = null;
            float     farthestDist = -1f;

            var candidates = new List<Transform>();

            foreach (var p in shrineSpawnPoints)
            {
                if (p == null) continue;

                // ไม่ย้ายไปจุดเดิม
                if (current != null && Vector3.Distance(p.position, current.position) < 0.1f) continue;

                float d = Vector3.Distance(p.position, dollPosition);
                if (d > farthestDist) { farthestDist = d; farthest = p; }
                if (d >= minShrineDistanceFromDoll) candidates.Add(p);
            }

            if (candidates.Count > 0) return candidates[Random.Range(0, candidates.Count)];

            if (farthest == null)
                Debug.LogWarning("[Rule4] ไม่มีจุดวางศาลให้เลือกเลย (จุดเดียวหรือว่างหมด)", this);

            return farthest;
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Doll Pickup / Place
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// เรียกโดย Doll ตอนผู้เล่นกด E — คืน false ถ้ายังถือตุ๊กตาตัวอื่นอยู่
        /// เก็บได้ → ศาลพระภูมิย้ายที่ทันที; เก็บตัวที่ chaseStartOnPickup → ผีไล่ออกมา
        /// </summary>
        public bool TryPickUpDoll(Doll doll)
        {
            if (!_gameplayActive || _carriedDoll != null) return false;

            _carriedDoll = doll;
            _pickupFrame = Time.frameCount;
            Debug.Log($"[Rule4 id={GetInstanceID()}] เก็บตุ๊กตา {doll.name} ({doll.Sound}) " +
                      $"— IsCarryingDoll={IsCarryingDoll}, spiritHouse={(spiritHouse != null ? spiritHouse.GetInstanceID().ToString() : "null")}", this);

            // ศาลย้ายไปจุดที่ห่างจากตุ๊กตาตัวนี้ — ผู้เล่นต้องเดินไกลไปวาง
            if (spiritHouse != null)
                spiritHouse.RelocateTo(PickShrinePointAwayFrom(doll.transform.position));

            _dollsPickedUp++;

            // ตัวที่ 1: เริ่มสุ่มเสียงบรรยากาศ
            if (_dollsPickedUp == ambientStingerStartOnPickup && _ambientStingerRoutine == null)
                _ambientStingerRoutine = StartCoroutine(AmbientStingerRoutine());

            // ตัวที่ 2: ฆ้อง + อีกาแตกตื่น "ผีมาแล้วนะ" แล้วเพลงพื้นหลังเงียบลง
            if (_dollsPickedUp == gongOnPickup)
            {
                AudioManager.instance.PlayTempleGong();
                AudioManager.instance.PlayCrowPanic();
                AudioManager.instance.StopRule4Background();
            }

            // ตัวที่ chaseStartOnPickup: ผีไล่ออกมาจากจุดไกล + เพลงผีไล่ + เลิกสุ่มเสียงบรรยากาศ
            if (_dollsPickedUp == chaseStartOnPickup && _ghost == null)
            {
                StopAmbientStingers();
                AudioManager.instance.StopRule4Background();   // เผื่อ gongOnPickup ตั้งไว้มากกว่า

                SpawnGhost();
                if (_ghost != null) _ghost.BeginChase();

                _ghostBackgroundPlaying = true;
                AudioManager.instance.StartRule4GhostBackground();
            }

            return true;
        }

        /// <summary>เรียกโดย SpiritHouse ตอนผู้เล่นวางตุ๊กตาสำเร็จ</summary>
        public void OnDollPlaced()
        {
            if (!_gameplayActive || _carriedDoll == null) return;

            _dolls.Remove(_carriedDoll);
            Destroy(_carriedDoll.gameObject);
            _carriedDoll = null;
            _dollsPlaced++;

            if (_dollsPlaced >= dollCount)
            {
                StartCoroutine(CompleteRoutine());
                return;
            }

            SpawnNextDoll();

            // วางครบตามที่กำหนด → ผีผูกคอหายไป เหลือแต่เชือก (เสียงทรมานหยุดด้วย)
            if (_dollsPlaced == hangingVanishAfterPlaced && _hangingGhost != null)
            {
                _hangingGhost.Vanish();
                AudioManager.instance.StopHangingGhostSounds();
            }
            // ถ้าผีไล่ออกมาแล้ว → เร็วขึ้นทุกครั้งที่วางสำเร็จ
            if (_ghost != null) _ghost.OnDollDelivered();
        }

        /// <summary>ผู้เล่นกลั้นหายใจไม่ไหว → ผีวิ่งเข้ามา (BreathSystem.OnForcedExhale)</summary>
        void HandleForcedExhale()
        {
            if (_ghost != null) _ghost.StartSprint();
        }

        IEnumerator CompleteRoutine()
        {
            _gameplayActive = false;

            if (_ghost != null) _ghost.Deactivate();
            if (breathSystem != null) breathSystem.EndRuleCleanup();
            if (stareSystem  != null) stareSystem.EndRuleCleanup();

            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            yield return new WaitForSeconds(2f);

            EndRule();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Ghost
        // ════════════════════════════════════════════════════════════════

        void SpawnGhost()
        {
            if (ghostPrefab == null)
            {
                Debug.LogError("[Rule4] ยังไม่ได้ใส่ ghostPrefab — ผีจะไม่ออกมาไล่", this);
                return;
            }

            Transform point = PickFarthestGhostPoint();
            Vector3 pos = point != null ? point.position : PlayerController.Instance.transform.position;
            Quaternion rot = point != null ? point.rotation : Quaternion.identity;

            // Init ยังไม่สั่งให้ขยับ — คนเรียกเป็นคนตัดสินว่าจะให้ยืนเป็นป้าย (StandBeside) หรือไล่ (BeginChase)
            _ghost = Instantiate(ghostPrefab, pos, rot);
            _ghost.Init(PlayerController.Instance.transform, this);
            if (stareSystem != null) stareSystem.SetGhost(_ghost);
        }

        void SpawnHangingGhost()
        {
            if (hangingGhostPrefab == null)
            {
                Debug.LogWarning("[Rule4] ยังไม่ได้ใส่ hangingGhostPrefab — จะไม่มีผีผูกคอตอนเริ่มกฎ", this);
                return;
            }

            if (hangingPoint == null)
            {
                Debug.LogError("[Rule4] ยังไม่ได้ใส่ hangingPoint — ไม่รู้จะแขวนผีตรงไหน", this);
                return;
            }

            _hangingGhost = Instantiate(hangingGhostPrefab, hangingPoint.position, hangingPoint.rotation);

            // กระดูกหัก + หายใจทรมาน ดังจากตัวผีระหว่างอนิเมชั่นผูกคอ
            AudioManager.instance.StartHangingGhostSounds(_hangingGhost.transform);
        }

        Transform PickFarthestGhostPoint()
        {
            if (ghostSpawnPoints == null || ghostSpawnPoints.Length == 0) return null;

            Vector3 playerPos = PlayerController.Instance.transform.position;
            Transform best = null;
            float bestDist = -1f;

            foreach (var p in ghostSpawnPoints)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.position, playerPos);
                if (d > bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Game Over
        // ════════════════════════════════════════════════════════════════

        /// <summary>เรียกโดย Rule4Ghost เมื่อผีเข้าใกล้เกินระยะ — ตายทันที</summary>
        public void OnGhostCaughtPlayer()
        {
            if (!_gameplayActive) return;
            AudioManager.instance.PlayRule3Death();
            StartCoroutine(GameOverRoutine());
        }

        IEnumerator GameOverRoutine()
        {
            _gameplayActive = false;

            // ── หยุดทุกระบบของ Rule 4 ก่อน animation ตาย ──
            if (_ghost != null) _ghost.Deactivate();
            if (breathSystem != null) breathSystem.EndRuleCleanup();
            if (stareSystem  != null) stareSystem.EndRuleCleanup();
            AudioManager.instance.StopAllRule4Sounds();
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            yield return StartCoroutine(PlayerController.Instance.PlayDeathFallRoutine());
            yield return new WaitForSeconds(0.5f);

            CleanupGameplay();

            PlayerController.Instance.SetMovement(true);
            PlayerController.Instance.isBlockStanding = false;
            AudioManager.instance.ResetHeartbeatLevel();

            // ── blink กลับ Relax แล้ว reset กล้องตอนจอดำ ──
            GameModeController.instance.DirectBlinkToMode(
                GameMode.Relax,
                onEyesClosed: () =>
                {
                    PlayerController.Instance.ResetCameraAfterDeath();
                    PlayerController.Instance.SetMovement(true);
                });

            yield return new WaitUntil(() => GameModeController.instance.IsEyesOpen);
            yield return null;   // ตัด synchronous chain ก่อน SetTime → CheckRules

            // ── rewind เวลากลับก่อน 22:00 ให้ RuleManager เรียก Rule 4 ใหม่ ──
            TimeManager.instance.SetTime(gameOverResetHour, gameOverResetMinute);
            TimeManager.instance.IsPauseTime(false);

            _isEnding = false;
            base.EndRule();   // ruleActive = false → CheckRules จะ retrigger เมื่อถึง 22:00
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Cleanup
        // ════════════════════════════════════════════════════════════════

        void CleanupGameplay()
        {
            if (breathSystem != null) breathSystem.OnForcedExhale -= HandleForcedExhale;
            if (lampFlicker  != null) lampFlicker.Stop();
            StopAmbientStingers();
            _ghostBackgroundPlaying = false;

            if (_ghost != null) _ghost.Deactivate();
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;

            if (_hangingGhost != null) Destroy(_hangingGhost.gameObject);
            _hangingGhost = null;

            ClearDolls();

            foreach (var s in _shadows)
                if (s != null) Destroy(s);
            _shadows.Clear();

            if (spiritHouse != null) spiritHouse.ResetSlots();

            if (breathSystem != null) breathSystem.EndRuleCleanup();
            if (stareSystem  != null) stareSystem.EndRuleCleanup();

            AudioManager.instance.StopAllRule4Sounds();
            AudioManager.instance.ResetHeartbeatLevel();

            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        void ClearDolls()
        {
            foreach (var d in _dolls)
                if (d != null) Destroy(d.gameObject);
            _dolls.Clear();
            _carriedDoll = null;
            _dollsSpawned = 0;
        }

        // กันค่า/เสียงค้างตอนกด Stop ใน Editor กลางคัน
        private void OnDisable()
        {
            if (breathSystem != null) breathSystem.OnForcedExhale -= HandleForcedExhale;
            if (breathSystem != null) breathSystem.EndRuleCleanup();
            if (stareSystem  != null) stareSystem.EndRuleCleanup();
        }

        #endregion
    }
}
