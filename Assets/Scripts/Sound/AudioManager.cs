using System.Collections;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;
    
    private EventInstance music;
    private EventInstance heartbeat;

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
    }
    public void SetHeartbeat(float value)
    {
        targetHeart = value;
    }
    public void UpdateHeartbeat()
    {
        currentHeart = Mathf.Lerp(currentHeart, targetHeart, Time.deltaTime * 2f);
        heartbeat.setParameterByName("HeartLevel", currentHeart);
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
