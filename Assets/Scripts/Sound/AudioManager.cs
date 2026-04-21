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
    Coroutine radioCoroutine;
    
    

    [SerializeField] private Transform radioTransform;
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

        
        radioOpen = RuntimeManager.CreateInstance("event:/RadioOpen");
        radioOpen.set3DAttributes(RuntimeUtils.To3DAttributes(radioTransform));
        
        radioSound = RuntimeManager.CreateInstance("event:/RadioSound");
        radioSound.set3DAttributes(RuntimeUtils.To3DAttributes(radioTransform));
        
        radioNoise = RuntimeManager.CreateInstance("event:/RadioNoise");
        radioNoise.set3DAttributes(RuntimeUtils.To3DAttributes(radioTransform));
        
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
    

    IEnumerator PlayRadioRoutine()
    {
        radioOpen.start();
        radioOpen.release(); 
        
        yield return new WaitForSeconds(0.5f);
        
        yield return StartCoroutine(FadeInNoise(1f));
        
    }
    IEnumerator FadeInNoise(float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float normalized = t / duration;

            radioSound.setVolume(normalized);
            yield return null;
        }

        radioSound.setVolume(1f);
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
