using UnityEngine;

public class GRD_MinimalSpawner : MonoBehaviour
{
    [Header("Assign a Mesh with reasonable vertex count and an HDRP Lit/Unlit Material")]
    public Mesh mesh;
    public Material material;

    [Range(1, 200000)] public int count = 20000;
    public Vector3 area = new Vector3(200, 0, 200);
    public uint seed = 123;

    void OnEnable()
    {
        if (!mesh || !material) return;

        var rnd = new System.Random((int)seed);

        for (int i = 0; i < count; i++)
        {
            // Create a simple MeshRenderer + MeshFilter instance
            var go = new GameObject("GRD_Inst_" + i);
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material; // no MaterialPropertyBlock, no per-renderer keywords

            // Random transform
            var pos = new Vector3(
                (float)rnd.NextDouble() * area.x - 0.5f * area.x,
                0f,
                (float)rnd.NextDouble() * area.z - 0.5f * area.z
            );
            var rot = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one;
        }

        // GRD picks up created GameObjects this frame and draws them via GPU instancing.
        // No extra API calls needed.
    }
}
