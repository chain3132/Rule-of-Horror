using System.Collections;
using System.Collections.Generic;
using System.Linq;
using InputSystem;
using TMPro;
using UnityEngine;

public class PaperSpawner : MonoBehaviour
{
    public Transform[] spawnPoints; 
    public GameObject[] paperPrefabs;
    [SerializeField] private GameObject[] fakePrefabs;
    public InputHandler inputHandler;
    [SerializeField] GameObject paperText;

    public (List<Paper> realPapers, List<Paper> fakePapers) SpawnPapers()
    {
        List<Transform> shuffledPoints = spawnPoints
            .OrderBy(x => Random.value)
            .ToList();
        
        List<GameObject> selectedReal = paperPrefabs
            .OrderBy(x => Random.value)
            .Take(4)
            .ToList();
        int fakeCount = spawnPoints.Length - 4;
        
        List<Paper> realPapers = new();
        List<Paper> fakePapers = new();
        int index = 0;
        for (int i = 0; i < 4; i++)
        {
            Transform point = shuffledPoints[index++];

            GameObject obj = Instantiate(selectedReal[i], point);

            SetupPaper(obj, false);
            
            realPapers.Add(obj.GetComponent<Paper>());
        }

        for (int i = 0; i < fakeCount; i++)
        {
            Transform point = shuffledPoints[index++];
            GameObject obj = Instantiate(fakePrefabs[Random.Range(0, fakePrefabs.Length)], point);

            SetupPaper(obj, true);

            fakePapers.Add(obj.GetComponent<Paper>());
        }
        return (realPapers, fakePapers);
    }
    public IEnumerator ShuffleRoutine(List<Paper> realPapers, List<Paper> fakePapers)
    {
        yield return new WaitForSeconds(0.3f);

        // Destroy fake เก่าทั้งหมด
        foreach (var fake in fakePapers)
        {
            if (fake != null)
                Destroy(fake.gameObject);
        }
        fakePapers.Clear();

        // real ที่ยังเหลือ
        List<Paper> activePapers = new List<Paper>();
        activePapers.AddRange(realPapers.Where(p => p != null && p.gameObject.activeSelf));

        // spawn fake ใหม่ให้ครบ spawnPoints ของ PaperSpawner
        int fakeNeeded = spawnPoints.Length - activePapers.Count;
        for (int i = 0; i < fakeNeeded; i++)
        {
            Paper fake = SpawnFake();
            fakePapers.Add(fake);
            activePapers.Add(fake);
        }

        // สลับตำแหน่ง
        List<Transform> shuffledPoints = spawnPoints
            .OrderBy(x => Random.value)
            .ToList();

        for (int i = 0; i < activePapers.Count; i++)
        {
            activePapers[i].transform.SetParent(shuffledPoints[i]);
            activePapers[i].transform.localPosition = Vector3.zero;
            activePapers[i].transform.localRotation = Quaternion.identity;
        }
    }
    public Paper SpawnFake()
    {
        GameObject prefab = fakePrefabs[Random.Range(0, fakePrefabs.Length)];
        GameObject obj = Instantiate(prefab);

        SetupPaper(obj, true);

        return obj.GetComponent<Paper>();
    }
    
    void SetupPaper(GameObject obj, bool isFake)
    {
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        Paper p = obj.GetComponent<Paper>();
        p.isFake = isFake;
        p.SetInputHandler(inputHandler);
        p.GetInteractionText(paperText);
    }
}
