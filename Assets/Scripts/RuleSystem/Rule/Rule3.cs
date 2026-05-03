using System.Collections;
using System.Collections.Generic;
using System.Linq;
using InputSystem;
using Manager;
using Player;
using RuleSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

public class Rule3 : RuleBase
{
    public static Rule3 Instance;

    // ─────────────── Input ───────────────
    [SerializeField] private InputHandler inputHandler;

    // ─────────────── Papers ───────────────
    [SerializeField] private PaperSpawner paperSpawner;

    private List<int>  correctOrder  = new List<int>();
    private int        currentIndex  = 0;
    private int        _correctCount = 0;
    private List<Paper> realPapers;
    private List<Paper> fakePapers;

    // ─────────────── Ghost ───────────────
    [Header("Ghost")]
    [SerializeField] private GameObject ghostPrefab;
    [SerializeField] private Transform  ghostSpawnPoint; // single fixed point – ghost never moves

    private GameObject ghostInstance;
    private Animator   ghostAnimator;

    // ─────────────── Ghost Look Event ───────────────
    // Animator triggers expected on the ghost Animator Controller:
    //   "TurnHead"  → plays SitTurningHead, then auto-transitions to SitStayTurnedHead (loop)
    //   "TurnBack"  → plays SitTurningHeadReversed, then auto-transitions back to sit idle
    [Header("Ghost Look Event")]
    [SerializeField] private float ghostLookIntervalMin  = 8f;
    [SerializeField] private float ghostLookIntervalMax  = 20f;
    [SerializeField] private float ghostTurnAnimDuration = 0.8f; // approx. duration of turning animation
    [SerializeField] private float ghostStareDuration    = 3f;   // how long ghost holds stare

    [Header("Ghost Warning")]
    [SerializeField] private float warningBaseDuration      = 3f;   // shrinks each time a paper is solved
    [SerializeField] private float warningDurationDecrement = 0.25f;// amount subtracted per correct paper
    [SerializeField] private float warningMinDuration       = 0.5f; // floor

    private int _ghostLookWrongCount = 0;
    private const int MaxGhostLookWrong = 3;

    // ─────────────── Vignette ───────────────
    [Header("Vignette")]
    [SerializeField] private float vignetteStartIntensity      = 0.587f; // matches Tension profile default
    [SerializeField] private float vignetteBaseIncreaseRate    = 0.003f; // intensity / second  (lower = more time)
    [SerializeField] private float vignetteRateIncreaseOnWrong = 0.002f; // added to rate on wrong answer
    [SerializeField] private float vignetteReliefOnCorrect     = 0.08f;  // subtracted on correct answer
    [SerializeField] private float vignetteGameOverThreshold   = 0.92f;

    // When vignette passes this point, smoothness + exposure both ramp toward full blackout
    [SerializeField] private float vignetteBlackoutStart     = 0.78f; // intensity where blackout effect begins
    [SerializeField] private float vignetteDefaultSmoothness = 0.2f;  // should match Tension Profile default
    [SerializeField] private float maxExposureDarkening      = -6f;   // EV at full blackout (more negative = darker)

    private float            _vignetteIntensity;
    private float            _vignetteCurrentRate;
    private Vignette         _vignette;
    private ColorAdjustments _colorAdjustments;

    // ─────────────── Breathing ───────────────
    private int _currentBreathingLevel = 0;

    // ─────────────── State guards ───────────────
    private bool _isRuleActive = false;
    private bool _isEnding     = false;

    // ══════════════════════════════════════════════════════
    #region Lifecycle

    private void Awake()
    {
        Instance = this;
    }

    public override void StartRule()
    {
        base.StartRule();
        StartCoroutine(RuleFlow());
    }

    protected override void UpdateRule()
    {
        AudioManager.instance.UpdateHeartbeat();
    }

