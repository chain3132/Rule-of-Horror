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

   

    public void SetRelaxMode()
    {
        BlinkToMode(GameMode.Relax);
    }

    public void SetTensionMode()
    {
        BlinkToMode(GameMode.Tension);

    }
    public void BlinkToMode(GameMode targetMode = GameMode.Relax)
    {
        StartCoroutine(BlinkRoutine(targetMode));
    }

    IEnumerator BlinkRoutine(GameMode targetMode)
    {
        float t = 0;
        float widthTop = topLid.sizeDelta.x;
        float widthBottom = bottomLid.sizeDelta.x;
        // ปิดตา
        while(t < 1)
        {
            t += Time.deltaTime * speed;
            float size = Mathf.Lerp(0, blinkSize, t);
            topLid.sizeDelta = new Vector2(widthTop, size);
            bottomLid.sizeDelta = new Vector2(widthBottom, size);

            yield return null;
        }
        
        ApplyMode(targetMode);
        yield return new WaitForSeconds(1f);

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
        if (mode == GameMode.Relax)
        {
            globalVolume.profile = relaxProfile;
            bloodOverlay.SetActive(false);
            AudioManager.instance.FadeOutMusic();
            SetSaraModel(mode);
            LightRandomCandle(); 
        }
        else
        {
            globalVolume.profile = tensionProfile;
            bloodOverlay.SetActive(true);
            AudioManager.instance.FadeInMusic();
            SetSaraModel(mode);
        }
        if (mode == GameMode.Relax)
        {
            phoneUI.SetSignalJam(false);
        }
        else
        {
            phoneUI.SetSignalJam(true);
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
