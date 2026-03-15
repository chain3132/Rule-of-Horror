using UnityEngine;

public class HeartZone : MonoBehaviour
{
    public float heartValue;

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            AudioManager.instance.SetHeartbeat(heartValue);
        }
    }
}
