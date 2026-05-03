using System;
using System.Collections;
using System.Collections.Generic;
using Manager;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public enum GameMode
{
    Relax,
    Tension
}

public class GameModeController : MonoBehaviour
{
    public Volume globalVolume;
    
    public VolumeProfile relaxProfile;
    public VolumeProfile tensionProfile;

    public GameObject bloodOverlay;
    public Light directionLight;
    
    
    public RectTransform topLid;
    public RectTransform bottomLid;

    public float blinkSize = 500f;  
    public float speed = 5f;
    
    [SerializeField] private MeshFilter saraMeshFilter;
    [SerializeField] private MeshCollider saraMeshCollider;
    [SerializeField] private MeshRenderer saraMeshRenderer;
    
    [SerializeField] private SerializedDictionary<GameMode,SaraModel> seraModels;
    [SerializeField] private List<CandleLight> candles;
    
    [SerializeField] private PhoneUIController phoneUI;
    [SerializeField] private WindZone[] windZones;
    
    [SerializeField] private Color relaxLightColor = new Color(1f, 0.95f, 0.8f);
    [SerializeField] private Color tensionLightColor = new Color(0.8f, 0.9f, 1f);
    [SerializeField] private Color relaxSkyBoxColor = new Color(1f, 0.95f, 0.8f);
    [SerializeField] private Color tensionSkyBoxColor = new Color(0.8f, 0.9f, 1f);
    [SerializeField] private ParticleSystem[] relaxParticle;
    [Header("Skybox Settings")]
    public Material relaxSkyboxMat;
    public Material tensionSkyboxMat;
    
    
    public static GameModeController instance;
    public bool IsEyesOpen { get; private set; } = false;
    LensDistortion lens;
    ChromaticAberration chroma;
    DepthOfField dof;
    
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
        globalVolume.profile.TryGet(out lens);
        globalVolume.profile.TryGet(out chroma);
        globalVolume.profile.TryGet(out dof);
    }
    public void SetRelaxMode()
    {
        BlinkToMode(GameMode.Relax);
        SetWindZone(GameMode.Relax);
    }
    public void SetWindZone(GameMode tenstion)
    {
        foreach(var zone in windZones)
        {
            switch (tenstion)
            {
                
                case GameMode.Relax:
                    zone.windMain = 0.2f;
                    break;
                case GameMode.Tension:
                    zone.windMain = 0.4f;
                    break;
            };
            Debug.Log("Set wind zone to " + zone.windMain);
        }
    }
    public void SetTensionMode()
    {
        BlinkToMode(GameMode.Tension);

    }
    public void BlinkToMode(GameMode targetMode = GameMode.Relax)
    {
        StartCoroutine(FaintThenBlinkRoutine(targetMode));
    }

    /// <summary>
    /// Blink to mode WITHOUT the faint/dizziness pre-animation.
    /// Use this after Rule 3 death where the screen is already dark —
    /// avoids the faint effect accidentally brightening the screen mid-transition.
    /// Eyelids close → ApplyMode → eyelids open. No post-processing manipulation.
    /// </summary>
    public void DirectBlinkToMode(GameMode targetMode = GameMode.Relax)
    {
        StartCoroutine(BlinkRoutine(targetMode));
    }
    IEnumerator FaintThenBlinkRoutine(GameMode targetMode)
    {
        float duration = 1f;
        float t = 0;
        TimeManager.instance.IsPauseTime(true);

        // เล่นเสียง heartbeat + build-up tension ตอน transition เข้า Tension mode เท่านั้น
        if (targetMode == GameMode.Tension)
            AudioManager.instance.StartRule3Intro();

        // : เวียนหัว
        while (t < duration)
        {   
            t += Time.deltaTime;

            float normalized = t / duration;
            float curve = Mathf.Pow(normalized, 2.5f);
            Time.timeScale = Mathf.Lerp(1f, 0.6f, curve);
            float shakeX = Mathf.Sin(Time.time * 12f) * curve * 2f;
            float shakeY = Mathf.Cos(Time.time * 9f) * curve * 2f;
            float roll = Mathf.Sin(Time.time * 6f) * curve * 8f;

            Camera.main.transform.localRotation = Quaternion.Euler(shakeX, shakeY, roll);
            
            if (normalized < 0.65f)
            {
                // phase 1: เริ่มมึน (เบา)
                SetFaintEffect(curve * 0.5f);
            }
            else
            {
                // phase 2: พัง 
                SetFaintEffect(curve);

               
                if (UnityEngine.Random.value < 0.1f * curve)
                {
                    Camera.main.transform.localRotation *= Quaternion.Euler(
                        UnityEngine.Random.Range(-3f, 3f),
                        UnityEngine.Random.Range(-3f, 3f),
                        UnityEngine.Random.Range(-10f, 10f)
                    );
                }
            }
            yield return new WaitForSecondsRealtime(0.01f + (curve * 0.04f));
        }
        Time.timeScale = 1f;

        Camera.main.transform.localRotation = Quaternion.identity;

        yield return new WaitForSeconds(0.5f);
        // เข้าสู่ blink จริง
        yield return StartCoroutine(BlinkRoutine(targetMode));
    }

    void SetFaintEffect(float t)
    {
        // มืดลง
        if (globalVolume.profile.TryGet(out ColorAdjustments color))
        {
            color.postExposure.value = Mathf.Lerp(0f, -2f, t);
            color.contrast.value = Mathf.Lerp(0f, -30f, t);
        }

        // ขอบมืด
        if (globalVolume.profile.TryGet(out Vignette vignette))
        {
            vignette.intensity.value = Mathf.Lerp(0.2f, 0.6f, t);
        }

        //  ภาพบิด
        if (lens != null)
        {
            float baseDistort = Mathf.Lerp(0f, -0.6f, t);

            // noise เพิ่มความแรงตาม t
            float noise = Mathf.Sin(Time.time * 20f) * 0.2f * t;

            lens.intensity.value = baseDistort + noise;
        }


        if (chroma != null)
        {
            chroma.intensity.value = Mathf.Lerp(0f, 1f, t);
        }

        if (dof != null)
        {
            dof.focusDistance.value = Mathf.Lerp(10f, 0.3f, t);
            dof.aperture.value = Mathf.Lerp(5.6f, 0.8f, t);
        }

        if (globalVolume.profile.TryGet(out ColorAdjustments saturationColor))
        {
            saturationColor.saturation.value = Mathf.Lerp(0f, -100f, t);
        }
    
    }

    IEnumerator BlinkRoutine(GameMode targetMode)
    {
        float t = 0;
        float widthTop = topLid.sizeDelta.x;
        float widthBottom = bottomLid.sizeDelta.x;
        IsEyesOpen = false;
        // ปิดตา
        while(t < 1)
        {
            t += Time.deltaTime * speed;
            float curve = Mathf.Pow(t, 2); // ปิดเร็วช่วงท้าย
            float size = Mathf.Lerp(0, blinkSize, curve);
            topLid.sizeDelta = new Vector2(widthTop, size);
            bottomLid.sizeDelta = new Vector2(widthBottom, size);

            yield return null;
        }
        
        ApplyMode(targetMode);
        yield return new WaitForSeconds(0.8f);

        // เปิดตา
        t = 1;

        while(t > 0)
        {
            t -= Time.deltaTime * speed;

            float size = Mathf.Lerp(0, blinkSize, t);

            topLid.sizeDelta = new Vector2(widthTop, size);
            bottomLid.sizeDelta = new Vector2(widthBottom, size);   

            yield return null;
        }
        IsEyesOpen = true;
        TimeManager.instance.IsPauseTime(false);

        // หยุดเสียง transition เมื่อตาเปิดแล้ว (mode change เสร็จ)
        if (targetMode == GameMode.Tension)
            AudioManager.instance.StopRule3Intro();
    }

    void SetSaraModel(GameMode mode)
    {
        if(seraModels.TryGetValue(mode,out SaraModel model))
        {
            saraMeshFilter.mesh = model.saraMeshFilter;
            saraMeshCollider.sharedMesh = model.saraMeshFilter;
            Material[] mats = new Material[saraMeshRenderer.materials.Length];

            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = model.saraMeshRenderer;
            }

            saraMeshRenderer.materials = mats;
        }
    }
    void ApplyMode(GameMode mode)
    {
        Debug.Log($"Applying {mode} mode");
        if (mode == GameMode.Relax)
        {
            globalVolume.profile = relaxProfile;
            bloodOverlay.SetActive(false);
            SetSaraModel(mode);
            LightRandomCandle(); 
            SetWindZone(GameMode.Relax);
            directionLight.color = relaxLightColor;
            RenderSettings.skybox = relaxSkyboxMat;
            //RenderSettings.skybox.SetColor("_Tint", relaxSkyBoxColor);
            AudioManager.instance.SwitchWindAmbience(0);
            foreach (var particle in relaxParticle)
            {
                particle.Play();
            }

        }
        else
        {
            globalVolume.profile = tensionProfile;
            bloodOverlay.SetActive(true);
            directionLight.color = tensionLightColor;
            RenderSettings.skybox = tensionSkyboxMat;
            RenderSettings.skybox.SetColor("_Tint", tensionSkyBoxColor);
            SetSaraModel(mode);
            SetWindZone(GameMode.Tension);
            AudioManager.instance.SwitchWindAmbience(1);
            foreach (var particle in relaxParticle)
            {
                particle.Stop();
            }


        }
        
    }
    public void LightRandomCandle()
    {
        foreach(var candle in candles)
        {
            if(!candle.IsLit())
            {
                candle.LightCandle();
                break;
            }
        }
    }
}
