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

        [Tooltip("Seconds after the rule starts, with the player never grabbing the thread, before the ghost starts walking towards them.")]
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

        [Tooltip("Ticked: walking while the ching rings kills the player. Unticked would only bring the ghost one step closer, which is not implemented yet.")]
        [SerializeField] private bool chingViolationKills = true;

        // ── Ghost voice ─────────────────────────────────────────────────
        [Header("Ghost Voice (the whispered \"ching chap\")")]
        [SerializeField] private float voiceMinGap = 8f;
        [SerializeField] private float voiceMaxGap = 18f;
        [Range(0f, 1f)]
        [SerializeField] private float voiceChance = 0.7f;

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
        private float _voiceTimer;
        private float _releaseTimer;
        private float _notGrabbedTimer;
        private int   _heartLevel;

        /// <summary>จำนวนระฆังที่ดังไปแล้วในรอบนี้ (debug / UI)</summary>
        public int BellCount => _bellCount;

        /// <summary>จำนวนระฆังที่ต้องได้ยินพอดีถึงจะนั่งจบกฎได้</summary>
        public int RequiredBells => requiredBells;

        public bool IsChingActive => _chingActive;

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

            UpdateGhostVoice(holding);
            UpdateReleaseHeartbeat(holding);
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
            _voiceTimer     = Random.Range(voiceMinGap, voiceMaxGap);
            _releaseTimer   = 0f;
            _notGrabbedTimer = 0f;
            _heartLevel     = 0;

            PlayerController.Instance.isBlockStanding = false;
            TimeManager.instance.IsPauseTime(true);
            AudioManager.instance.ResetHeartbeatLevel();

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

            // นั่งแล้ว
            if (_bellCount == requiredBells)
            {
                StartCoroutine(CompleteRoutine());
            }
            else
            {
                Debug.Log($"[Rule5] นั่งตอนระฆัง {_bellCount}/{requiredBells} — ผิดเงื่อนไข → jumpscare", this);
                StartJumpscareDeath();
            }
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
                    if (chingViolationKills && _chingViolation > chingMoveGrace)
                    {
                        Debug.Log("[Rule5] เดินตอนฉิ่งดัง → ตาย", this);
                        StopChing();
                        StartJumpscareDeath();
                        return;
                    }
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
            _chingActive    = true;
            AudioManager.instance.StartRule5Ching();
        }

        void StopChing()
        {
            if (!_chingActive) return;
            _chingActive = false;
            AudioManager.instance.StopRule5Ching();
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

        /// <summary>ปล่อยมือแล้วไม่กลับมาจับ → หัวใจเต้นแรงขึ้นเรื่อยๆ → หัวใจวาย</summary>
        void UpdateReleaseHeartbeat(bool holding)
        {
            if (holding || walker == null || !walker.EverGrabbed)
            {
                if (_heartLevel != 0) { _heartLevel = 0; AudioManager.instance.ResetHeartbeatLevel(); }
                _releaseTimer = 0f;
                return;
            }

            _releaseTimer += Time.deltaTime;

            int level = _releaseTimer < heartSlowUntil ? 1
                      : _releaseTimer < heartFastUntil ? 2
                      : 3;

            if (level != _heartLevel)
            {
                _heartLevel = level;
                AudioManager.instance.SetHeartbeatLevel(level);
            }

            if (_releaseTimer >= heartCriticalUntil)
            {
                Debug.Log("[Rule5] ปล่อยสายเกินเวลา → หัวใจวาย", this);
                StartHeartAttackDeath();
            }
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

            // ระฆังครบแล้วปล่อย → ผีไล่ทันที (ผู้เล่นต้องวิ่งไปศาลา)
            if (_bellCount >= requiredBells) _ghost.BeginChase();
            else                             _ghost.EnterApproach();
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

            // ต้องดึงจุด spawn เข้า NavMesh ก่อน Instantiate — NavMeshAgent ที่เกิดนอก NavMesh
            // จะสร้างไม่สำเร็จ ("Failed to create agent because there is no valid NavMesh")
            // แล้วทุก property ของมันจะ throw รัวทุกเฟรมหลังจากนั้น
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
            AudioManager.instance.ResetHeartbeatLevel();
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
            AudioManager.instance.StopRule5Background();
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
            AudioManager.instance.StopRule5Background();
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
            AudioManager.instance.ResetHeartbeatLevel();

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

            AudioManager.instance.StopAllRule5Sounds();
            AudioManager.instance.ResetHeartbeatLevel();

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
        }

        #endregion
    }
}
