using Player;
using UnityEngine;

public class LookDistortionSystem : MonoBehaviour
{
    public static LookDistortionSystem Instance;

    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float forceMultiplier = 2f;
    [SerializeField] private PlayerController player;
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
        if (!isActive || target == null)
        {
            player.SetExternalLookForce(Vector2.zero);
            return;
        }

        Vector3 dir = (target.position - cameraPivot.position).normalized;

        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float currentYaw = cameraPivot.parent.eulerAngles.y;

        float deltaYaw = Mathf.DeltaAngle(currentYaw, targetYaw);

        // เพิ่มแรงเรื่อย ๆ (Tension)
        currentForce += Time.deltaTime * 3f;

        float forceX = deltaYaw * 0.01f * currentForce * forceMultiplier;

        externalForce = new Vector2(forceX, 0);
        player.SetExternalLookForce(externalForce);
    }
}
