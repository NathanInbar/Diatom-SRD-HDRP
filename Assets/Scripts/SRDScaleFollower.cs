using UnityEngine;

public class SRDScaleFollower : MonoBehaviour
{
    public ScaleProxy source;

    void OnEnable()
    {
        if (source != null)
            source.OnScaleChanged += UpdateScale;
    }

    void OnDisable()
    {
        if (source != null)
            source.OnScaleChanged -= UpdateScale;
    }

    void UpdateScale(float newScale)
    {
        //TODO - put the SRD scale effect here 
        gameObject.transform.localScale = new Vector3(newScale, newScale, newScale);
    }
}
