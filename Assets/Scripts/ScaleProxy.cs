using UnityEngine;
using System;

public class ScaleProxy : MonoBehaviour
{
    public event Action<float> OnScaleChanged;

    private float _previousScale;

    void Start()
    {
        _previousScale = transform.localScale.x;
    }

    void Update()
    {
        float currentScale = transform.localScale.x;
        if (!Mathf.Approximately(currentScale, _previousScale))
        {
            _previousScale = currentScale;
            OnScaleChanged?.Invoke(currentScale);
        }
    }

}
