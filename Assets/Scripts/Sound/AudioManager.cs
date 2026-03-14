using System.Collections;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;
    [Header("- - - Audio Source - - -")]
    [SerializeField] private AudioSource musicSource;

    [Header("- - - Audio Clip - - -")]
    public AudioClip backgroundMusic;
    private FMOD.Studio.EventInstance music;



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

        StartCoroutine(FadeIn());
    }
    
    IEnumerator FadeIn()
    {
        float t = 0;

        while (t < 1)
        {
            t += Time.deltaTime / 3f; // 3 วิ
            music.setParameterByName("MusicIntensity", t);
            yield return null;
        }
    }
}
