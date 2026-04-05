using System.Collections.Generic;
using System.Linq;
using InputSystem;
using UnityEngine;

public class PaperSpawner : MonoBehaviour
{
    public Transform[] spawnPoints; // 8 จุด
    public GameObject paperPrefab;
    public InputHandler inputHandler;

    public List<Paper> SpawnPapers()
    {
        List<Transform> selected = spawnPoints
            .OrderBy(x => Random.value)
            .Take(4)
            .ToList();

        List<int> numbers = new List<int> {1,2,3,4}
            .OrderBy(x => Random.value)
            .ToList();

        List<Paper> papers = new List<Paper>();

        for (int i = 0; i < 4; i++)
        {
            GameObject obj = Instantiate(paperPrefab, selected[i].position, Quaternion.identity);
            Paper paper = obj.GetComponent<Paper>();
            paper.SetNumber(numbers[i]);
            paper.SetInputHandler(inputHandler);
            papers.Add(paper);
        }

        return papers;
    }
}