    public override void EndRule()
    {
        _isRuleActive = false;
        _isEnding     = true;

        LightFlickerSystem.Instance.StopAmbientFlicker();
        AudioManager.instance.StopBreathing();
        AudioManager.instance.StopHeartbeat();

        // Restore vignette + exposure to tension-profile defaults
        SetVignetteValue(vignetteStartIntensity);
        if (_colorAdjustments != null)
            _colorAdjustments.postExposure.value = 0f;

        TimeManager.instance.SetTime(21, 40);
        TimeManager.instance.IsPauseTime(false);
        GameModeController.instance.BlinkToMode(GameMode.Relax);
        PlayerController.Instance.isBlockStanding = false;
        base.EndRule();
    }

    #endregion

    // ══════════════════════════════════════════════════════
    #region Rule Flow

    bool PlayerIsSitting()  => PlayerController.Instance.IsSitting();
    bool PlayerEyesOpened() => GameModeController.instance.IsEyesOpen;

    IEnumerator RuleFlow()
    {
        TimeManager.instance.IsPauseTime(true);

        // 1. Wait for player to sit down
        yield return new WaitUntil(() => PlayerIsSitting());

        // Player is not allowed to stand during Rule 3
        PlayerController.Instance.isBlockStanding = true;

        GameModeController.instance.BlinkToMode(GameMode.Tension);

        // 2. Wait for blink transition to finish
        yield return new WaitUntil(() => PlayerEyesOpened());

        // 3. Begin gameplay
        StartGameplay();
    }

    void StartGameplay()
    {
        _isRuleActive        = true;
        _isEnding            = false;
        currentIndex         = 0;
        _correctCount        = 0;
        _ghostLookWrongCount = 0;

        // ── Papers ──
        var result   = paperSpawner.SpawnPapers();
        realPapers   = result.realPapers;
        fakePapers   = result.fakePapers;
        correctOrder = realPapers.Select(p => p.number).OrderBy(n => n).ToList();

        // ── Ghost (single spawn point, no movement) ──
        ghostInstance = Instantiate(ghostPrefab, ghostSpawnPoint.position, ghostSpawnPoint.rotation);
        ghostAnimator = ghostInstance.GetComponentInChildren<Animator>(); // Animator is on child GhostWithAnimation

        // ── Vignette: start from the profile's current value ──
        _vignetteCurrentRate = vignetteBaseIncreaseRate;
        CacheVignetteReference();
        _vignetteIntensity = (_vignette != null)
            ? _vignette.intensity.value
            : vignetteStartIntensity;

        // ── Breathing: level 1 (slow) ──
        _currentBreathingLevel = 1;
        AudioManager.instance.StartBreathing();
        AudioManager.instance.SetBreathingLevel(1);

        // ── Light flicker: ambient only, papers shuffle – ghost stays put ──
        LightFlickerSystem.Instance.StartAmbientFlicker();

        StartCoroutine(VignetteIncreaseRoutine());
        StartCoroutine(GhostLookEventRoutine());
    }

    #endregion

    // ══════════════════════════════════════════════════════
    #region Vignette

    void CacheVignetteReference()
    {
        var profile = GameModeController.instance.globalVolume.profile;
        profile.TryGet(out _vignette);
        profile.TryGet(out _colorAdjustments); // requires ColorAdjustments override in Tension Profile
    }

    void SetVignetteValue(float intensity)
    {
        if (_vignette == null) CacheVignetteReference();
        if (_vignette == null) return;

        // ── Vignette intensity: darkens edges ──
        _vignette.intensity.value = Mathf.Clamp01(intensity);

        // ── When approaching game-over: ramp smoothness + exposure together for full blackout ──
        if (intensity >= vignetteBlackoutStart)
        {
            float t = Mathf.InverseLerp(vignetteBlackoutStart, vignetteGameOverThreshold, intensity);

            // Smoothness → 1 makes vignette cover the whole screen
            _vignette.smoothness.value = Mathf.Lerp(vignetteDefaultSmoothness, 1f, t);

            // Exposure darkens the centre that vignette alone can't reach
            if (_colorAdjustments != null)
                _colorAdjustments.postExposure.value = Mathf.Lerp(0f, maxExposureDarkening, t);
        }
        else
        {
            _vignette.smoothness.value = vignetteDefaultSmoothness;
            if (_colorAdjustments != null)
                _colorAdjustments.postExposure.value = 0f;
        }
    }

