using System.Collections;
using Manager;
using Player;
using Rule5;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RuleSystem.Rule
{
    /// <summary>
    /// กฎข้อ 5 — "ปิดตาแล้วเดินตามสายสิญจน์ ฟังระฆังให้ครบ 3 ครั้ง"
    ///
    /// Flow:
    ///   1. ผู้เล่นนั่ง → blink เข้า Tension → สายสิญจน์ + ผ้าแดง + ผีโผล่ที่จุดเริ่มสาย, มือผีบังจอ
    ///   2. เดินไปกด E ที่ผ้าแดงจุดเริ่ม → ตัวถูกล็อกกับสาย เดินได้แค่ W มองได้ 180°
    ///      ผีเดินเคียงข้างไปด้วย
    ///   3. ระหว่างเดิน (จับอยู่ + ขยับ) ระฆังจะดังเป็นระยะ — ดังได้เกิน 3 ผู้เล่นต้องนับเอง
    ///      เสียงฉิ่งดังเมื่อไรต้องหยุดเดินจนกว่าจะเงียบ (เดินต่อ = ตาย)
    ///      เสียงผีกระซิบ "ฉิ่ง ฉับ" สุ่มระหว่างจับสาย
    ///   4. ปล่อยมือ (E) ได้ แต่ต้องกลับมาจับที่ผ้าแดงจุดเดิม
    ///      ปล่อยแล้วไม่จับ: หัวใจเต้น slow 0-5 / fast 5-10 / critical 10-15 / 15+ หัวใจวายตาย
    ///      ผีค่อยๆ เดินเข้าหา ใกล้เกิน = ตาย
    ///   5. ระฆังครบ 3 → ปล่อยมือ → ผีวิ่งไล่ทันที → วิ่งไปนั่งที่ศาลา
    ///      นั่งตอนระฆัง == 3 พอดี → จบกฎ / ไม่ใช่ 3 → ผีโผล่หน้าพุ่งเข้ามา (jumpscare) ตาย
    ///   สิ่งกีดขวางบนสาย (ThreadObstacle) บังคับให้ต้องปล่อยมือ / กด E ค้างแก้สายพัน
    /// </summary>
    public class Rule5 : RuleBase
    {
        // ── References ──────────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private SacredThreadWalker walker;

        [Tooltip("มือผีที่บังหน้าจอ (UI/GameObject ที่ปิดไว้) — เปิดตลอดช่วงเล่นกฎ")]
        [SerializeField] private GameObject ghostHandOverlay;

        // ── Ghost ───────────────────────────────────────────────────────
        [Header("Ghost")]
        [SerializeField] private Rule5Ghost ghostPrefab;

        [Tooltip("จุดยืนรอตอนเริ่มกฎ — เว้นว่าง = ยืนอีกฝั่งของสายตรงจุดเริ่ม")]
        [SerializeField] private Transform ghostWaitPoint;

        [Tooltip("ถ้าเว้น ghostWaitPoint: ยืนห่างจากสายไปอีกฝั่งกี่เมตร")]
        [SerializeField] private float ghostWaitSideOffset = 1.5f;

        [Tooltip("เริ่มกฎแล้วผู้เล่นไม่ยอมมาจับสายภายในกี่วินาที ผีจะเริ่มเดินเข้าหา")]
        [SerializeField] private float approachIfNotGrabbedAfter = 25f;

        // ── Bells ───────────────────────────────────────────────────────
        [Header("Bells (ระฆัง)")]
        [Tooltip("ต้องได้ยินกี่ครั้งพอดีถึงจะไปนั่งจบกฎได้")]
        [SerializeField] private int requiredBells = 3;

        [Tooltip("เว้นช่วงระหว่างระฆังแต่ละครั้ง (วินาทีที่ 'เดินอยู่' — ปล่อยมือ/หยุดเดิน เวลาไม่นับ)")]
        [SerializeField] private float bellMinGap = 10f;
        [SerializeField] private float bellMaxGap = 20f;

        [Tooltip("ระฆังครั้งแรกดังหลังเริ่มเดินกี่วินาที")]
        [SerializeField] private float firstBellDelay = 8f;

        [Tooltip("นับเวลาระฆังเฉพาะตอนขยับ (ติ๊ก) หรือแค่จับสายก็นับ (ไม่ติ๊ก)")]
        [SerializeField] private bool bellsRequireWalking = true;

        // ── Ching ───────────────────────────────────────────────────────
        [Header("Ching (ฉิ่ง — ต้องหยุดเดิน)")]
        [SerializeField] private float chingMinGap = 12f;
        [SerializeField] private float chingMaxGap = 25f;

        [Tooltip("ฉิ่งดังนานกี่วินาที (สุ่มในช่วง)")]
        [SerializeField] private float chingMinDuration = 3f;
        [SerializeField] private float chingMaxDuration = 6f;

        [Tooltip("เดินระหว่างฉิ่งดังได้นานสุดกี่วินาที (รวม) ก่อนโดนลงโทษ — เผื่อเวลาตอบสนอง")]
        [SerializeField] private float chingMoveGrace = 0.5f;

        [Tooltip("เดินตอนฉิ่งดัง → ตาย (ติ๊ก) / ไม่ติ๊ก = แค่ผีจะเข้ามาประชิด 1 ก้าว (ยังไม่ทำ — ตายอย่างเดียวไปก่อน)")]
        [SerializeField] private bool chingViolationKills = true;

        // ── Ghost voice ─────────────────────────────────────────────────
        [Header("Ghost Voice (ฉิ่ง ฉับ)")]
        [SerializeField] private float voiceMinGap = 8f;
        [SerializeField] private float voiceMaxGap = 18f;
        [Range(0f, 1f)]
        [SerializeField] private float voiceChance = 0.7f;

        // ── Heartbeat after release ─────────────────────────────────────
        [Header("Heartbeat (ปล่อยมือแล้วไม่กลับมาจับ)")]
        [SerializeField] private float heartSlowUntil     = 5f;
        [SerializeField] private float heartFastUntil     = 10f;
        [SerializeField] private float heartCriticalUntil = 15f;   // เกินนี้ = หัวใจวาย

        // ── Hints ───────────────────────────────────────────────────────
        [Header("Hints")]
        [TextArea(2, 4)]
        [SerializeField] private string introHint = "จับสายสิญจน์แล้วเดินไป…\nฟังเสียงระฆังให้ครบ {0} ครั้ง แล้วกลับมานั่ง";
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

        public bool IsChingActive => _chingActive;

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
