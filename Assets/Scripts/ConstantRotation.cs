using UnityEngine;

public class ConstantRotation : MonoBehaviour
{
    [System.Flags]
    public enum Axis
    {
        X = 1 << 0,
        Y = 1 << 1,
        Z = 1 << 2
    }

    public Axis rotationAxis;
    public float rotationSpeed = 10f; // degrees per second

    private Vector3 rotationDirection;

    private void Start()
    {
        if ((rotationAxis & Axis.X) != 0)
            rotationDirection.x = 1f;
        if ((rotationAxis & Axis.Y) != 0)
            rotationDirection.y = 1f;
        if ((rotationAxis & Axis.Z) != 0)
            rotationDirection.z = 1f;
    }

    private void Update()
    {
        transform.Rotate(rotationDirection * rotationSpeed * Time.deltaTime);
    }
}