    IEnumerator VignetteIncreaseRoutine()
    {
        while (_isRuleActive)
        {
            _vignetteIntensity += _vignetteCurrentRate * Time.deltaTime;
            _vignetteIntensity  = Mathf.Clamp01(_vignetteIntensity);
            SetVignetteValue(_vignetteIntensity);
            UpdateBreathingLevel();

            if (_vignetteIntensity >= vignetteGameOverThreshold)
            {
                TriggerGameOver();
                yield break;
            }

            yield return null;
        }
    }

    void UpdateBreathingLevel()
    {
        int newLevel;
        if      (_vignetteIntensity < 0.25f) newLevel = 1; // slow
        else if (_vignetteIntensity < 0.45f) newLevel = 2; // medium
        else                                 newLevel = 3; // struggling / critical

        if (newLevel != _currentBreathingLevel)
        {
            _currentBreathingLevel = newLevel;
            AudioManager.instance.SetBreathingLevel(newLevel);
        }
    }

    #endregion

    // ══════════════════════════════════════════════════════
    #region Ghost Look Event

    IEnumerator GhostLookEventRoutine()
    {
        // Initial grace period before first look event
        yield return new WaitForSeconds(Random.Range(10f, 20f));

        while (_isRuleActive)
        {
            // ── Step 1: Warning sound (bone / neck cracking) ──
            float warningDuration = Mathf.Max(
                warningMinDuration,
                warningBaseDuration - (_correctCount * warningDurationDecrement)
            );
            AudioManager.instance.PlayGhostWarning();
            yield return new WaitForSeconds(warningDuration);
            if (!_isRuleActive) yield break;

            // ── Step 2: Ghost starts turning head (SitTurningHead) ──
            if (ghostAnimator != null)
                ghostAnimator.SetTrigger("TurnHead");

            yield return new WaitForSeconds(ghostTurnAnimDuration);
            if (!_isRuleActive) yield break;

            // ── Step 3: Ghost stares (SitStayTurnedHead auto-transitions from SitTurningHead)
            //            Player must NOT move the mouse ──
            bool  playerMoved  = false;
            float stareElapsed = 0f;

            while (stareElapsed < ghostStareDuration && _isRuleActive)
            {
                stareElapsed += Time.deltaTime;

                if (!playerMoved && IsPlayerMovingMouse())
                {
                    playerMoved = true;
                    OnPlayerMovedDuringLook();
                    if (_isEnding) yield break;
                }

                yield return null;
            }

            if (!_isRuleActive) yield break;

            // ── Step 4: Ghost turns back (SitTurningHeadReversed) ──
            if (ghostAnimator != null)
                ghostAnimator.SetTrigger("TurnBack");

            yield return new WaitForSeconds(ghostTurnAnimDuration);

            // ── Step 5: Random wait before next look event ──
            yield return new WaitForSeconds(Random.Range(ghostLookIntervalMin, ghostLookIntervalMax));
        }
    }

    /// <summary>Uses the New Input System via InputHandler to detect mouse / look movement.</summary>
    bool IsPlayerMovingMouse()
    {
        // Threshold in pixels (raw delta) – adjust if too sensitive / not sensitive enough
        return inputHandler.GetLookInput().sqrMagnitude > 0.01f;
    }

    void OnPlayerMovedDuringLook()
    {
        _ghostLookWrongCount++;

        // Heartbeat pulse: 1 = Slow, 2 = Fast, 3 = Critical
        int heartLevel = Mathf.Clamp(_ghostLookWrongCount, 1, 3);
        AudioManager.instance.SetHeartbeatLevel(heartLevel);
        StartCoroutine(HeartbeatPulseRoutine());

        Debug.Log($"[Rule3] Caught moving! Ghost look wrong: {_ghostLookWrongCount}/{MaxGhostLookWrong}");

        if (_ghostLookWrongCount >= MaxGhostLookWrong)
            TriggerGameOver();
    }

