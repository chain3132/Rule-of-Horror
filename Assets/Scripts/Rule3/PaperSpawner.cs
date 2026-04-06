using System.Collections.Generic;
using System.Linq;
using InputSystem;
using TMPro;
using UnityEngine;

public class PaperSpawner : MonoBehaviour
{
    public Transform[] spawnPoints; // 8 จุด
    public GameObject[] paperPrefabs;
    public InputHandler inputHandler;
    [SerializeField] GameObject paperText;

    public List<Paper> SpawnPapers()
    {
        List<Transform> selectedPoints = spawnPoints
            .OrderBy(x => Random.value)
            .Take(4)
            .ToList();
        
        List<GameObject> selectedPrefabs = paperPrefabs
            .OrderBy(x => Random.value)
            .Take(4)
            .ToList();

        List<Paper> activePapers = new List<Paper>();

        for (int i = 0; i < 4; i++)
        {
            Transform point = selectedPoints[i];

            GameObject obj = Instantiate(selectedPrefabs[i], point);

            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;

            Paper paper = obj.GetComponent<Paper>();
            paper.SetInputHandler(inputHandler);
            paper.GetInteractionText(paperText);
            activePapers.Add(paper);
        }

        return activePapers;
    }
}
