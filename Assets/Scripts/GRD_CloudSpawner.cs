using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class GRD_CloudSpawner : MonoBehaviour
{
    [Header("Meshes / Material (HDRP Lit/Unlit)")]
    public List<Mesh> meshes = new List<Mesh>();
    public Material material;

    [Header("Count / Area")]
    [Range(1, 200000)] public int count = 20000;

    [Tooltip("X = width, Y = height (0 for flat), Z = depth. Centered on this transform.")]
    public Vector3 area = new Vector3(200f, 0f, 200f);

    [Tooltip("Local-space vertical offset of the area center.")]
    public float areaYOffset = 0f;

    [Header("Scale Settings")]
    [Min(0f)] public float minScale = 1f;
    [Min(0f)] public float maxScale = 1f;

    [Header("Random Seed")]
    public uint seed = 123;

    [Header("Density")]
    [Tooltip("Evaluated on normalized radius [0,1]. Value = acceptance probability.")]
    public AnimationCurve densityOverRadius = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Range(1, 256)]
    public int maxAttemptsPerInstance = 48;

    [Header("Gizmos")]
    public bool drawGizmos = true;
    public bool drawWire = true;
    public bool drawSolid = false;
    public Color gizmoColor = new Color(0f, 1f, 0f, 0.15f);
    public Color gizmoWireColor = new Color(0f, 0.8f, 0f, 1f);

    private readonly List<Transform> _spawned = new();
    private const string InstancePrefix = "GRD_Inst_";

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (meshes == null || meshes.Count == 0 || !material) return;

        Spawn();
    }

    void OnDisable() => ClearSpawned();
    void OnDestroy() => ClearSpawned();

    private void Spawn()
    {
        ClearSpawned();

        var rnd = new System.Random((int)seed);
        float w = Mathf.Max(0f, area.x);
        float h = Mathf.Max(0f, area.y);
        float d = Mathf.Max(0f, area.z);

        float sMin = Mathf.Min(minScale, maxScale);
        float sMax = Mathf.Max(minScale, maxScale);

        for (int i = 0; i < count; i++)
        {
            Vector3 localPos = SamplePositionWithDensity(rnd, w, h, d);
            float yaw = (float)rnd.NextDouble() * 360f;
            float s = Mathf.Lerp(sMin, sMax, (float)rnd.NextDouble());

            var go = new GameObject(InstancePrefix + i);
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = meshes[rnd.Next(meshes.Count)];

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;

            go.transform.localPosition = new Vector3(localPos.x, areaYOffset + localPos.y, localPos.z);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(s, s, s);

            _spawned.Add(go.transform);
        }
    }

    private Vector3 SamplePositionWithDensity(System.Random rnd, float w, float h, float d)
    {
        float hx = 0.5f * w;
        float hy = 0.5f * Mathf.Max(h, 0f);
        float hz = 0.5f * d;
        bool flat = h <= Mathf.Epsilon;
        Vector3 candidate = Vector3.zero;

        for (int attempt = 0; attempt < maxAttemptsPerInstance; attempt++)
        {
            float x = (float)rnd.NextDouble() * w - hx;
            float z = (float)rnd.NextDouble() * d - hz;
            float y = flat ? 0f : ((float)rnd.NextDouble() * h - hy);
            candidate = new Vector3(x, y, z);

            float rx = (hx > 0f) ? Mathf.Abs(x) / hx : 0f;
            float ry = flat ? 0f : ((hy > 0f) ? Mathf.Abs(y) / hy : 0f);
            float rz = (hz > 0f) ? Mathf.Abs(z) / hz : 0f;
            float r = flat ? Mathf.Max(rx, rz) : Mathf.Max(rx, ry, rz);

            float acceptProb = Mathf.Clamp01(densityOverRadius.Evaluate(Mathf.Clamp01(r)));
            if ((float)rnd.NextDouble() <= acceptProb)
                return candidate;
        }
        return candidate;
    }

    private void ClearSpawned()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i])
                DestroySmart(_spawned[i].gameObject);
        }
        _spawned.Clear();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c && c.name.StartsWith(InstancePrefix))
                DestroySmart(c.gameObject);
        }
    }

    private static void DestroySmart(Object obj)
    {
        if (!obj) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        var prevMatrix = Gizmos.matrix;
        var prevColor = Gizmos.color;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

        float w = Mathf.Max(0f, area.x);
        float h = Mathf.Max(0f, area.y);
        float d = Mathf.Max(0f, area.z);

        Vector3 center = new Vector3(0f, areaYOffset, 0f);
        Vector3 size = new Vector3(w, Mathf.Max(h, 0.001f), d);

        if (drawSolid)
        {
            Gizmos.color = gizmoColor;
            Gizmos.DrawCube(center, size);
        }

        if (drawWire)
        {
            Gizmos.color = gizmoWireColor;
            Gizmos.DrawWireCube(center, new Vector3(w, Mathf.Max(h, 0.001f), d));
        }

        Gizmos.matrix = prevMatrix;
        Gizmos.color = prevColor;
    }
}
