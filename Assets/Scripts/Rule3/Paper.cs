using System.Collections;
using InputSystem;
using TMPro;
using UnityEngine;

public class Paper : MonoBehaviour
{
    public int number;

    private bool isPlayerLooking;
    private Camera playerCam;
    private InputHandler inputHandler;
    private bool isSubscribed = false;
    private bool isUsed = false;
    private float lookEnterTime;
    private GameObject interactionText;
    [SerializeField] private Renderer paperRenderer;
    private Material mat;
    public void SetInputHandler(InputHandler handler)
    {
        inputHandler = handler;
    }
    public void GetInteractionText(GameObject text)
    {
        interactionText = text;
    }

    private void Start()
    {
        playerCam = Camera.main;
        paperRenderer = GetComponent<Renderer>();
        mat = paperRenderer.material;
    }

    private void Update()
    {
        CheckLook();
    }

    void CheckLook()
    {
        Vector3 dir = (transform.position - playerCam.transform.position).normalized;
        float angle = Vector3.Angle(playerCam.transform.forward, dir);

        isPlayerLooking = angle < 10f;
        if (isPlayerLooking && !isSubscribed)
        {
            isSubscribed = true;
            lookEnterTime = Time.time;
            inputHandler.OnRightClickPressed += OnInteract;
            interactionText.SetActive(true);
            

        }
        else if (!isPlayerLooking && isSubscribed)
        {
            inputHandler.OnRightClickPressed -= OnInteract;
            interactionText.SetActive(false);
            isSubscribed = false;
        }
    }

    void OnInteract()
    {
        if (Time.time - lookEnterTime < 0.1f) return;
        Debug.Log("Interact with paper: " + number);
        if (isUsed) return;

        isUsed = true;
        StartCoroutine(InteractRoutine());
        interactionText.SetActive(false);
    }

    IEnumerator InteractRoutine()
    {
        LookDistortionSystem.Instance.StartFocus(transform);

        // รอซูมเข้า
        yield return new WaitForSeconds(1f);
        bool isCorrect = Rule3.Instance.CheckAnswer(number);
        if (isCorrect)
        {
            Burn();
        }
        else{
            isUsed = false;
            yield return new WaitForSeconds(1f);
        }
        LookDistortionSystem.Instance.StopFocus();
    }

    void Burn()
    {
        inputHandler.OnRightClickPressed -= OnInteract;
        // play animation / shader
        StartCoroutine(BurnRoutine());    }
    IEnumerator BurnRoutine()
    {
        float duration = 1.5f; // ระยะเวลาเผา
        float t = 0;

        while (t < duration)
        {
            t += Time.deltaTime;

            float dissolveValue = t / duration; // 0 → 1
            mat.SetFloat("_Dissolve", dissolveValue);

            yield return null;
        }

        mat.SetFloat("_Dissolve", 1f); // กันไม่สุด

        // แจ้งว่าเลือกเสร็จ
        Rule3.Instance.OnPaperSelected(number);

        gameObject.SetActive(false);
    }
}
