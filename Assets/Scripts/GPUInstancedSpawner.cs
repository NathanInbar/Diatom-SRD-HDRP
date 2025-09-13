using UnityEngine;

public class GPUInstancedSpawner : MonoBehaviour {
    [Header("Assign in Inspector")]
    public Mesh mesh;
    public Material material;
    [Range(1, 100000)] public int instanceCount = 20000;
    public Vector3 areaSize = new Vector3(200, 0, 200);

    const int kBatch = 1023;
    Matrix4x4[] matrices;
    Vector4[] colors;
    MaterialPropertyBlock mpb;

    void OnEnable() {
        matrices = new Matrix4x4[instanceCount];
        colors = new Vector4[instanceCount];
        var rnd = new System.Random(123);

        for (int i = 0; i < instanceCount; i++) {
            var pos = new Vector3(
                (float)rnd.NextDouble() * areaSize.x - areaSize.x * 0.5f,
                (float)rnd.NextDouble() * Mathf.Max(1f, areaSize.y),
                (float)rnd.NextDouble() * areaSize.z - areaSize.z * 0.5f
            );
            var rot = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
            var scale = Vector3.one;
            matrices[i] = Matrix4x4.TRS(pos, rot, scale);

            colors[i] = new Color(
                (float)rnd.NextDouble(),
                (float)rnd.NextDouble(),
                (float)rnd.NextDouble(),
                1f
            );
        }

        mpb = new MaterialPropertyBlock();
        mpb.SetVectorArray("_BaseColor", colors); // must exist in shader as an instanced property
    }

    void Update() {
        if (!mesh || !material) return;

        for (int i = 0; i < instanceCount; i += kBatch) {
            int count = Mathf.Min(kBatch, instanceCount - i);
            Graphics.DrawMeshInstanced(mesh, 0, material,
                new System.ArraySegment<Matrix4x4>(matrices, i, count).Array, count, mpb,
                UnityEngine.Rendering.ShadowCastingMode.On, true, 0, null,
                UnityEngine.Rendering.LightProbeUsage.BlendProbes, null);
        }
    }
}
