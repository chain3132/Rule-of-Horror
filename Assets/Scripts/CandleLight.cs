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
}
