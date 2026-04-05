using Player;
using UnityEngine;

public class LookDistortionSystem : MonoBehaviour
{
    public static LookDistortionSystem Instance;

    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float forceMultiplier = 2f;
    [SerializeField] private PlayerController player;
    
    [SerializeField] private Camera cam;
    [SerializeField] private float normalFOV = 60f;
    [SerializeField] private float zoomFOV = 40f;
    [SerializeField] private float zoomSpeed = 5f;
    
    private bool isFocusMode;
    private Transform focusTarget;
    private bool isActive;
    private Transform target;
    private float currentForce;

    private Vector2 externalForce;

    public Vector2 ExternalForce => externalForce;

    private void Awake()
    {
        Instance = this;
    }

    public void StartPull(Transform targetTransform)
    {
        target = targetTransform;
        isActive = true;
        currentForce = 0.5f;
    }

    public void StopPull()
    {
        isActive = false;
        externalForce = Vector2.zero;
    }

    void Update()
    {
        //  Focus Mode (priority สูงสุด)
        if (isFocusMode && focusTarget != null)
        {
            HandleFocusMode();
            return;
        }

        // 🔁 Pull Mode
        if (isActive && target != null)
        {
            HandlePullMode();
            return;
        }

        // 💤 Idle
        player.SetExternalLookForce(Vector2.zero);

        cam.fieldOfView = Mathf.Lerp(
            cam.fieldOfView,
            normalFOV,
            Time.deltaTime * zoomSpeed
        );
    }
    void HandlePullMode()
    {
        Vector3 dir = (target.position - cameraPivot.position).normalized;

        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float currentYaw = cameraPivot.parent.eulerAngles.y;

        float deltaYaw = Mathf.DeltaAngle(currentYaw, targetYaw);

        currentForce += Time.deltaTime * 3f;

        float forceX = deltaYaw * 0.01f * currentForce * forceMultiplier;

        externalForce = new Vector2(forceX, 0);
        player.SetExternalLookForce(externalForce);

        float t = Mathf.Clamp01(currentForce / 2f);
        t = t * t;

        float targetFOV = Mathf.Lerp(normalFOV, zoomFOV, t);

        cam.fieldOfView = Mathf.MoveTowards(
            cam.fieldOfView,
            targetFOV,
            zoomSpeed * Time.deltaTime
        );
    }
    void HandleFocusMode()
    {
        // 👉 หมุนกล้องไปหากระดาษแบบ smooth
        Vector3 dir = (focusTarget.position - cameraPivot.position).normalized;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        cameraPivot.rotation = Quaternion.Slerp(
            cameraPivot.rotation,
            targetRot,
            Time.deltaTime * 3f
        );

        // 👉 ซูมเข้า
        cam.fieldOfView = Mathf.MoveTowards(
            cam.fieldOfView,
            zoomFOV,
            10 * Time.deltaTime
        );
    }
    public void StartFocus(Transform targetTransform)
    {
        // ❗ ปิด pull mode ก่อน
        isActive = false;
        target = null;
        externalForce = Vector2.zero;

        focusTarget = targetTransform;
        isFocusMode = true;

        player.SetExternalLookForce(Vector2.zero);
        player.SetLook(false);
    }

    public void StopFocus()
    {
        isFocusMode = false;
        focusTarget = null;

        // reset
        externalForce = Vector2.zero;
        player.SetExternalLookForce(Vector2.zero);

        player.SetLook(true); 
    }
}