    IEnumerator HeartbeatPulseRoutine()
    {
        // Let heartbeat play for a few seconds then fade back to zero
        yield return new WaitForSeconds(3f);
        AudioManager.instance.ResetHeartbeatLevel();
    }

    #endregion

    // ══════════════════════════════════════════════════════
    #region Answer Handling

    /// <summary>Called by Paper.cs – returns true if the selected number is the next correct one.</summary>
    public bool CheckAnswer(int number)
    {
        if (currentIndex >= correctOrder.Count) return false;
        return number == correctOrder[currentIndex];
    }

    /// <summary>Called by Paper.cs when the player picks the correct paper.</summary>
    public void OnPaperSelected(Paper selectedPaper)
    {
        if (currentIndex >= correctOrder.Count) return;

        currentIndex++;
        _correctCount++;
        realPapers.Remove(selectedPaper);

        // Vignette relief – reward for correct answer
        _vignetteIntensity = Mathf.Max(
            vignetteStartIntensity,
            _vignetteIntensity - vignetteReliefOnCorrect
        );
        SetVignetteValue(_vignetteIntensity);

        // Impact flicker for paper shuffle – ghost does NOT move
        LightFlickerSystem.Instance.PlayImpactFlicker();

        bool isLast = currentIndex >= correctOrder.Count;
        StartCoroutine(OnCorrectRoutine(selectedPaper, isLast));
    }

    IEnumerator OnCorrectRoutine(Paper paper, bool isLast)
    {
        yield return new WaitForSeconds(0.3f); // wait for flicker

        // Destroy the correctly answered paper
        if (paper != null) Destroy(paper.gameObject);

        if (isLast)
        {
            yield return new WaitForSeconds(1.5f);
            CleanupAndEnd(isGameOver: false);
        }
        else
        {
            // Shuffle remaining papers; ghost stays at its spawn point
            StartCoroutine(paperSpawner.ShuffleRoutine(realPapers, fakePapers));
        }
    }

    /// <summary>Called by Paper.cs when the player picks the wrong paper.
    /// Note: Paper.cs must NOT destroy the paper on a wrong pick.</summary>
    public void OnWrong()
    {
        _vignetteCurrentRate += vignetteRateIncreaseOnWrong;
        Debug.Log($"[Rule3] Wrong answer! Vignette rate → {_vignetteCurrentRate:F4}/s");
    }

    #endregion

    // ══════════════════════════════════════════════════════
    #region Game Over

    void TriggerGameOver()
    {
        if (_isEnding) return; // already handled
        _isEnding     = true;
        _isRuleActive = false;
        StartCoroutine(GameOverRoutine());
    }

    IEnumerator GameOverRoutine()
    {
        Debug.Log("[Rule3] GAME OVER");
        // TODO: Call your GameManager / game-over screen here
        // e.g. GameManager.instance.TriggerGameOver();
        yield return new WaitForSeconds(1.5f);
        CleanupAndEnd(isGameOver: true);
    }

    #endregion

    // ══════════════════════════════════════════════════════
    #region Cleanup

    void CleanupAndEnd(bool isGameOver)
    {
        _isRuleActive = false;

        if (ghostInstance != null) Destroy(ghostInstance);

        if (realPapers != null)
        {
            foreach (var p in realPapers) if (p != null) Destroy(p.gameObject);
            realPapers.Clear();
        }
        if (fakePapers != null)
        {
            foreach (var p in fakePapers) if (p != null) Destroy(p.gameObject);
            fakePapers.Clear();
        }

        if (!isGameOver)
            EndRule();
        // If game over, let GameManager handle scene / flow instead of calling EndRule normally
    }

    #endregion
}
