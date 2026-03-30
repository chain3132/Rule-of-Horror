using System.Collections;
using TMPro;
using UnityEngine;

public class HorrorTextUI : MonoBehaviour
{
    public static HorrorTextUI instance;

    [Header("UI")]
    public TMP_Text uiText;
    private CanvasGroup canvasGroup;

    [Header("Typing")]
    public float typingSpeed = 0.03f;

    [Header("Subtle Flicker")]
    public bool enableFlicker = true;
    public float flickerIntensity = 0.1f;

    [Header("Fade")]
    public float fadeSpeed = 0.5f;

    private Coroutine currentRoutine;

    void Awake()
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
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0;
    }

    #region Public API

    public void ShowText(string message)
    {
        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(ShowRoutine(message));
    }

    public void HideText()
    {
        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(HideRoutine());
    }

    #endregion

    #region Show

    IEnumerator ShowRoutine(string text)
    {
        yield return FadeIn();
        yield return TypeText(text);
    }

    IEnumerator FadeIn()
    {
        while (canvasGroup.alpha < 1)
        {
            canvasGroup.alpha += Time.deltaTime * fadeSpeed;
            yield return null;
        }
    }

    #endregion

    #region Typewriter

    IEnumerator TypeText(string text)
    {
        uiText.text = "";

        for (int i = 0; i < text.Length; i++)
        {
            uiText.text += text[i];

            // 🔥 subtle flicker (ไม่รบกวนสายตา)
            if (enableFlicker)
            {
                Color col = uiText.color;
                col.a = 1f - Random.Range(0f, flickerIntensity);
                uiText.color = col;
            }

            yield return new WaitForSeconds(typingSpeed);
        }
    }

    #endregion

    #region Hide

    IEnumerator HideRoutine()
    {
        // 🔥 delay นิดนึงให้คนอ่านทัน
        yield return new WaitForSeconds(0.5f);

        // 🔥 fade out นุ่ม ๆ
        while (canvasGroup.alpha > 0)
        {
            canvasGroup.alpha -= Time.deltaTime * fadeSpeed;
            yield return null;
        }

        uiText.text = "";
    }

    #endregion
}
