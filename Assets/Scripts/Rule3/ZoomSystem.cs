using System.Collections;
using UnityEngine;

public class ZoomSystem : MonoBehaviour
{
    public static ZoomSystem Instance;

    public Transform cameraTransform;
    public float zoomDistance = 0.5f;
    public float speed = 5f;

    private void Awake()
    {
        Instance = this;
    }

    public void ZoomTo(Transform target)
    {
        StopAllCoroutines();
        StartCoroutine(ZoomRoutine(target));
    }

    IEnumerator ZoomRoutine(Transform target)
    {
        Vector3 targetPos = target.position - target.forward * zoomDistance;

        float t = 0;
        Vector3 startPos = cameraTransform.position;

        while (t < 1)
        {
            t += Time.deltaTime * speed;
            cameraTransform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }
    }
}
