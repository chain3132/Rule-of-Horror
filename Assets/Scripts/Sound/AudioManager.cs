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

    // ── Rule 2 looping sounds (ต้องเป็น EventInstance ถึงจะ stop ได้) ──
    private EventInstance rule2AmbienceBG; // ambient background loop ของ Rule 2
    private EventInstance rule2Ambience;   // ambient loop รอง

    // ── Rule 3 exclusive sounds ──
    private EventInstance rule3HeartbeatIntro; // looping heartbeat during mode-transition (separate from the game heartbeat)
    private EventInstance rule3BuildUpTension; // build-up tension music that plays together with the intro heartbeat
    private EventInstance rule3Ambient;        // Rule3-specific ambient background (looping, plays after transition)
    private EventInstance rule3LightBulb;      // one-shot light-bulb click for each flicker burst
    private EventInstance rule3BurnPaper;      // one-shot paper burn sound
    private EventInstance rule3Death;          // one-shot death sting played when the player dies in Rule 3

    // ── Rule 4 sounds ──
    private EventInstance rule4Ambient;    // looping ambient background ของ Rule 4
    private EventInstance rule4HoldBreath; // looping "อั้นลมหายใจ" ระหว่างกลั้น
    private EventInstance rule4GhostChase; // looping เสียงผีไล่ (3D ตามตัวผี)

    // event ของ Rule 4 อาจยังไม่ถูกสร้างใน FMOD — เก็บสถานะไว้เพื่อข้ามการเล่นแทนที่จะพัง
    private bool _rule4AmbientOk, _rule4HoldBreathOk, _rule4GhostChaseOk;

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
        if (player != null)
            breathing.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(player));

        // TODO: create "event:/GhostWarning" in FMOD (bone / neck crack one-shot)
        ghostWarning = RuntimeManager.CreateInstance("event:/GhostWarning");

        // ── Rule 3 exclusive sounds ──
        // TODO: create these events in FMOD Studio:
        //   event:/Rule3HeartbeatIntro  – looping heartbeat (different timbre from event:/HeartBeat)
        //   event:/Rule3BuildUpTension  – looping build-up music for the blink transition
        //   event:/Rule3Ambient         – looping ambient background unique to Rule 3
        //   event:/Rule3LightBulb       – short one-shot light-bulb buzz/click
        // ── Rule 2 looping instances ──
        rule2AmbienceBG = RuntimeManager.CreateInstance("event:/Rule2/Rule2AmbienceBG");
        rule2Ambience   = RuntimeManager.CreateInstance("event:/Rule2/Rule2Ambience");

        rule3HeartbeatIntro = RuntimeManager.CreateInstance("event:/Rule3HeartbeatIntro");
        rule3BuildUpTension = RuntimeManager.CreateInstance("event:/Rule3BuildUpTension");
        rule3Ambient        = RuntimeManager.CreateInstance("event:/Rule3Ambient");
        rule3LightBulb      = RuntimeManager.CreateInstance("event:/Rule3LightBulb");
        rule3BurnPaper      = RuntimeManager.CreateInstance("event:/BurnPaper");
        rule3Death          = RuntimeManager.CreateInstance("event:/Rule3Death");

        // ── Rule 4 ──
        // TODO: สร้าง event เหล่านี้ใน FMOD Studio
        //   event:/Rule4/Rule4Ambient   – ambient loop ของ Rule 4
        //   event:/Rule4/HoldBreath     – เสียงอั้นลมหายใจ (loop, มี parameter "BreathStrain" 0-1)
        //   event:/Rule4/ForcedExhale   – หายใจออกแรงตอนกลั้นไม่ไหว (one-shot)
        //   event:/Rule4/GhostChase     – เสียงผีไล่ (loop, 3D ตามตัวผี)
        //   event:/Rule4/DollCry / DollLaugh / DollHum / DollCall / DollCough
        //                                              – เสียงตุ๊กตา 5 แบบ (loop, 3D, attenuation ตามระยะ)
        //   event:/Rule4/DollPickup / DollPlace        – one-shot ตอนเก็บ / วางตุ๊กตา
        _rule4AmbientOk    = TryCreate("event:/Rule4/Rule4Ambient", out rule4Ambient);
        _rule4HoldBreathOk = TryCreate("event:/Rule4/HoldBreath",   out rule4HoldBreath);
        _rule4GhostChaseOk = TryCreate("event:/Rule4/GhostChase",   out rule4GhostChase);


        WomanScream = RuntimeManager.CreateInstance("event:/WomanScream");
        if (womanScreamTransform != null && womanScreamTransform.Length > 0 && womanScreamTransform[0] != null)
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
        if (player != null)
            windAmbience.set3DAttributes(RuntimeUtils.To3DAttributes(player));
        
    }
    // ─────────── FMOD Safety ───────────

    /// <summary>
    /// สร้าง EventInstance แบบไม่พังถ้ายังไม่มี event นั้นใน FMOD
    /// CreateInstance จะ throw EventNotFoundException ซึ่งจะทำให้ Start() หยุดกลางคัน
    /// แล้วเสียงที่ประกาศต่อจากนั้นไม่ถูกสร้างเลย — ต้องกันไว้เสมอ
    /// </summary>
    private static bool TryCreate(string path, out EventInstance inst)
    {
        try
        {
            inst = RuntimeManager.CreateInstance(path);
            return true;
        }
        catch (EventNotFoundException)
        {
            Debug.LogWarning($"[FMOD] ยังไม่มี event '{path}' — ข้ามเสียงนี้ไปก่อน");
            inst = default;
            return false;
        }
    }

    /// <summary>เล่น one-shot แบบไม่พังถ้ายังไม่มี event นั้นใน FMOD</summary>
    private static void SafeOneShot(string path, Vector3 position)
    {
        try
        {
            RuntimeManager.PlayOneShot(path, position);
        }
        catch (EventNotFoundException)
        {
            Debug.LogWarning($"[FMOD] ยังไม่มี event '{path}' — ข้ามเสียงนี้ไปก่อน");
        }
    }

    /// <summary>เสียงแจ้งเตือนข้อความเข้า (ใช้ตอน FriendList unlock contact)</summary>
    public void PlayMessageNotification()
    {
        RuntimeManager.PlayOneShot("event:/Notification");
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

    /// <summary>Start the looping breathing sound.
    /// Stop ก่อน start เสมอ เพื่อกัน FMOD instance ค้างอยู่ใน fade-out state จากรอบก่อน</summary>
    public void StartBreathing()
    {
        breathing.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
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

    // ─────────── Rule 4 Sounds ───────────

    /// <summary>เริ่ม ambient loop ของ Rule 4</summary>
    public void StartRule4Ambient()
    {
        if (!_rule4AmbientOk) return;
        if (player != null)
            rule4Ambient.set3DAttributes(RuntimeUtils.To3DAttributes(player));
        rule4Ambient.start();
    }

    /// <summary>หยุดเสียงทั้งหมดของ Rule 4</summary>
    public void StopAllRule4Sounds()
    {
        if (_rule4AmbientOk)    rule4Ambient.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        if (_rule4HoldBreathOk) rule4HoldBreath.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        if (_rule4GhostChaseOk) rule4GhostChase.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    /// <summary>เริ่มเสียงอั้นลมหายใจ (loop ตลอดที่กลั้นอยู่)</summary>
    public void StartHoldBreath()
    {
        if (!_rule4HoldBreathOk) return;
        rule4HoldBreath.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        rule4HoldBreath.start();
    }

    /// <summary>ความทรมานของการกลั้น 0-1 — ยิ่งใกล้ 1 ยิ่งอั้นแรง</summary>
    public void SetBreathStrain(float strain01)
    {
        if (!_rule4HoldBreathOk) return;
        rule4HoldBreath.setParameterByName("BreathStrain", Mathf.Clamp01(strain01));
    }

    /// <summary>หยุดเสียงอั้นลมหายใจ</summary>
    public void StopHoldBreath()
    {
        if (!_rule4HoldBreathOk) return;
        rule4HoldBreath.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    /// <summary>หายใจออกแรงตอนกลั้นไม่ไหว (one-shot)</summary>
    public void PlayForcedExhale()
        => SafeOneShot("event:/Rule4/ForcedExhale", ListenerPos);

    /// <summary>เริ่มเสียงผีไล่ — ตำแหน่ง 3D อัปเดตผ่าน UpdateGhostChasePosition()</summary>
    public void StartGhostChase(Transform ghost)
    {
        if (!_rule4GhostChaseOk) return;
        rule4GhostChase.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        if (ghost != null)
            rule4GhostChase.set3DAttributes(RuntimeUtils.To3DAttributes(ghost));
        rule4GhostChase.start();
    }

    /// <summary>อัปเดตตำแหน่ง 3D ของเสียงผีไล่ (เรียกทุกเฟรมจาก Rule4Ghost)</summary>
    public void UpdateGhostChasePosition(Transform ghost)
    {
        if (!_rule4GhostChaseOk || ghost == null) return;
        rule4GhostChase.set3DAttributes(RuntimeUtils.To3DAttributes(ghost));
    }

    /// <summary>หยุดเสียงผีไล่</summary>
    public void StopGhostChase()
    {
        if (!_rule4GhostChaseOk) return;
        rule4GhostChase.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    /// <summary>เสียงเก็บตุ๊กตา (one-shot)</summary>
    public void PlayDollPickup()
        => SafeOneShot("event:/Rule4/DollPickup", ListenerPos);

    /// <summary>เสียงวางตุ๊กตาที่ศาล (one-shot)</summary>
    public void PlayDollPlace()
        => SafeOneShot("event:/Rule4/DollPlace", ListenerPos);

    // ─────────── Rule 2 Sounds ───────────

    private Vector3 ListenerPos =>
        player != null ? player.position : Vector3.zero;

    /// <summary>เริ่ม ambient background loop หลักของ Rule 2</summary>
    public void PlayRule2AmbienceBG()
    {
        if (player != null)
            rule2AmbienceBG.set3DAttributes(RuntimeUtils.To3DAttributes(player));
        rule2AmbienceBG.start();
    }

    /// <summary>เริ่ม ambient loop รองของ Rule 2</summary>
    public void PlayRule2Ambience()
    {
        if (player != null)
            rule2Ambience.set3DAttributes(RuntimeUtils.To3DAttributes(player));
        rule2Ambience.start();
    }

    /// <summary>หยุดเสียงทั้งหมดของ Rule 2 (loop + radio)</summary>
    public void StopAllRule2Sounds()
    {
        rule2AmbienceBG.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        rule2Ambience.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);

        // หยุดเสียงวิทยุที่ Rule 2 ใช้
        StopRadio();
        StopPlayRadioPrayer();
    }

    /// <summary>เสียงโหยหวน (one-shot)</summary>
    public void PlayRule2Wail()
        => RuntimeManager.PlayOneShot("event:/Rule2/Wail", ListenerPos);

    /// <summary>เสียงคนเดินในป่าข้างหลัง (one-shot)</summary>
    public void PlayRule2ForestFootsteps()
        => RuntimeManager.PlayOneShot("event:/Rule2/ForestFootsteps", ListenerPos);

    /// <summary>เสียงกระซิบข้างหู (one-shot)</summary>
    public void PlayRule2Whisper()
        => RuntimeManager.PlayOneShot("event:/Rule2/Whisper", ListenerPos);

    /// <summary>เสียงผีหัวเราะเบาๆ (one-shot)</summary>
    public void PlayRule2GhostLaugh()
        => RuntimeManager.PlayOneShot("event:/Rule2/GhostLaugh", ListenerPos);

    /// <summary>เสียงหวีดๆ ambient (one-shot)</summary>
    public void PlayRule2Eerie()
        => RuntimeManager.PlayOneShot("event:/Rule2/Eerie", ListenerPos);

    /// <summary>เสียงหมาหอนจากระยะไกล (one-shot)</summary>
    public void PlayRule2DogHowl()
        => RuntimeManager.PlayOneShot("event:/Rule2/DogHowl", ListenerPos);

    /// <summary>เสียงรถประจำทางเข้ามาใกล้ (one-shot)</summary>
    public void PlayRule2BusApproach()
        => RuntimeManager.PlayOneShot("event:/Rule2/BusApproach", ListenerPos);

    /// <summary>เสียงคนวิ่งผ่านหลังผู้เล่น (one-shot)</summary>
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
