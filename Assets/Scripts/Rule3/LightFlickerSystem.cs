using System.Collections;
using UnityEngine;

public class LightFlickerSystem : MonoBehaviour
{
    public static LightFlickerSystem Instance;

    [SerializeField] private Light[] lights;

    private void Awake()
    {
        Instance = this;
    }

    public void StartAmbientFlicker()
    {
        StartCoroutine(AmbientFlickerRoutine());
    }

    public void StopAmbientFlicker()
    {
        StopAllCoroutines();
        SetAllLights(true);
    }

    public void PlayImpactFlicker()
    {
        StopAllCoroutines();
        StartCoroutine(ImpactFlickerRoutine());
    }

    void SetAllLights(bool state)
    {
        foreach (var l in lights)
        {
            l.enabled = state;
        }
    }
    IEnumerator AmbientFlickerRoutine()
    {
        while (true)
        {
            // ‍ช่วงปกติ (ไม่มี flicker)
            float calmTime = Random.Range(2f, 5f);
            SetNormalIntensity();
            yield return new WaitForSeconds(calmTime);

            // ⚡ ช่วง flicker (burst สั้น ๆ)
            float flickerDuration = Random.Range(0.3f, 1f);
            float t = 0;

            while (t < flickerDuration)
            {
                t += Time.deltaTime;

                foreach (var l in lights)
                {
                    float flicker = Random.Range(0.6f, 13f);
                    l.intensity = flicker;
                }

                yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));
            }

            // 🔄 กลับปกติ
            SetNormalIntensity();
        }
    }
    void SetNormalIntensity()
    {
        foreach (var l in lights)
        {
            l.intensity = 13f; // หรือค่า base ของคุณ
        }
    }
    IEnumerator ImpactFlickerRoutine()
    {
        // 🔥 phase 1: กระพริบแรง
        for (int i = 0; i < 6; i++)
        {
            SetAllLights(false);
            yield return new WaitForSeconds(0.05f);

            SetAllLights(true);
            yield return new WaitForSeconds(0.05f);
        }

        // 🔥 phase 2: ดับจริง
        SetAllLights(false);
        yield return new WaitForSeconds(2f);

        // 🔥 phase 3: เปิดกลับ
        SetAllLights(true);

        // 🔁 กลับไป ambient
        StartAmbientFlicker();
    }
}
