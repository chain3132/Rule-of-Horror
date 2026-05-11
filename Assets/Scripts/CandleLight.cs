using UnityEngine;

public class CandleLight : MonoBehaviour
{
    [SerializeField] private Light candleLight;
    private bool isLit = false;

    void Start()
    {
        candleLight.enabled = false;
    }

    private void OnEnable()
    {
        if (candleLight != null)
            candleLight.enabled = isLit;
    }

    public void LightCandle()
    {
        if (isLit) return;

        isLit = true;
        candleLight.enabled = true;
    }

    public bool IsLit()
    {
        return isLit;
    }

    /// <summary>ซ่อนแสงชั่วคราว (isLit ยังคงอยู่) — ใช้ตอนเข้า Tension mode</summary>
    public void HideLight()
    {
        if (candleLight != null)
            candleLight.enabled = false;
    }

    /// <summary>แสดงแสงคืน ถ้าเทียนติดอยู่ — ใช้ตอนเข้า Relax mode</summary>
    public void ShowLight()
    {
        if (candleLight != null)
            candleLight.enabled = isLit;
    }
}
