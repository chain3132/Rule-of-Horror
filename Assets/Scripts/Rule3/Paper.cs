using System.Collections;
using InputSystem;
using UnityEngine;

public class Paper : MonoBehaviour
{
    public int number;

    private bool isPlayerLooking;
    private Camera playerCam;
    private InputHandler inputHandler;
    public void SetInputHandler(InputHandler handler)
    {
        inputHandler = handler;
    }

    public void SetNumber(int num)
    {
        number = num;
    }

    private void Start()
    {
        playerCam = Camera.main;
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
        if (isPlayerLooking)
        {
            inputHandler.OnRightClickPressed += OnInteract;
        }
        else
        {
            inputHandler.OnRightClickPressed -= OnInteract;
        }
    }

    void OnInteract()
    {
        StartCoroutine(InteractRoutine());
    }

    IEnumerator InteractRoutine()
    {
        LookDistortionSystem.Instance.StartFocus(transform);

        // รอซูมเข้า
        yield return new WaitForSeconds(1f);

        // 🔥 เผา
        Burn();

        yield return new WaitForSeconds(1.2f);

        LookDistortionSystem.Instance.StopFocus();
    }

    void Burn()
    {
        // play animation / shader
    }
}
