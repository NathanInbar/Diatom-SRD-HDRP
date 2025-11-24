using UnityEngine;
using UnityEngine.Playables;

public class LoopTimeline : MonoBehaviour
{
    [SerializeField]
    PlayableDirector director;
    void Start()
    {
        director.stopped += LoopOnStop;    
    }
    private void LoopOnStop(PlayableDirector obj)
    {
        obj.time = 0;
        obj.Play();
    }
}
