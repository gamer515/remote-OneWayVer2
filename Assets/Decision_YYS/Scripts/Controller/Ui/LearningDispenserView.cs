using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Displays the existing learning jobs on the supplied 3D dispenser; owns no economy or timers.</summary>
public sealed class LearningDispenserView : MonoBehaviour
{
    [SerializeField] private MineLearningStation station;
    [SerializeField] private Transform queueBottom, dynamicRoot;
    [SerializeField] private Vector3 queueStep = new Vector3(0,.126f,0);
    [SerializeField] private GameObject queuedTilePrefab;
    [SerializeField] private GameObject[] learningBookPrefabs;
    [SerializeField] private Transform[] outletPath;
    [SerializeField, Min(.01f)] private float deliverySeconds = .6f;
    [SerializeField] private Vector3 activeStep = new Vector3(0,.32f,0);
    [SerializeField] private TMP_Text activeTimer;
    private readonly List<Transform> tiles = new List<Transform>();
    private readonly Dictionary<MineLearningJob,Transform> books = new Dictionary<MineLearningJob,Transform>();
    private readonly List<MineLearningJob> finished = new List<MineLearningJob>();
    public int QueuedVisualCount => tiles.Count;
    public int ActiveVisualCount => books.Count;

    private void LateUpdate()
    {
        var progress = station.Progress;
        if(progress == null) return;
        while(tiles.Count > progress.queued.Count)
        {
            var last=tiles[tiles.Count-1];tiles.RemoveAt(tiles.Count-1);Remove(last);
        }
        while(tiles.Count < progress.queued.Count)
            tiles.Add(Instantiate(queuedTilePrefab,dynamicRoot).transform);
        for(int i=0;i<tiles.Count;i++)
            tiles[i].SetPositionAndRotation(queueBottom.TransformPoint(queueStep*i),queueBottom.rotation);
        finished.Clear();
        foreach(var job in books.Keys) if(!progress.active.Contains(job)) finished.Add(job);
        foreach(var job in finished) {Remove(books[job]);books.Remove(job);}
        for(int i=0;i<progress.active.Count;i++)
        {
            var job=progress.active[i];
            if(!books.TryGetValue(job,out var book))
            {
                book=Instantiate(learningBookPrefabs[job.ability],dynamicRoot).transform;
                books.Add(job,book);
            }
            float t=job.transit>0?Mathf.Clamp01(1-job.transit/deliverySeconds):1;
            float path=t*(outletPath.Length-1);int segment=Mathf.Min((int)path,outletPath.Length-2);
            Vector3 position=Vector3.Lerp(outletPath[segment].position,outletPath[segment+1].position,path-segment);
            if(job.transit<=0)position+=dynamicRoot.TransformVector(activeStep*i);
            book.SetPositionAndRotation(position,transform.rotation);
        }
        if(activeTimer!=null)
            activeTimer.text=progress.active.Count==0?string.Empty:
                progress.active[0].transit>0?"배출 중":progress.active[0].remaining.ToString("F1")+"초";
    }
    private void Remove(Transform item)
    {
        item.gameObject.SetActive(false);Destroy(item.gameObject);
    }
}
