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
    ///   2. ตุ๊กตา 5 ตัวถูกสุ่มวางบนพื้น หาเจอได้ด้วยการฟังเสียงอย่างเดียว (ร้องไห้ / หัวเราะ / ฮัมเพลง)
    ///      ถ้ามีตัวไหนสุ่มอยู่ใต้ศาลา จะมีเงาโผล่ออกมาให้เห็นก่อน
    ///   3. เก็บทีละตัว → เดินไปวางที่ศาลพระภูมิ → ศาลย้ายที่ทุกครั้งที่เก็บได้ → ทำซ้ำจนครบ 5
    ///   4. ผี: ตุ๊กตา 2 ตัวแรก ผียืนนิ่งเป็น "ป้ายบอกตำแหน่ง" อยู่ข้างตุ๊กตา
    ///           เก็บตุ๊กตา → ผีหายไป, วางที่ศาลเสร็จ → ผีโผล่ข้างตุ๊กตาตัวถัดไป
    ///           วางตัวที่ 2 เสร็จ → spawn ผีตัวใหม่ที่จุดไกล แล้วไล่ยาวจนจบกฎ
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
        [SerializeField] private Doll dollPrefab;

        [Tooltip("จำนวนตุ๊กตาที่ต้องเก็บ")]
        [SerializeField] private int dollCount = 5;

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

        [Tooltip("ผีจะไป 'ยืนเป็นป้าย' ข้างตุ๊กตากี่ตัวแรก — หลังจากนั้น spawn ใหม่แล้วไล่ตลอด (สเปก = 2)")]
        [SerializeField] private int guidedDollCount = 2;

        [Tooltip("ผียืนห่างจากตุ๊กตากี่เมตรตอนเป็นป้ายบอกตำแหน่ง")]
        [SerializeField] private float ghostMarkerOffset = 1.2f;

        // ── Pavilion Shadow ─────────────────────────────────────────────
        [Header("Pavilion Shadow (ใบ้ตุ๊กตาใต้ศาลา)")]
        [SerializeField] private GameObject pavilionShadowPrefab;
        [SerializeField] private Transform  pavilionShadowPoint;
        [SerializeField] private float      shadowDelay    = 4f;
        [SerializeField] private float      shadowDuration = 2.5f;

        // ── Hints ───────────────────────────────────────────────────────
        [Header("Hints")]
        [TextArea(2, 4)]
        [SerializeField] private string introHint = "ศาลพระภูมิว่างเปล่า…\nหาตุ๊กตา 5 ตัวกลับมาวางให้ครบ";

        [Tooltip("แสดง introHint กี่วินาทีก่อนซ่อน")]
        [SerializeField] private float introHintDuration = 6f;

        // ── Game Over ───────────────────────────────────────────────────
        [Header("Game Over Reset")]
        [Tooltip("เวลาที่ rewind กลับไปหลังตาย (ก่อน 22:00)")]
        [SerializeField] private int gameOverResetHour   = 21;
        [SerializeField] private int gameOverResetMinute = 55;

        // ── Runtime ─────────────────────────────────────────────────────
        private readonly List<Doll>       _dolls   = new List<Doll>();
        private readonly List<GameObject> _shadows = new List<GameObject>();

        private Rule4Ghost _ghost;
        private Doll       _carriedDoll;
        private int        _dollsPlaced;
        private int        _pickupFrame = -1;
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

            // ผีจะหยุดรอถ้าผู้เล่นกำลังกลั้นหายใจ
            if (_ghost != null && breathSystem != null)
                _ghost.IsPlayerHidden = breathSystem.IsHolding;
        }

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
            _carriedDoll    = null;
            _gameplayActive = true;

            // ปลดล็อกการลุกก่อนเสมอ — ถ้าโค้ดข้างล่างเกิด throw (เช่น reference ใน Inspector ว่าง
            // หรือ FMOD event หาย) ผู้เล่นจะได้ไม่ติดอยู่ในท่านั่งจนเล่นต่อไม่ได้
            PlayerController.Instance.isBlockStanding = false;

            // ต้อง pause ซ้ำตรงนี้ — GameModeController.BlinkRoutine สั่ง IsPauseTime(false)
            // ทุกครั้งที่ตาเปิดสุด ค่าที่ RuleFlow ตั้งไว้ตอนต้นจึงถูกล้างไปแล้ว
            // Rule 4 ไม่ได้ใช้เวลาเป็นตัวจับจังหวะ กฎจบเมื่อวางตุ๊กตาครบเท่านั้น
            TimeManager.instance.IsPauseTime(true);

            SpawnDolls();

            if (spiritHouse != null)
            {
                spiritHouse.Setup(this, inputHandler);
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

            // ผีอยู่ตั้งแต่ต้น — ตุ๊กตา 2 ตัวแรกมันไปยืนเป็นป้ายบอกตำแหน่งให้
            SpawnGhost();
            ShowGhostMarkerAtNextDoll();

            AudioManager.instance.StartRule4Ambient();
            AudioManager.instance.ResetHeartbeatLevel();

            if (HorrorTextUI.instance != null)
            {
                HorrorTextUI.instance.ShowText(introHint);
                StartCoroutine(HideIntroHintRoutine());
            }
        }

        IEnumerator HideIntroHintRoutine()
        {
            yield return new WaitForSeconds(introHintDuration);
            if (HorrorTextUI.instance != null) HorrorTextUI.instance.HideText();
        }

        /// <summary>สุ่มจุดวางตุ๊กตาจาก pool รวม (จุดทั่วไป + จุดใต้ศาลา) แบบไม่ซ้ำจุด</summary>
        void SpawnDolls()
        {
            ClearDolls();

            var pool = new List<Transform>();
            if (dollSpawnPoints     != null) pool.AddRange(dollSpawnPoints);
            if (pavilionSpawnPoints != null) pool.AddRange(pavilionSpawnPoints);
            pool.RemoveAll(p => p == null);

            if (pool.Count < dollCount)
            {
                Debug.LogError($"[Rule4] จุด spawn ตุ๊กตามีแค่ {pool.Count} จุด แต่ต้องการ {dollCount} — เพิ่มจุดใน Inspector", this);
                return;
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

            bool anyUnderPavilion = false;

            for (int i = 0; i < dollCount; i++)
            {
                Transform point = pool[i];
                var doll = Instantiate(dollPrefab, point.position, point.rotation);

                // ถ้าตุ๊กตามีมากกว่าจำนวนเสียง ค่อยวนกลับมาใช้ซ้ำ
                doll.Setup(this, inputHandler, sounds[i % sounds.Count]);
                _dolls.Add(doll);

                if (IsPavilionPoint(point)) anyUnderPavilion = true;
            }

            // มีตุ๊กตาอยู่ใต้ศาลา → ปล่อยเงาออกมาให้เห็นก่อนไปเก็บ
            if (anyUnderPavilion) StartCoroutine(PavilionShadowRoutine());
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
        /// เก็บได้ 1 ตัว → ศาลพระภูมิย้ายที่ทันที + ผีออกมาไล่ (ครั้งแรกเท่านั้น)
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

            // ช่วงป้ายบอกตำแหน่ง: เก็บตุ๊กตาแล้วผีหายไปเลย ค่อยโผล่ใหม่ตอนวางเสร็จ
            // ช่วงไล่ (ตัวที่ 3 เป็นต้นไป): ไม่ต้องทำอะไร ผีไล่อยู่แล้ว
            if (_dollsPlaced < guidedDollCount) HideGhost();

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

            if (_dollsPlaced < guidedDollCount)
            {
                // ยังอยู่ช่วงป้ายบอกตำแหน่ง → ผีโผล่ไปยืนข้างตุ๊กตาตัวถัดไป
                ShowGhostMarkerAtNextDoll();
            }
            else if (_dollsPlaced == guidedDollCount)
            {
                // วางครบ 2 ตัวแล้ว → spawn ผีตัวใหม่ที่จุดไกลจากผู้เล่น แล้วเริ่มไล่ยาว
                RespawnGhostForChase();
            }
            else
            {
                // ตัวที่ 4 เป็นต้นไป — ผีไล่อยู่แล้ว แค่เร่งความเร็วขึ้น
                if (_ghost != null) _ghost.OnDollDelivered();
            }
        }

        /// <summary>ให้ผีโผล่ไปยืนข้างตุ๊กตาที่เหลือตัวที่ใกล้ผู้เล่นที่สุด (ป้ายบอกตำแหน่ง)</summary>
        void ShowGhostMarkerAtNextDoll()
        {
            if (_ghost == null) return;

            Doll target = FindNearestRemainingDoll();
            if (target == null)
            {
                // ไม่เหลือตุ๊กตาให้ชี้แล้ว — ข้ามไปโหมดไล่เลย
                RespawnGhostForChase();
                return;
            }

            _ghost.gameObject.SetActive(true);
            _ghost.StandBeside(target.transform, ghostMarkerOffset);
        }

        /// <summary>ซ่อนผี (ตอนผู้เล่นเก็บตุ๊กตาขึ้นมือระหว่างช่วงป้ายบอกตำแหน่ง)</summary>
        void HideGhost()
        {
            if (_ghost == null) return;

            _ghost.Deactivate();               // ตัดเสียง + หยุด AI ก่อนซ่อน
            _ghost.gameObject.SetActive(false);
        }

        /// <summary>
        /// ทิ้งผีตัวเก่าแล้ว spawn ตัวใหม่ที่จุดไกลจากผู้เล่น จากนั้นเริ่มไล่ยาวจนจบกฎ
        /// เรียกตอนผู้เล่นวางตุ๊กตาตัวที่ guidedDollCount เสร็จ
        /// </summary>
        void RespawnGhostForChase()
        {
            if (_ghost != null)
            {
                _ghost.Deactivate();
                Destroy(_ghost.gameObject);
                _ghost = null;
            }

            SpawnGhost();
            if (_ghost != null) _ghost.BeginChase();
        }

        /// <summary>ตุ๊กตาที่ยังไม่ถูกเก็บและอยู่ใกล้ผู้เล่นที่สุด — ใช้เป็นเป้าหมายให้ผีนำทาง</summary>
        Doll FindNearestRemainingDoll()
        {
            Vector3 playerPos = PlayerController.Instance.transform.position;
            Doll  best     = null;
            float bestDist = float.MaxValue;

            foreach (var d in _dolls)
            {
                if (d == null || d == _carriedDoll || !d.gameObject.activeSelf) continue;

                float dist = Vector3.Distance(d.transform.position, playerPos);
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
            return best;
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

            if (HorrorTextUI.instance != null) HorrorTextUI.instance.HideText();

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
            if (HorrorTextUI.instance != null) HorrorTextUI.instance.HideText();

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

            if (_ghost != null) _ghost.Deactivate();
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;

            ClearDolls();

            foreach (var s in _shadows)
                if (s != null) Destroy(s);
            _shadows.Clear();

            if (spiritHouse != null) spiritHouse.ResetSlots();

            if (breathSystem != null) breathSystem.EndRuleCleanup();
            if (stareSystem  != null) stareSystem.EndRuleCleanup();

            AudioManager.instance.StopAllRule4Sounds();
            AudioManager.instance.ResetHeartbeatLevel();

            if (HorrorTextUI.instance != null) HorrorTextUI.instance.HideText();
        }

        void ClearDolls()
        {
            foreach (var d in _dolls)
                if (d != null) Destroy(d.gameObject);
            _dolls.Clear();
            _carriedDoll = null;
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
