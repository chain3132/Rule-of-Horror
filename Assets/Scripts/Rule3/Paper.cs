using System.Collections;
using InputSystem;
using UnityEngine;

public class Paper : MonoBehaviour
{
    public int number;
    public bool isFake;

    private bool          isPlayerLooking;
    private Camera        playerCam;
    private InputHandler  inputHandler;
    private bool          isSubscribed = false;
    private bool          isUsed       = false;
    private float         lookEnterTime;
    private GameObject    interactionText;

    [SerializeField] private Renderer paperRenderer;
    private Material mat;

    // ─────────────── Init ───────────────

    public void SetInputHandler(InputHandler handler)
    {
        inputHandler = handler;
    }

    public void GetInteractionText(GameObject text)
    {
        interactionText = text;
    }

    // ─────────────── Lifecycle ───────────────

    private void Start()
    {
        playerCam     = Camera.main;
        paperRenderer = GetComponent<Renderer>();
        mat           = paperRenderer.material;
    }

    /// <summary>Unsubscribe when the paper is disabled (e.g. during shuffle repositioning).</summary>
    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>Unsubscribe when the paper is destroyed — prevents MissingReferenceException on right-click.</summary>
    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Update()
    {
        CheckLook();
    }

    // ─────────────── Look Detection ───────────────

    void CheckLook()
    {
        if (playerCam == null) return;

        Vector3 dir   = (transform.position - playerCam.transform.position).normalized;
        float   angle = Vector3.Angle(playerCam.transform.forward, dir);

        isPlayerLooking = angle < 10f;

        if (isPlayerLooking && !isSubscribed)
        {
            isSubscribed  = true;
            lookEnterTime = Time.time;
            inputHandler.OnRightClickPressed += OnInteract;
            if (interactionText != null) interactionText.SetActive(true);
        }
        else if (!isPlayerLooking && isSubscribed)
        {
            Unsubscribe();
        }
    }

    /// <summary>Safely removes the input subscription and hides interaction text.</summary>
    void Unsubscribe()
    {
        if (isSubscribed && inputHandler != null)
        {
            inputHandler.OnRightClickPressed -= OnInteract;
            isSubscribed = false;
        }
        if (interactionText != null)
            interactionText.SetActive(false);
    }

    // ─────────────── Interaction ───────────────

    void OnInteract()
    {
        // Debounce: ignore click if player just started looking at this paper
        if (Time.time - lookEnterTime < 0.1f) return;
        if (isUsed) return;

        Debug.Log("Interact with paper: " + number);

        isUsed = true;
        if (interactionText != null) interactionText.SetActive(false);

        if (isFake)
            StartCoroutine(FakeRoutine());
        else
            StartCoroutine(InteractRoutine());
    }

    // ─────────────── Fake Paper ───────────────

    IEnumerator FakeRoutine()
    {
        LookDistortionSystem.Instance.StartFocus(transform);
        yield return new WaitForSeconds(1f);

        // Fake paper = always wrong
        Rule3.Instance.OnWrong();

        yield return new WaitForSeconds(0.5f);
        LookDistortionSystem.Instance.StopFocus();

        // Allow the player to click this fake paper again (it will always be wrong)
        isUsed = false;
    }

    // ─────────────── Real Paper ───────────────

    IEnumerator InteractRoutine()
    {
        LookDistortionSystem.Instance.StartFocus(transform);

        yield return new WaitForSeconds(1f);

        bool isCorrect = Rule3.Instance.CheckAnswer(number);

        if (isCorrect)
        {
            Burn();
        }
        else
        {
            // Wrong order → notify Rule3 to increase vignette rate; paper stays
            Rule3.Instance.OnWrong();
            isUsed = false; // allow retry
        }

        yield return new WaitForSeconds(1f);
        LookDistortionSystem.Instance.StopFocus();
    }

    // ─────────────── Burn (correct answer) ───────────────

    void Burn()
    {
        // Unsubscribe before we start destroying to prevent any late callbacks
        Unsubscribe();
        StartCoroutine(BurnRoutine());
    }

    IEnumerator BurnRoutine()
    {
        float duration = 1.5f;
        float t        = 0f;

        AudioManager.instance.PlayBurnPaper();

        while (t < duration)
        {
            t += Time.deltaTime;
            mat.SetFloat("_Dissolve", t / duration);
            yield return null;
        }

        mat.SetFloat("_Dissolve", 1f);

        // Notify Rule3 – Rule3.OnCorrectRoutine will Destroy this GameObject
        // Do NOT call SetActive(false) here; let Rule3 manage the lifetime
        Rule3.Instance.OnPaperSelected(this);
    }
}
