using UnityEngine;

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

    private void Start()
    {
        musicSource.clip = backgroundMusic;
        musicSource.Play();
    }
}
