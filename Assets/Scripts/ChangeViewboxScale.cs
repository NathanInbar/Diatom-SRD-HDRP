using UnityEngine;

[ExecuteAlways]
public class ChangeViewboxScale : MonoBehaviour
{
    [Range(0.1f, 1000f)]
    public float size = 1f;

    private float _lastSize = -1f;

    void Update()
    {
        if (Mathf.Approximately(size, _lastSize)) return;

        _lastSize = size;
        transform.localScale = Vector3.one * size;
    }
}
