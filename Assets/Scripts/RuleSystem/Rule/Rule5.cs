using System.Collections;
using Manager;
using Player;
using Rule5;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RuleSystem.Rule
{
    
    public class Rule5 : RuleBase
    {
        // ── References ──────────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private SacredThreadWalker walker;

        [Tooltip("Reveals obstacles one at a time while the player cannot see them. Optional: leave empty for a run with no obstacles.")]
        [SerializeField] private ThreadObstacleSpawner obstacleSpawner;

        [Tooltip("Ghost hand covering the screen (a UI object or GameObject left disabled). Enabled for the whole rule.")]
        [SerializeField] private GameObject ghostHandOverlay;

        // ── Ghost ───────────────────────────────────────────────────────
        [Header("Ghost")]
        [SerializeField] private Rule5Ghost ghostPrefab;

        [Tooltip("Where the ghost waits when the rule starts. Leave empty to have it stand across the thread from the start point.")]
        [SerializeField] private Transform ghostWaitPoint;

        [Tooltip("Used when ghostWaitPoint is empty: how far across the thread the ghost stands, in metres.")]
        [SerializeField] private float ghostWaitSideOffset = 1.5f;

        [Tooltip("Seconds after the rule starts, with the player never once grabbing the thread, before the ghost starts walking towards them. " +
                 "This is the only case where the ghost walks over on its own: once the thread has been held, letting go leaves the ghost standing " +
                 "where it is until the player prays.")]
        [SerializeField] private float approachIfNotGrabbedAfter = 25f;

        // ── Bells ───────────────────────────────────────────────────────
        [Header("Bells")]
        [Tooltip("Exact number of bells the player must hear before sitting down finishes the rule.")]
        [SerializeField] private int requiredBells = 3;

        [Tooltip("Gap between bells, in seconds of walking. Time spent stopped or off the thread does not count.")]
        [SerializeField] private float bellMinGap = 10f;
        [SerializeField] private float bellMaxGap = 20f;

        [Tooltip("Seconds of walking before the first bell rings.")]
        [SerializeField] private float firstBellDelay = 8f;

        [Tooltip("Ticked: bell time only counts while actually moving. Unticked: holding the thread is enough.")]
        [SerializeField] private bool bellsRequireWalking = true;

        // ── Ching ───────────────────────────────────────────────────────
        [Header("Ching (the player must stop walking)")]
        [SerializeField] private float chingMinGap = 12f;
        [SerializeField] private float chingMaxGap = 25f;

        [Tooltip("How long the ching keeps ringing, in seconds, picked at random in this range.")]
        [SerializeField] private float chingMinDuration = 3f;
        [SerializeField] private float chingMaxDuration = 6f;

        [Tooltip("Total seconds of walking allowed while the ching rings before it counts as a violation. Reaction-time grace.")]
        [SerializeField] private float chingMoveGrace = 0.5f;

        [Tooltip("Text shown the first time the player keeps walking through a ching. Leave empty for no hint. " +
                 "Walking does not kill outright: the ghost behind closes in for as long as it lasts, and kills once it is close enough " +
                 "(tune the distances on the ghost prefab).")]
        [TextArea(1, 3)]
        [SerializeField] private string chingWarnHint = "หยุดเดิน… มีอะไรขยับเข้ามาใกล้ข้างหลัง";

        // ── Ghost voice ─────────────────────────────────────────────────
        [Header("Ghost Voice (the whispered \"ching chap\")")]
        [SerializeField] private float voiceMinGap = 8f;
        [SerializeField] private float voiceMaxGap = 18f;
        [Range(0f, 1f)]
        [SerializeField] private float voiceChance = 0.7f;

        // ── Pray ────────────────────────────────────────────────────────
        [Header("Pray (Spacebar = \"I am going back to my seat\")")]
        [Tooltip("The player must pray, while still holding the thread, before sitting down counts as finishing the rule. " +
                 "Praying is what sets the ghost chasing once the thread is let go, and it stops the heart attack countdown. " +
                 "Sitting down without praying is a loss even with every bell counted.")]
        [TextArea(1, 3)]
        [SerializeField] private string prayHint = "…ขอลาแล้วนะ";

        [Tooltip("Shown when the player presses the pray key after letting go of the thread, which is too late. Leave empty for no hint.")]
        [TextArea(1, 3)]
        [SerializeField] private string prayNotHoldingHint = "ต้องจับสายสิญจน์อยู่ถึงจะพนมมือไหว้ได้";

        [Tooltip("Animator on the player that plays the praying animation. Optional, nothing is wired up yet.")]
        [SerializeField] private Animator prayAnimator;

        [Tooltip("Trigger fired on that animator when the player prays.")]
        [SerializeField] private string prayTrigger = "pray";

        // ── Heartbeat after release ─────────────────────────────────────
        [Header("Heartbeat (let go and never come back)")]
        [SerializeField] private float heartSlowUntil     = 5f;
        [SerializeField] private float heartFastUntil     = 10f;
        [SerializeField] private float heartCriticalUntil = 15f;   // เกินนี้ = หัวใจวาย

        // ── Hints ───────────────────────────────────────────────────────
        [Header("Hints")]
        [TextArea(2, 4)]
        [SerializeField] private string introHint = "";
        [SerializeField] private float  introHintDuration = 6f;

        // ── Game Over ───────────────────────────────────────────────────
        [Header("Game Over Reset")]
        [SerializeField] private int gameOverResetHour   = 22;
        [SerializeField] private int gameOverResetMinute = 55;

        // ── Runtime ─────────────────────────────────────────────────────
        private Rule5Ghost _ghost;
        private bool  _gameplayActive;
        private bool  _isEnding;
        private bool  _completed;
        private bool  _leftSeat;

        private int   _bellCount;
        private float _bellTimer;
        private float _chingTimer;
        private float _chingRemaining;
        private float _chingViolation;
        private bool  _chingActive;
        private bool  _chingWarned;
        private float _voiceTimer;
        private float _releaseTimer;
        private float _notGrabbedTimer;
        private int   _heartLevel;
        private bool  _prayed;

        /// <summary>จำนวนระฆังที่ดังไปแล้วในรอบนี้ (debug / UI)</summary>
        public int BellCount => _bellCount;

        /// <summary>จำนวนระฆังที่ต้องได้ยินพอดีถึงจะนั่งจบกฎได้</summary>
        public int RequiredBells => requiredBells;

        public bool IsChingActive => _chingActive;

        /// <summary>true = สวดมนต์แล้ว (กด Space) — ผีไล่อยู่ และนั่งลงถึงจะนับว่าชนะ</summary>
        public bool HasPrayed => _prayed;

        /// <summary>true = กฎกำลังเล่นอยู่จริง (ผ่าน blink เข้ามาแล้ว ยังไม่ตาย/ยังไม่จบ)</summary>
        public bool IsGameplayActive => _gameplayActive;

        /// <summary>ผีของรอบนี้ (null ถ้ายังไม่ spawn) — ใช้โดย Rule5DevSkip</summary>
        public Rule5Ghost ActiveGhost => _ghost;

        /// <summary>ตัวเดินสาย — ใช้โดย Rule5DevSkip อ่านสถานะจับ/ระยะ</summary>
        public SacredThreadWalker Walker => walker;

        /// <summary>ตัวปล่อยสิ่งกีดขวาง (null ถ้าไม่ได้ใส่) — ใช้โดย Rule5DevSkip</summary>
        public ThreadObstacleSpawner ObstacleSpawner => obstacleSpawner;

        // ── Debug hooks (Rule5DevSkip เท่านั้น) ──────────────────────────

        /// <summary>DEBUG: ทำให้ระฆังดังทันที 1 ครั้ง โดยไม่ต้องรอเวลา</summary>
        public void DebugRingBell()
        {
            if (!_gameplayActive) return;
            _bellTimer = Random.Range(bellMinGap, bellMaxGap);
            _bellCount++;
            AudioManager.instance.PlayRule5Bell();
            Debug.Log($"[Rule5] (debug) ระฆังครั้งที่ {_bellCount}", this);
        }

        /// <summary>DEBUG: เปิด/ปิดเสียงฉิ่งด้วยมือ (เปิดแล้วค้างไว้จนกดปิด)</summary>
        public void DebugToggleChing()
        {
            if (!_gameplayActive) return;

            if (_chingActive) { StopChing(); return; }

            _chingRemaining = 9999f;
            _chingViolation = 0f;
            _chingWarned    = false;
            _chingActive    = true;
            AudioManager.instance.StartRule5Ching();
        }

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
            yield return null;
            yield return new WaitUntil(() => !PlayerEyesOpened());
            yield return new WaitUntil(PlayerEyesOpened);

            StartGameplay();
        }

        protected override void UpdateRule()
        {
            if (!_gameplayActive) return;

            // กฎจบด้วยการนั่งเท่านั้น — ยึดนาฬิกาไว้ (BlinkRoutine ปล่อยได้ทุกเมื่อ)
            if (!TimeManager.instance.IsPaused) TimeManager.instance.IsPauseTime(true);

            AudioManager.instance.UpdateHeartbeat();

            UpdateSeat();
            if (!_gameplayActive) return;

            bool holding = walker != null && walker.IsHolding;
            bool walking = walker != null && walker.IsWalking;

            UpdateBells(holding, walking);
            UpdateChing(holding, walking);
            if (!_gameplayActive) return;

            UpdatePray(holding);
            UpdateGhostVoice(holding);
            UpdateHeartbeat(holding);
            UpdateNotGrabbed(holding);
        }

        public override void EndRule()
        {
            if (_isEnding) return;

            if (_gameplayActive && !_completed)
            {
                Debug.LogWarning("[Rule5] ถูกสั่ง EndRule ทั้งที่ยังเล่นอยู่ — เมิน (กฎจบเมื่อกลับมานั่งหลังระฆังครบเท่านั้น)", this);
                TimeManager.instance.IsPauseTime(true);
                return;
            }

            _isEnding       = true;
            _gameplayActive = false;

            CleanupGameplay();

            PlayerController.Instance.isBlockStanding = false;
            TimeManager.instance.IsPauseTime(false);
            GameModeController.instance.BlinkToMode(GameMode.Relax);

            base.EndRule();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Gameplay
        // ════════════════════════════════════════════════════════════════

        void StartGameplay()
        {
            _isEnding       = false;
            _completed      = false;
            _gameplayActive = true;
            _leftSeat       = false;
            _bellCount      = 0;
            _bellTimer      = firstBellDelay;
            _chingTimer     = Random.Range(chingMinGap, chingMaxGap);
            _chingActive    = false;
            _chingViolation = 0f;
            _chingWarned    = false;
            _voiceTimer     = Random.Range(voiceMinGap, voiceMaxGap);
            _releaseTimer   = 0f;
            _notGrabbedTimer = 0f;
            _heartLevel     = 0;
            _prayed         = false;

            PlayerController.Instance.isBlockStanding = false;
            TimeManager.instance.IsPauseTime(true);
            AudioManager.instance.SilenceHeartbeat();

            if (walker == null)
            {
                Debug.LogError("[Rule5] ยังไม่ได้ใส่ walker (SacredThreadWalker)", this);
                return;
            }

            walker.OnGrabbed  -= HandleGrabbed;
            walker.OnReleased -= HandleReleased;
            walker.OnGrabbed  += HandleGrabbed;
            walker.OnReleased += HandleReleased;
            walker.BeginRule();
            if (obstacleSpawner != null) obstacleSpawner.BeginRule();

            SpawnGhost();

            if (ghostHandOverlay != null) ghostHandOverlay.SetActive(true);
            AudioManager.instance.StartRule5Background();

            if (PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowLine(introHint.Replace("{0}", requiredBells.ToString()), introHintDuration);
        }

        /// <summary>ลุกออกจากศาลาแล้วกลับมานั่ง = ตัดสินผล</summary>
        void UpdateSeat()
        {
            bool sitting = PlayerIsSitting();

            if (!_leftSeat)
            {
                if (!sitting) _leftSeat = true;
                return;
            }
            if (!sitting) return;

            // นั่งแล้ว — ต้องครบทั้งจำนวนระฆัง และต้องสวดมนต์ลาก่อนถึงจะนับว่าชนะ
            if (_bellCount == requiredBells && _prayed)
            {
                StartCoroutine(CompleteRoutine());
                return;
            }

            Debug.Log(_bellCount != requiredBells
                        ? $"[Rule5] นั่งตอนระฆัง {_bellCount}/{requiredBells} — ผิดเงื่อนไข → jumpscare"
                        : "[Rule5] ระฆังครบแต่ไม่ได้สวดมนต์ลา — ผิดเงื่อนไข → jumpscare", this);
            StartJumpscareDeath();
        }

        void UpdateBells(bool holding, bool walking)
        {
            if (!holding) return;
            if (bellsRequireWalking && !walking) return;

            _bellTimer -= Time.deltaTime;
            if (_bellTimer > 0f) return;

            _bellTimer = Random.Range(bellMinGap, bellMaxGap);
            _bellCount++;
            AudioManager.instance.PlayRule5Bell();
            Debug.Log($"[Rule5] ระฆังครั้งที่ {_bellCount}", this);
        }

        void UpdateChing(bool holding, bool walking)
        {
            if (_chingActive)
            {
                _chingRemaining -= Time.deltaTime;

                if (walking)
                {
                    _chingViolation += Time.deltaTime;
                    if (_chingViolation > chingMoveGrace) ApplyChingPressure();
                }

                if (_chingRemaining <= 0f) StopChing();
                return;
            }

            // ฉิ่งสุ่มดังเฉพาะตอนจับสายอยู่ (ปล่อยมืออยู่ก็ไม่มีความหมาย)
            if (!holding) return;

            _chingTimer -= Time.deltaTime;
            if (_chingTimer > 0f) return;

            _chingTimer     = Random.Range(chingMinGap, chingMaxGap);
            _chingRemaining = Random.Range(chingMinDuration, chingMaxDuration);
            _chingViolation = 0f;
            _chingWarned    = false;
            _chingActive    = true;
            AudioManager.instance.StartRule5Ching();
        }

        
        void ApplyChingPressure()
        {
            if (_ghost != null) _ghost.PressEscort(Time.deltaTime);

            if (_chingWarned) return;
            _chingWarned = true;
            Debug.Log("[Rule5] เดินตอนฉิ่งดัง → ผีข้างหลังเริ่มขยับเข้ามา", this);

            if (!string.IsNullOrEmpty(chingWarnHint) && PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowLine(chingWarnHint, 3f);

            if (_ghost != null)
                AudioManager.instance.PlayRule5GhostChingChap(_ghost.transform.position + Vector3.up * 1.5f);
        }

        void StopChing()
        {
            if (!_chingActive) return;
            _chingActive = false;
            if (AudioManager.instance != null) AudioManager.instance.StopRule5Ching();
        }

        /// <summary>
        /// กด Space = สวดมนต์ลา "โอเค ฉันจะกลับไปนั่งที่แล้ว" — กดได้เฉพาะตอนที่ยังจับสายอยู่เท่านั้น
        ///
        /// ลำดับที่ตั้งใจให้เป็น: จับสาย → นับระฆังครบ → สวดลา (Space, มือหลุดเอง) → วิ่งกลับไปนั่ง
        /// กด Space แล้วมือจะหลุดจากสายให้เอง (ไม่ต้องกด E ซ้ำ) แล้วผีถึงออกไล่ตรงจังหวะนั้น
        /// ปล่อยมือไปแล้วค่อยกด = สายไป ไม่นับ / ไม่สวดแล้วไปนั่ง = ไม่ชนะ ต่อให้ระฆังครบก็ตาม
        /// </summary>
        void UpdatePray(bool holding)
        {
            if (_prayed || walker == null || !walker.PrayPressedThisFrame) return;

            if (!holding)
            {
                // ยังไม่เคยแตะสายเลย = ยังไม่ได้เริ่มกฎด้วยซ้ำ เงียบไว้ ไม่ต้องบอกใบ้ว่ามีปุ่มนี้อยู่
                if (!walker.EverGrabbed) return;

                if (!string.IsNullOrEmpty(prayNotHoldingHint) && PlayerDialogueUI.instance != null)
                    PlayerDialogueUI.instance.ShowLine(prayNotHoldingHint, 3f);
                return;
            }

            Pray();
        }

        void Pray()
        {
            _prayed       = true;
            _releaseTimer = 0f;   // สวดแล้ว = ประกาศว่ากำลังกลับไปนั่ง ไม่ตายด้วยหัวใจวายอีก
            Debug.Log($"[Rule5] สวดมนต์ลา (ระฆัง {_bellCount}/{requiredBells}) — ผีจะออกไล่ตอนปล่อยมือ", this);

            if (prayAnimator != null && !string.IsNullOrEmpty(prayTrigger)) prayAnimator.SetTrigger(prayTrigger);
            AudioManager.instance.PlayRule5Pray();

            // สวดจบ = เลิกจับสายแล้ว ไม่ต้องกด E ซ้ำ
            // ปล่อยมือตรงนี้ทำให้ HandleReleased ทำงาน → ผีออกไล่พอดีจังหวะที่ผู้เล่นวิ่งได้
            if (walker != null)
            {
                walker.SetGrabHintEnabled(false);   // จากนี้ไปคือวิ่งกลับไปนั่ง ไม่ใช่กลับมาจับสาย
                walker.ReleaseByPlayer();
            }

            if (!string.IsNullOrEmpty(prayHint) && PlayerDialogueUI.instance != null)
                PlayerDialogueUI.instance.ShowLine(prayHint, 3f);

            // กันเหนียว เผื่อปล่อยมือไม่สำเร็จด้วยเหตุใดก็ตาม
            if (_ghost != null && _ghost.Mode != Rule5GhostMode.Chase &&
                walker != null && !walker.IsHolding) _ghost.BeginChase();
        }

        void UpdateGhostVoice(bool holding)
        {
            if (!holding) return;

            _voiceTimer -= Time.deltaTime;
            if (_voiceTimer > 0f) return;

            _voiceTimer = Random.Range(voiceMinGap, voiceMaxGap);
            if (Random.value >= voiceChance) return;

            Vector3 pos = _ghost != null ? _ghost.transform.position + Vector3.up * 1.5f
                                         : PlayerController.Instance.transform.position;
            AudioManager.instance.PlayRule5GhostChingChap(pos);
        }

        /// <summary>
        /// เสียงหัวใจ 2 ที่มา — จับสายอยู่: ตามระยะผีที่ตามหลัง / ปล่อยมือแล้วไม่กลับมาจับ: ตามเวลาจนหัวใจวาย
        /// (สองอย่างนี้เกิดพร้อมกันไม่ได้ เลยใช้ _heartLevel ตัวเดียวกัน)
        /// </summary>
        void UpdateHeartbeat(bool holding)
        {
            if (holding || walker == null || !walker.EverGrabbed)
            {
                _releaseTimer = 0f;
                SetHeartLevel(holding ? GhostCloseHeartLevel() : 0);
                return;
            }

            // สวดมนต์ลาแล้ว — หัวใจวายไม่นับอีก เหลือแค่ผีที่ไล่อยู่ข้างหลัง (เต้นแรงค้างไว้เลย)
            if (_prayed)
            {
                _releaseTimer = 0f;
                SetHeartLevel(3);
                return;
            }

            _releaseTimer += Time.deltaTime;

            SetHeartLevel(_releaseTimer < heartSlowUntil ? 1
                        : _releaseTimer < heartFastUntil ? 2
                        : 3);

            if (_releaseTimer >= heartCriticalUntil)
            {
                Debug.Log("[Rule5] ปล่อยสายเกินเวลา → หัวใจวาย", this);
                StartHeartAttackDeath();
            }
        }

        /// <summary>ผีตามหลังใกล้แค่ไหน → ระดับเสียงหัวใจ (0 = เงียบ)</summary>
        int GhostCloseHeartLevel()
        {
            if (_ghost == null || _ghost.Mode != Rule5GhostMode.Escort) return 0;

            float t = _ghost.EscortCloseness01;
            return t < 0.15f ? 0
                 : t < 0.5f  ? 1
                 : t < 0.8f  ? 2
                 : 3;
        }

        void SetHeartLevel(int level)
        {
            if (level == _heartLevel) return;
            _heartLevel = level;

            if (level <= 0) AudioManager.instance.ResetHeartbeatLevel();
            else            AudioManager.instance.SetHeartbeatLevel(level);
        }

        /// <summary>ตั้งแต่เริ่มกฎ ยังไม่ยอมมาจับสาย → ผีเริ่มเดินเข้าหา</summary>
        void UpdateNotGrabbed(bool holding)
        {
            if (holding || walker == null || walker.EverGrabbed || _ghost == null) return;
            if (_ghost.Mode != Rule5GhostMode.Wait) return;

            _notGrabbedTimer += Time.deltaTime;
            if (_notGrabbedTimer >= approachIfNotGrabbedAfter) _ghost.EnterApproach();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Walker events
        // ════════════════════════════════════════════════════════════════

        void HandleGrabbed()
        {
            if (!_gameplayActive) return;
            _releaseTimer = 0f;
            if (_ghost != null && _ghost.Mode != Rule5GhostMode.Chase) _ghost.EnterEscort();
        }

        void HandleReleased(ReleaseReason reason)
        {
            if (!_gameplayActive || reason == ReleaseReason.Forced) return;

            StopChing();   // ฉิ่งที่ค้างอยู่ไม่มีความหมายแล้ว

            if (_ghost == null) return;

            // สวดลาไว้แล้วค่อยปล่อยมือ = ผีออกไล่ตรงนี้ (จังหวะเดียวกับที่ผู้เล่นเริ่มวิ่งได้พอดี)
            // ปล่อยมือเฉยๆ โดยไม่ได้สวด = ผียืนรออยู่ตรงนั้น ไม่ตามไปไหน ต่อให้ระฆังครบแล้วก็ตาม
            // ระหว่างนั้นตัวกดดันคือเวลานับถอยหลังหัวใจวายอย่างเดียว
            if (_prayed) _ghost.BeginChase();
            else         _ghost.EnterStandby();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Ghost
        // ════════════════════════════════════════════════════════════════

        void SpawnGhost()
        {
            if (ghostPrefab == null)
            {
                Debug.LogWarning("[Rule5] ยังไม่ได้ใส่ ghostPrefab — จะไม่มีผี", this);
                return;
            }

            Vector3 pos;
            if (ghostWaitPoint != null) pos = ghostWaitPoint.position;
            else if (walker != null && walker.Thread != null)
                pos = walker.Thread.GetGroundPoint(0f) - walker.Thread.GetRight(0f) * ghostWaitSideOffset;
            else pos = PlayerController.Instance.transform.position + PlayerController.Instance.transform.forward * 3f;

            
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 12f, UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position;
            else
                Debug.LogWarning($"[Rule5] ไม่เจอ NavMesh ใกล้จุด spawn ผี ({pos}) — " +
                                 "ผีจะเดินด้วย Transform (ไม่หลบสิ่งกีดขวาง) ควร bake NavMesh คลุมเส้นสายสิญจน์", this);

            _ghost = Instantiate(ghostPrefab, pos, Quaternion.identity);
            _ghost.Init(PlayerController.Instance.transform, walker, OnGhostCaughtPlayer);
            _ghost.EnterWait(pos);
        }

        /// <summary>ผีเข้าใกล้เกินระยะ (Approach / Chase)</summary>
        public void OnGhostCaughtPlayer()
        {
            if (!_gameplayActive) return;
            StartJumpscareDeath();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Complete / Game Over
        // ════════════════════════════════════════════════════════════════

        IEnumerator CompleteRoutine()
        {
            _completed      = true;
            _gameplayActive = false;

            if (_ghost != null) _ghost.Deactivate();
            StopChing();
            if (walker != null) walker.EndRuleCleanup();
            if (obstacleSpawner != null) obstacleSpawner.EndRuleCleanup();
            if (ghostHandOverlay != null) ghostHandOverlay.SetActive(false);
            AudioManager.instance.SilenceHeartbeat();
            AudioManager.instance.PlayRule5Complete();

            yield return new WaitForSeconds(2f);
            EndRule();
        }

        void StartJumpscareDeath()
        {
            if (!_gameplayActive) return;
            _gameplayActive = false;
            StartCoroutine(JumpscareDeathRoutine());
        }

        void StartHeartAttackDeath()
        {
            if (!_gameplayActive) return;
            _gameplayActive = false;
            StartCoroutine(HeartAttackDeathRoutine());
        }

        IEnumerator JumpscareDeathRoutine()
        {
            var player = PlayerController.Instance;

            StopChing();
            if (walker != null) walker.ForceRelease(silent: true);
            AudioManager.instance.StopAllRule5Sounds();   // รวมเสียงหัวใจด้วย ไม่งั้นดังคลอ jumpscare ยาวไปถึง Relax
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            player.SetLook(false);
            player.SetMovement(false);

            if (_ghost == null) SpawnGhost();
            if (_ghost != null)
            {
                Transform cam = player.CameraPivot != null ? player.CameraPivot : player.transform;
                var routine = StartCoroutine(_ghost.JumpscareRoutine(cam));

                // บังคับมองหน้าผีตลอดที่มันพุ่งเข้ามา
                bool done = false;
                StartCoroutine(WaitThenFlag(routine, () => done = true));
                while (!done)
                {
                    player.LookAtWorldPoint(_ghost.transform.position + Vector3.up * 1.6f);
                    yield return null;
                }
            }
            else yield return new WaitForSeconds(1f);

            yield return FinishGameOver();
        }

        IEnumerator HeartAttackDeathRoutine()
        {
            StopChing();
            if (walker != null) walker.ForceRelease(silent: true);
            if (_ghost != null) _ghost.Deactivate();
            AudioManager.instance.StopAllRule5Sounds();
            AudioManager.instance.PlayRule5HeartAttack();
            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();

            yield return StartCoroutine(PlayerController.Instance.PlayDeathFallRoutine());
            yield return new WaitForSeconds(0.5f);

            yield return FinishGameOver();
        }

        IEnumerator WaitThenFlag(Coroutine c, System.Action onDone)
        {
            yield return c;
            onDone?.Invoke();
        }

        /// <summary>ส่วนท้ายของการตายทุกแบบ — เคลียร์ / blink กลับ / rewind เวลาให้ RuleManager เรียกกฎใหม่</summary>
        IEnumerator FinishGameOver()
        {
            CleanupGameplay();

            var player = PlayerController.Instance;
            player.SetMovement(true);
            player.isBlockStanding = false;
            AudioManager.instance.SilenceHeartbeat();

            GameModeController.instance.DirectBlinkToMode(
                GameMode.Relax,
                onEyesClosed: () =>
                {
                    player.ResetCameraAfterDeath();
                    player.SetMovement(true);
                });

            yield return new WaitUntil(() => GameModeController.instance.IsEyesOpen);
            yield return null;

            TimeManager.instance.SetTime(gameOverResetHour, gameOverResetMinute);
            TimeManager.instance.IsPauseTime(false);

            _isEnding = false;
            base.EndRule();
        }

        #endregion

        // ════════════════════════════════════════════════════════════════
        #region Cleanup
        // ════════════════════════════════════════════════════════════════

        void CleanupGameplay()
        {
            StopChing();

            if (walker != null)
            {
                walker.OnGrabbed  -= HandleGrabbed;
                walker.OnReleased -= HandleReleased;
                walker.EndRuleCleanup();
            }

            if (obstacleSpawner != null) obstacleSpawner.EndRuleCleanup();

            if (_ghost != null) { _ghost.Deactivate(); Destroy(_ghost.gameObject); }
            _ghost = null;

            if (ghostHandOverlay != null) ghostHandOverlay.SetActive(false);

            // StopAllRule5Sounds ดับเสียงหัวใจให้ด้วยแล้ว (SilenceHeartbeat ข้างใน)
            if (AudioManager.instance != null) AudioManager.instance.StopAllRule5Sounds();
            _heartLevel = 0;

            var player = PlayerController.Instance;
            if (player != null) player.ClearYawLimit();

            if (PlayerDialogueUI.instance != null) PlayerDialogueUI.instance.Hide();
        }

        private void OnDisable()
        {
            if (walker != null)
            {
                walker.OnGrabbed  -= HandleGrabbed;
                walker.OnReleased -= HandleReleased;
            }

            // ถูกปิดทั้งที่ยังเล่นอยู่ (เปลี่ยน scene / โหลดเซฟ) — เสียงของกฎต้องไม่ค้างต่อไป
            if (!_gameplayActive) return;
            _gameplayActive = false;
            CleanupGameplay();
        }

        #endregion
    }
}
