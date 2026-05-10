using System.Collections;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;
    
    private EventInstance music;
    private EventInstance heartbeat;
    private EventInstance radioOpen;
    private EventInstance radioSound;
    private EventInstance radioNoise;
    private EventInstance windAmbience;
    private EventInstance switchLight;
    private EventInstance radioPray;
    private EventInstance WomanScream;
    private EventInstance jumpScare;
    private EventInstance ghostScream;
    private EventInstance breathing;    // looping breathing sfx  – set FMOD event path below
    private EventInstance ghostWarning; // one-shot neck/bone crack warning

    // ── Rule 3 exclusive sounds ──
    private EventInstance rule3HeartbeatIntro; // looping heartbeat during mode-transition (separate from the game heartbeat)
    private EventInstance rule3BuildUpTension; // build-up tension music that plays together with the intro heartbeat
    private EventInstance rule3Ambient;        // Rule3-specific ambient background (looping, plays after transition)
    private EventInstance rule3LightBulb;      // one-shot light-bulb click for each flicker burst
    private EventInstance rule3BurnPaper;      // one-shot paper burn sound
    private EventInstance rule3Death;          // one-shot death sting played when the player dies in Rule 3

    Coroutine radioCoroutine;
    
    [SerializeField] private Transform radioTransform;
    [SerializeField] private Transform[] womanScreamTransform;
    [SerializeField] public Transform player;
    public  bool isPlaying = false;

    private float angle;
    private int heartLevel = 0; // 0,1,2
    float currentHeart;
    float targetHeart;
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        music = FMODUnity.RuntimeManager.CreateInstance("event:/New Event");
        music.set3DAttributes(
            FMODUnity.RuntimeUtils.To3DAttributes(transform));
        music.start();
        music.setParameterByName("MusicIntensity", 0); 
        
        heartbeat = RuntimeManager.CreateInstance("event:/HeartBeat");
        heartbeat.start();
        heartbeat.setParameterByName("HeartLevel", 0);
        
        //ghostScream = RuntimeManager.CreateInstance("event:/GhostScream");

        // ── Rule 3 sounds ──
        // TODO: create "event:/Breathing" in FMOD with a "BreathingLevel" parameter (1 / 2 / 3)
        breathing = RuntimeManager.CreateInstance("event:/Breathing");
        breathing.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(Camera.main.transform));

        // TODO: create "event:/GhostWarning" in FMOD (bone / neck crack one-shot)
        ghostWarning = RuntimeManager.CreateInstance("event:/GhostWarning");

        // ── Rule 3 exclusive sounds ──
        // TODO: create these events in FMOD Studio:
        //   event:/Rule3HeartbeatIntro  – looping heartbeat (different timbre from event:/HeartBeat)
        //   event:/Rule3BuildUpTension  – looping build-up music for the blink transition
        //   event:/Rule3Ambient         – looping ambient background unique to Rule 3
        //   event:/Rule3LightBulb       – short one-shot light-bulb buzz/click
        rule3HeartbeatIntro = RuntimeManager.CreateInstance("event:/Rule3HeartbeatIntro");
        rule3BuildUpTension = RuntimeManager.CreateInstance("event:/Rule3BuildUpTension");
        rule3Ambient        = RuntimeManager.CreateInstance("event:/Rule3Ambient");
        rule3LightBulb      = RuntimeManager.CreateInstance("event:/Rule3LightBulb");
        rule3BurnPaper      = RuntimeManager.CreateInstance("event:/BurnPaper");
        rule3Death          = RuntimeManager.CreateInstance("event:/Rule3Death");
        
        WomanScream = RuntimeManager.CreateInstance("event:/WomanScream");
        WomanScream.set3DAttributes(RuntimeUtils.To3DAttributes(womanScreamTransform[0]));
        
        jumpScare = RuntimeManager.CreateInstance("event:/JumpScareSound");
        

        radioOpen = RuntimeManager.CreateInstance("event:/RadioOpen");
        radioOpen.set3DAttributes(RuntimeUtils.To3DAttributes(radioTransform));
        
        radioSound = RuntimeManager.CreateInstance("event:/RadioSound");
        radioSound.set3DAttributes(RuntimeUtils.To3DAttributes(radioTransform));
        
        radioNoise = RuntimeManager.CreateInstance("event:/RadioNoise");
        radioNoise.set3DAttributes(RuntimeUtils.To3DAttributes(radioTransform));
        
        radioPray = RuntimeManager.CreateInstance("event:/RadioPrayer");
        radioPray.set3DAttributes(RuntimeUtils.To3DAttributes(radioTransform));
        
        radioNoise.setVolume(0f);
        radioNoise.start();

        radioSound.setVolume(0f);
        radioSound.start();
        
        windAmbience = RuntimeManager.CreateInstance("event:/WindAmbience");
        windAmbience.start();
        windAmbience.setParameterByName("Tension", 0);
        windAmbience.set3DAttributes(
            RuntimeUtils.To3DAttributes(Camera.main.transform));
        
    }
    public void SetHeartbeat(float value)
    {
        targetHeart = value;
    }
    public void SwitchWindAmbience(float tension)
    {
        windAmbience.setParameterByName("Tension", tension);
    }
    public void PlayJumpScare()
    {
        jumpScare.start();
    }
    public void PlayJumpScareGhost()
    {
        ghostScream.start();
    }
    public void StartWomanScream()
    {
        WomanScream.start();
    }

    
   
    public void StopWomanScream()
    {
        WomanScream.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }
    public void IncreaseHeartbeatLevel()
    {
        if (heartLevel >= 3) return; //  กันเกิน

        heartLevel++;
        targetHeart = heartLevel;
        Debug.Log("Increase heartbeat to level " + targetHeart);
        
    }
    public void SetHeartbeatLevel(int level)
    {
        heartLevel = Mathf.Clamp(level, 0, 3);
        targetHeart = heartLevel;
    }

    public void DecreaseHeartbeatLevel()
    {
        if (heartLevel <= 1) return; // ไม่ต่ำกว่า 1
        heartLevel--;
        targetHeart = heartLevel;
        Debug.Log("Decrease heartbeat to level " + heartLevel);
    }
    public void StopHeartbeat()
    {
        heartbeat.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        targetHeart = 0f;
        currentHeart = 0f;
    }

    /// <summary>Set heartbeat target back to 0 without stopping the FMOD event (lets it fade naturally).</summary>
    public void ResetHeartbeatLevel()
    {
        heartLevel  = 0;
        targetHeart = 0f;
    }

    // ─────────── Breathing (Rule 3) ───────────

    /// <summary>Start the looping breathing sound.</summary>
    public void StartBreathing()
    {
        breathing.start();
    }

    /// <summary>Stop the looping breathing sound with a fade out.</summary>
    public void StopBreathing()
    {
        breathing.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    /// <summary>Set breathing intensity level: 1 = slow, 2 = medium, 3 = struggling.</summary>
    public void SetBreathingLevel(int level)
    {
        // Parameter name must match the FMOD event parameter "BreathingLevel"
        breathing.setParameterByName("BreathingLevel", (float)level);
    }

    // ─────────── Ghost Warning (Rule 3) ───────────

    /// <summary>Play the ghost-is-about-to-look warning sound (bone/neck crack). Fire-and-forget.</summary>
    public void PlayGhostWarning()
    {
        ghostWarning.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); // restart if already playing
        ghostWarning.start();
    }

    // ─────────── Rule 3 Intro (mode-transition) ───────────

    /// <summary>Start the looping heartbeat + build-up tension that play during the Tension blink transition.</summary>
    public void StartRule3Intro()
    {
        rule3HeartbeatIntro.start();
        rule3BuildUpTension.start();
    }

    /// <summary>Stop the intro heartbeat and build-up tension after the transition is done.</summary>
    public void StopRule3Intro()
    {
        rule3HeartbeatIntro.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        rule3BuildUpTension.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    // ─────────── Rule 3 Ambient ───────────

    /// <summary>Start the Rule3-specific ambient background loop.</summary>
    public void StartRule3Ambient()
    {
        rule3Ambient.start();
    }

    /// <summary>Stop the Rule3 ambient with a fade-out.</summary>
    public void StopRule3Ambient()
    {
        rule3Ambient.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    // ─────────── Rule 3 Light Bulb ───────────

    /// <summary>Play the one-shot light-bulb buzz/click sound on each flicker burst.</summary>
    public void PlayLightBulb()
    {
        rule3LightBulb.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); // cut previous if still playing
        rule3LightBulb.start();
    }

    // ─────────── Rule 3 Burn Paper ───────────

    /// <summary>Start the paper burn sound (plays for the duration of the dissolve animation).</summary>
    public void PlayBurnPaper()
    {
        rule3BurnPaper.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        rule3BurnPaper.start();
    }

    /// <summary>Stop the paper burn sound early if needed.</summary>
    public void StopBurnPaper()
    {
        rule3BurnPaper.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    // ─────────── Rule 3 Death ───────────

    /// <summary>Play the one-shot death sting when the player dies in Rule 3.</summary>
    public void PlayRule3Death()
    {
        rule3Death.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        rule3Death.start();
    }

    /// <summary>Stop the death sound early if needed (e.g. hard cut on blink).</summary>
    public void StopRule3Death()
    {
        rule3Death.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    // ─────────── Rule 2 Atmospheric Sounds ───────────
    // TODO: สร้าง FMOD events เหล่านี้:
    //   event:/Rule2/Wail              – เสียงโหยหวน (one-shot)
    //   event:/Rule2/ForestFootsteps   – เสียงคนเดินในป่า (one-shot)
    //   event:/Rule2/Whisper           – เสียงกระซิบข้างหู (one-shot)
    //   event:/Rule2/GhostLaugh        – เสียงผีหัวเราะเบาๆ (one-shot)
    //   event:/Rule2/Eerie             – เสียงหวีดๆ ambient (one-shot)
    //   event:/Rule2/DogHowl           – เสียงหมาหอนจากระยะไกล (one-shot)
    //   event:/Rule2/BusApproach       – เสียงรถประจำทางเข้ามาใกล้ (one-shot)
    //   event:/Rule2/RunningFootsteps  – เสียงคนวิ่งผ่านหลัง (one-shot)

    private Vector3 ListenerPos =>
        Camera.main != null ? Camera.main.transform.position : Vector3.zero;

    /// <summary>เสียงโหยหวน (สุ่ม event ตอน First Blackout Return)</summary>
    public void PlayRule2Wail()
        => RuntimeManager.PlayOneShot("event:/Rule2/Wail", ListenerPos);

    /// <summary>เสียงคนเดินในป่าข้างหลัง (สุ่ม event ตอน First Blackout Return)</summary>
    public void PlayRule2ForestFootsteps()
        => RuntimeManager.PlayOneShot("event:/Rule2/ForestFootsteps", ListenerPos);

    /// <summary>เสียงกระซิบข้างหู (สุ่ม event ตอน First Blackout Return)</summary>
    public void PlayRule2Whisper()
        => RuntimeManager.PlayOneShot("event:/Rule2/Whisper", ListenerPos);

    /// <summary>เสียงผีหัวเราะเบาๆ (สุ่ม sound ระหว่าง Fix Panel)</summary>
    public void PlayRule2GhostLaugh()
        => RuntimeManager.PlayOneShot("event:/Rule2/GhostLaugh", ListenerPos);

    /// <summary>เสียงหวีดๆ ambient (สุ่ม sound ระหว่าง Fix Panel)</summary>
    public void PlayRule2Eerie()
        => RuntimeManager.PlayOneShot("event:/Rule2/Eerie", ListenerPos);

    /// <summary>เสียงหมาหอนจากระยะไกล (สุ่ม sound ระหว่าง Fix Panel)</summary>
    public void PlayRule2DogHowl()
        => RuntimeManager.PlayOneShot("event:/Rule2/DogHowl", ListenerPos);

    /// <summary>เสียงรถประจำทางเข้ามาใกล้ (สุ่ม sound ระหว่าง Fix Panel)</summary>
    public void PlayRule2BusApproach()
        => RuntimeManager.PlayOneShot("event:/Rule2/BusApproach", ListenerPos);

    /// <summary>เสียงคนวิ่งผ่านหลังผู้เล่น (สุ่ม ระหว่างเดินกลับศาลา)</summary>
    public void PlayRule2RunningFootsteps()
        => RuntimeManager.PlayOneShot("event:/Rule2/RunningFootsteps", ListenerPos);

    public void UpdateHeartbeat()
    {
        currentHeart = Mathf.Lerp(currentHeart, targetHeart, Time.deltaTime * 2f);
        heartbeat.setParameterByName("HeartLevel", currentHeart);
    }
    public void PlayRadio()
    {
        if (radioCoroutine != null)
            StopCoroutine(radioCoroutine);
        StartCoroutine(PlayRadioRoutine());
    }
    public void PlayRadioPrayer(int prayIndex)  
    {
        radioPray.start();
        radioPray.setParameterByName("RadioPray", prayIndex);
        radioPray.setVolume(1f);
    }
    public void StopPlayRadioPrayer()
    {
        radioPray.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    IEnumerator PlayRadioRoutine()
    {
        radioNoise.start();
        radioNoise.release();
        radioOpen.start();
        yield return new WaitForSeconds(0.5f);
        
        yield return StartCoroutine(FadeInNoise(1f));
        
    }
    public void StopRadio()
    {
        if (radioCoroutine != null)
            StopCoroutine(radioCoroutine);
        radioOpen.start();
        radioNoise.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        radioSound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }
    IEnumerator FadeInNoise(float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float normalized = t / duration;

            radioNoise.setVolume(normalized);
            yield return null;
        }

        radioNoise.setVolume(0.7f);
    }
    public void GoToMusic()
    {
        if (radioCoroutine != null)
            StopCoroutine(radioCoroutine);

        radioCoroutine = StartCoroutine(SwitchToMusic(2f));
    }
    public void GoToNoise()
    {
        if (radioCoroutine != null)
            StopCoroutine(radioCoroutine);

        radioCoroutine = StartCoroutine(SwitchToNoise(2f));
    }
    
    IEnumerator SwitchToMusic(float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;

            float normalized = t / duration;

            // fade out noise
            radioNoise.setVolume(1f - normalized);

            // fade in music
            radioSound.setVolume(normalized);

            yield return null;
        }

        radioNoise.setVolume(0f);
        radioSound.setVolume(1f);
    }
    IEnumerator SwitchToNoise(float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;

            float normalized = t / duration;

            // fade out music
            radioSound.setVolume(1f - normalized);

            // fade in noise
            radioNoise.setVolume(normalized);

            yield return null;
        }

        radioSound.setVolume(0f);
        radioNoise.setVolume(1f);
    }
    public void FadeInMusic()
    {
        StopAllCoroutines();
        StartCoroutine(FadeMusic(1f, 2f));
    }

    public void FadeOutMusic()
    {
        StopAllCoroutines();
        StartCoroutine(FadeMusic(0f, 2f));
    }

    public void StopNoise()
    {
        radioNoise.setVolume(0f);
    }

    IEnumerator FadeMusic(float target, float duration)
    {
        music.getParameterByName("MusicIntensity", out float current);

        float t = 0;

        while (t < duration)
        {
            t += Time.deltaTime;

            float value = Mathf.Lerp(current, target, t / duration);
            music.setParameterByName("MusicIntensity", value);

            yield return null;
        }

        music.setParameterByName("MusicIntensity", target);
    }
}
