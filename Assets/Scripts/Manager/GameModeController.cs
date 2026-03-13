using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
    
    

    public void SetRelaxMode()
    {
        globalVolume.profile = relaxProfile;
        bloodOverlay.SetActive(false);
    }

    public void SetTensionMode()
    {
        globalVolume.profile = tensionProfile;
        bloodOverlay.SetActive(true);
    }
    public void Blink()
    {
        StartCoroutine(BlinkRoutine());
    }

    IEnumerator BlinkRoutine()
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
}
