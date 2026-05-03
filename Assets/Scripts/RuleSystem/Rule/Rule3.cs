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
    [SerializeField] private float vignetteStartIntensity      = 0.2f; // matches Tension profile default
    [SerializeField] private float vignetteBaseIncreaseRate    = 0.003f; // intensity / second  (lower = more time)
    [SerializeField] private float vignetteRateIncreaseOnWrong = 0.002f; // added to rate on wrong answer
    [SerializeField] private float vignetteReliefOnCorrect     = 0.08f;  // subtracted on correct answer
    [SerializeField] private float vignetteGameOverThreshold   = 0.92f;

    // When vignette passes this point, smoothness + exposure both ramp toward full blackout
    [SerializeField] private float vignetteBlackoutStart     = 0.2f; // intensity where blackout effect begins
    [SerializeField] private float vignetteDefaultSmoothness = 0.2f;  // should match Tension Profile default
    [SerializeField] private float maxExposureDarkening      = -6f;   // EV at full blackout (more negative = darker)

    private float            _vignetteIntensity;
    private float            _vignetteCurrentRate;
    private Vignette         _vignette;
    private ColorAdjustments _colorAdjustments;

    // Original profile values – cached on first use, restored on disable / end rule
    private float _origVignetteIntensity;
    private float _origVignetteSmoothness;
    private float _origPostExposure;
    private bool  _profileCached = false;

    // ─────────────── Breathing ───────────────
    private int   _currentBreathingLevel = 0;
    private float _vignetteStartValue    = 0f; // actual vignette value when gameplay begins (read from profile)

    // ─────────────── State guards ───────────────
    private bool _isRuleActive = false;
    private bool _isEnding     = false;

    // ══════════════════════════════════════════════════════
    #region Lifecycle

    private void Awake()
    {
        Instance = this;
    }

    // Runs whenever the object is disabled OR Play Mode ends in the Editor
    // Guarantees the VolumeProfile asset values are always restored
    private void OnDisable()
    {
        RestoreProfileDefaults();
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
        // Guard: prevents re-entry from RuleManager.CheckRules() → EndRule() StackOverflow loop
        if (_isEnding) return;

        _isRuleActive = false;
        _isEnding     = true;

        LightFlickerSystem.Instance.StopAmbientFlicker();
        LightFlickerSystem.Instance.OnFlickerBurst -= OnLightFlicker;
        AudioManager.instance.StopBreathing();
        AudioManager.instance.StopHeartbeat();
        AudioManager.instance.StopRule3Ambient();
        AudioManager.instance.StopRule3Intro(); // safety: in case rule ends during blink transition

        // Restore vignette + exposure to their original asset values
        RestoreProfileDefaults();

        PlayerController.Instance.isBlockStanding = false;

        // Use a coroutine to break the synchronous call chain:
        // SetTime() → RuleManager.CheckRules() → EndRule() would cause StackOverflow
        StartCoroutine(EndRuleAsync());
    }

    IEnumerator EndRuleAsync()
    {
        // Wait one frame so EndRule() has fully returned before triggering CheckRules via SetTime
        yield return null;

        TimeManager.instance.SetTime(21, 40);
        TimeManager.instance.IsPauseTime(false);
        GameModeController.instance.BlinkToMode(GameMode.Relax);
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

        // 2. Wait for blink transition to finish (eyes re-open in Tension mode)
        // GameModeController plays StartRule3Intro / StopRule3Intro internally during the transition
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

        // บันทึกค่า vignette จริงตอนเริ่ม เพื่อคำนวณ breathing level เป็น % ของ progress
        _vignetteStartValue = _vignetteIntensity;

        // ── Breathing: level 1 (slow) ──
        _currentBreathingLevel = 1;
        AudioManager.instance.StartBreathing();
        AudioManager.instance.SetBreathingLevel(1);

        // ── Rule3 ambient background ──
        AudioManager.instance.StartRule3Ambient();

        // ── Light flicker: ambient only, papers shuffle – ghost stays put ──
        LightFlickerSystem.Instance.OnFlickerBurst += OnLightFlicker;
        LightFlickerSystem.Instance.StartAmbientFlicker();

        StartCoroutine(VignetteIncreaseRoutine());
        StartCoroutine(GhostLookEventRoutine());
    }

    /// <summary>Called every time the light flickers — plays the light-bulb sound.</summary>
    void OnLightFlicker()
    {
        if (!_isRuleActive) return;
        AudioManager.instance.PlayLightBulb();
    }

    #endregion

    // ══════════════════════════════════════════════════════
    #region Vignette

    void CacheVignetteReference()
    {
        var profile = GameModeController.instance.globalVolume.profile;
        profile.TryGet(out _vignette);
        profile.TryGet(out _colorAdjustments); // requires ColorAdjustments override in Tension Profile

        // Save original values the first time so we can always restore them
        if (!_profileCached)
        {
            _origVignetteIntensity = _vignette  != null ? _vignette.intensity.value  : 0.587f;
            _origVignetteSmoothness= _vignette  != null ? _vignette.smoothness.value : 0.2f;
            _origPostExposure      = _colorAdjustments != null ? _colorAdjustments.postExposure.value : 0f;
            _profileCached = true;
        }
    }

    void RestoreProfileDefaults()
    {
        if (!_profileCached) return;
        if (_vignette != null)
        {
            _vignette.intensity.value  = _origVignetteIntensity;
            _vignette.smoothness.value = _origVignetteSmoothness;
        }
        if (_colorAdjustments != null)
            _colorAdjustments.postExposure.value = _origPostExposure;
        _profileCached = false; // allow re-cache next play session
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
        // คำนวณเป็น % ว่าไปถึงไหนแล้วจากจุดเริ่มไปยัง game over
        // ทำให้ threshold ไม่ขึ้นอยู่กับ absolute value ของ vignette
        float range    = vignetteGameOverThreshold - _vignetteStartValue;
        float progress = range > 0f
            ? (_vignetteIntensity - _vignetteStartValue) / range
            : 0f;

        int newLevel;
        if      (progress < 0.25f) newLevel = 1; // 0–25%  → หายใจช้า
        else if (progress < 0.55f) newLevel = 2; // 25–55% → หายใจกลาง
        else                       newLevel = 3; // 55%+   → หายใจเร็ว/หนัก

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

        // Heartbeat level: 1 = Slow, 2 = Fast, 3 = Critical – stays until ghost turns back
        int heartLevel = Mathf.Clamp(_ghostLookWrongCount, 1, 3);
        AudioManager.instance.SetHeartbeatLevel(heartLevel);
        // No auto-reset here — GhostLookEventRoutine resets after ghost turns back

        Debug.Log($"[Rule3] Caught moving! Ghost look wrong: {_ghostLookWrongCount}/{MaxGhostLookWrong}");

        if (_ghostLookWrongCount >= MaxGhostLookWrong)
            TriggerGameOver();
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
        Debug.Log("[Rule3] GAME OVER – starting death reset sequence");

        // Stop gameplay loops so nothing fires during the death animation
        LightFlickerSystem.Instance.StopAmbientFlicker();
        LightFlickerSystem.Instance.OnFlickerBurst -= OnLightFlicker;
        AudioManager.instance.StopBreathing();
        AudioManager.instance.StopRule3Ambient();

        // ① Death sound + camera fall play simultaneously
        AudioManager.instance.PlayRule3Death();
        yield return StartCoroutine(PlayerController.Instance.PlayDeathFallRoutine());

        // ② Short pause at the fallen position
        yield return new WaitForSeconds(0.5f);

        // ③ Destroy ghost + papers, then begin mode-switch & time reset
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
            EndRule();          // success path – normal progression
        else
            StartCoroutine(GameOverResetRoutine()); // death path – reset & retry
    }

    /// <summary>
    /// Rule 3 death reset: blink back to Relax, restore camera, rewind time to 20:40.
    /// Rule 3 is NOT marked complete so RuleManager will re-trigger it when time reaches its start time.
    /// </summary>
    IEnumerator GameOverResetRoutine()
    {
        // Restore vignette / exposure profile to its defaults
        RestoreProfileDefaults();

        // Safety-stop any remaining sounds
        AudioManager.instance.StopHeartbeat();
        AudioManager.instance.StopRule3Intro();

        // Release standing lock before the blink (safe – player is already sitting)
        PlayerController.Instance.isBlockStanding = false;

        // ④ Blink back to Relax mode — use DirectBlink to skip the faint effect
        //    (screen is already dark from death; faint would incorrectly brighten it first)
        GameModeController.instance.DirectBlinkToMode(GameMode.Relax);

        // ⑤ Wait until eyes are fully open in Relax
        yield return new WaitUntil(() => GameModeController.instance.IsEyesOpen);

        // ⑥ Snap camera back to neutral sitting orientation (removes the fall tilt)
        PlayerController.Instance.ResetCameraAfterDeath();

        // ⑦ Break the synchronous chain before SetTime fires CheckRules
        yield return null;

        // ⑧ Rewind time to 20:40 – RuleManager will restart Rule 3 when time reaches its start hour
        TimeManager.instance.SetTime(20, 40);
        TimeManager.instance.IsPauseTime(false);

        // ⑨ Reset internal guards so Rule 3 can run again cleanly
        _isEnding = false;
        base.EndRule(); // sets ruleActive = false → CheckRules can re-trigger StartRule()
    }

    #endregion
}
