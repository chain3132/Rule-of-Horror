using UnityEngine;

public class LookDestroyer : MonoBehaviour
{
    public float requiredLookTime = 0.3f;
    public float lookAngle = 8f;

    private float lookTimer = 0f;
    private Camera cam;
    private bool hasPlayedSound = false;

    private void Start()
    {
        cam = Camera.main;
    }

    private void Update()
    {
        if (IsLookingAtTarget())
        {
            lookTimer += Time.deltaTime;

            // 🔥 เล่นเสียงครั้งเดียวตอนเริ่มมอง
            if (!hasPlayedSound)
            {
                AudioManager.instance.PlayJumpScare();
                hasPlayedSound = true;
            }

            if (lookTimer >= requiredLookTime)
            {
                DestroySelf();
            }
        }
        else
        {
            // 🔥 decay แทน reset (สำคัญมาก)
            lookTimer -= Time.deltaTime * 2f;
            lookTimer = Mathf.Max(0f, lookTimer);

            hasPlayedSound = false;
        }
    }

    bool IsLookingAtTarget()
    {
        Vector3 dir = (transform.position - cam.transform.position).normalized;

        // กันกรณีอยู่หลังกล้อง
        float dot = Vector3.Dot(cam.transform.forward, dir);
        if (dot < 0) return false;

        float angle = Vector3.Angle(cam.transform.forward, dir);
        return angle < lookAngle;
    }

    void DestroySelf()
    {
        Debug.Log("Destroyed: " + gameObject.name);
        Destroy(gameObject);
    }
}
