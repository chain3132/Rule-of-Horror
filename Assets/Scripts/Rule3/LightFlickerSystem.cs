using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class LightFlickerSystem : MonoBehaviour
{
    public static LightFlickerSystem Instance;

    [SerializeField] private Light[] lights;

    /// <summary>Fired once at the start of every flicker burst (ambient or impact).</summary>
    public event Action OnFlickerBurst;

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
            // ข้ามไฟเทียน — CandleLight จัดการ enabled state เอง
            if (l.GetComponent<CandleLight>() != null || l.GetComponentInParent<CandleLight>() != null)
                continue;
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
                OnFlickerBurst?.Invoke(); // notify subscribers (e.g. Rule3 → LightBulb sound)

                foreach (var l in lights)
                {
                    // ข้ามไฟเทียน — ไม่แก้ intensity ของมัน
                    if (l.GetComponent<CandleLight>() != null || l.GetComponentInParent<CandleLight>() != null)
                        continue;
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
            // ข้ามไฟเทียน
            if (l.GetComponent<CandleLight>() != null || l.GetComponentInParent<CandleLight>() != null)
                continue;
            l.intensity = 13f; // หรือค่า base ของคุณ
        }
    }
    IEnumerator ImpactFlickerRoutine()
    {
        // 🔥 phase 1: กระพริบแรง
        for (int i = 0; i < 6; i++)
        {
            SetAllLights(false);
            OnFlickerBurst?.Invoke(); // notify subscribers for impact flicker too
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
