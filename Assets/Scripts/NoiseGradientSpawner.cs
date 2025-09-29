// CurveNoiseSpawner.cs
// Editor-time spawner: places EXACTLY `totalInstances` objects chosen randomly from `prefabs`
// inside an oriented box. Box position/rotation from this Transform; SIZE from explicit `boxSize` (world units).
// Height distribution = AnimationCurve PDF over [0..1]. Positions jittered by fractal Perlin.
// Performance: optional prefab linking/Undo; O(1) average spacing check via hashed 3D grid; minimal hierarchy churn.

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class CurveNoiseSpawner : MonoBehaviour
{
    [Header("Volume (Transform pos/rot; explicit size)")]
    [Tooltip("World-space box size (X,Y,Z). Independent of Transform scale.")]
    public Vector3 boxSize = new Vector3(10, 5, 10);
    public bool drawSolidGizmo = true;
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.08f);
    public Color gizmoWire  = new Color(0.2f, 0.8f, 1f, 0.9f);

    [Header("Distribution")]
    [Tooltip("AnimationCurve used as a PDF over normalized height [0..1]. Values < 0 are clamped to 0. Area normalized.")]
    public AnimationCurve heightPdf = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
    [Min(0)] public int totalInstances = 250;

    [Header("Noise Displacement (Perlin-based)")]
    [Tooltip("Frequency in cycles per box dimension.")]
    public float frequency = 1.5f;
    [Tooltip("Displacement amplitude in local units (per axis).")]
    public Vector3 amplitude = new Vector3(0.2f, 0.1f, 0.2f);
    [Range(1, 6)] public int octaves = 3;
    public float lacunarity = 2.0f;
    public float gain = 0.5f;
    public int seed = 12345;

    [Header("Collision/Clumping Control")]
    [Tooltip("If > 0, reject candidates closer than this (world units).")]
    public float minSpacing = 0f;
    [Tooltip("Safety cap on attempts when spacing is enforced.")]
    public int maxAttemptsMultiplier = 30;

    [Header("Prefabs")]
    [Tooltip("Uniform random choice per spawn from this list.")]
    public List<GameObject> prefabs = new List<GameObject>();

    [Header("Output")]
    [Tooltip("Instances parented under this GameObject.")]
    public string containerName = "__Spawned";

    [Header("Editor Perf")]
    [Tooltip("Use PrefabUtility.InstantiatePrefab to keep prefab links. OFF is fastest.")]
    public bool linkPrefabAssets = false;
    [Tooltip("Record Undo for created instances. OFF is fastest.")]
    public bool recordUndo = false;

    Transform _tx;

    void Awake() { _tx = transform; }
    void Reset() { _tx = transform; }

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(containerName)) containerName = "__Spawned";
        if (boxSize.x <= 0f) boxSize.x = 0.0001f;
        if (boxSize.y <= 0f) boxSize.y = 0.0001f;
        if (boxSize.z <= 0f) boxSize.z = 0.0001f;
        if (lacunarity < 1f) lacunarity = 1f;
        if (gain <= 0f) gain = 0.0001f;
        if (frequency <= 0f) frequency = 0.0001f;
        if (octaves < 1) octaves = 1;
        if (totalInstances < 0) totalInstances = 0;
    }

    void OnDrawGizmos()
    {
        if (_tx == null) _tx = transform;
        var c = _tx.position;
        var r = _tx.rotation;
        var s = Abs(boxSize);
        Gizmos.matrix = Matrix4x4.TRS(c, r, Vector3.one);
        if (drawSolidGizmo) { Gizmos.color = gizmoColor; Gizmos.DrawCube(Vector3.zero, s); }
        Gizmos.color = gizmoWire; Gizmos.DrawWireCube(Vector3.zero, s);
    }

    static Vector3 Abs(in Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    GameObject GetOrCreateContainer()
    {
        var child = _tx.Find(containerName);
        if (child) return child.gameObject;
        var go = new GameObject(containerName);
#if UNITY_EDITOR
        if (recordUndo) Undo.RegisterCreatedObjectUndo(go, "Create Spawn Container");
#endif
        go.transform.SetParent(_tx, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    public void ClearSpawned()
    {
        var child = _tx.Find(containerName);
        if (!child) return;
#if UNITY_EDITOR
        if (recordUndo) Undo.DestroyObjectImmediate(child.gameObject);
        else
#endif
        DestroyImmediate(child.gameObject);
    }

    public void Spawn()
    {
        var validPrefabs = CollectValidPrefabs(prefabs);
        if (validPrefabs.Count == 0) { Debug.LogError("No valid prefabs supplied."); return; }

        var size = Abs(boxSize);
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f) { Debug.LogWarning("boxSize must be > 0 on all axes."); return; }

        var container = GetOrCreateContainer();
        var trs = Matrix4x4.TRS(_tx.position, _tx.rotation, Vector3.one);

        // Build CDF from heightPdf
        const int RES = 256;
        float[] cdf = new float[RES];
        float sum = 0f;
        for (int i = 0; i < RES; i++)
        {
            float t = (i + 0.5f) / RES;
            float v = Mathf.Max(0f, heightPdf.Evaluate(t));
            sum += v;
            cdf[i] = sum;
        }
        if (sum <= 1e-8f) { Debug.LogWarning("Height curve integrates to ~0. Nothing to spawn."); return; }
        for (int i = 0; i < RES; i++) cdf[i] /= sum;

        var rng = new System.Random(seed);
        float Next01() => (float)rng.NextDouble();

        // Spacing grid (LOCAL space)
        Dictionary<Vector3Int, List<Vector3>> grid = null;
        float cellSize = 0f;
        float spacing2 = 0f;
        if (minSpacing > 0f)
        {
            cellSize = Mathf.Max(1e-4f, minSpacing);
            spacing2 = minSpacing * minSpacing;
            grid = new Dictionary<Vector3Int, List<Vector3>>(Mathf.CeilToInt(totalInstances * 1.1f));
        }

        Vector3 o1 = HashVec(seed + 11);
        Vector3 o2 = HashVec(seed + 23);
        Vector3 o3 = HashVec(seed + 37);

#if UNITY_EDITOR
        int undoGroup = -1;
        if (recordUndo)
        {
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("CurveNoiseSpawner Spawn");
        }
#endif

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = Mathf.Max(totalInstances * Mathf.Max(3, maxAttemptsMultiplier), totalInstances);

        while (spawned < totalInstances && attempts < maxAttempts)
        {
            attempts++;

            float fy = SampleCdf(cdf, Next01()); // [0..1]
            float fx = Next01();
            float fz = Next01();

            Vector3 local = new Vector3(fx - 0.5f, fy - 0.5f, fz - 0.5f);
            local = Vector3.Scale(local, size);

            Vector3 n = FractalPerlin3(local, size, frequency, octaves, lacunarity, gain, o1, o2, o3);
            Vector3 disp = new Vector3(n.x - 0.5f, n.y - 0.5f, n.z - 0.5f);
            disp = Vector3.Scale(disp, amplitude);

            Vector3 localDisplaced = local + disp;

            // Clamp to interior
            Vector3 half = size * 0.5f;
            localDisplaced = new Vector3(
                Mathf.Clamp(localDisplaced.x, -half.x, half.x),
                Mathf.Clamp(localDisplaced.y, -half.y, half.y),
                Mathf.Clamp(localDisplaced.z, -half.z, half.z)
            );

            // Spacing check in LOCAL space using hashed grid
            if (grid != null)
            {
                if (TooCloseLocal(localDisplaced, cellSize, spacing2, grid)) continue;
                var key = Cell(localDisplaced, cellSize);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Vector3>(4);
                list.Add(localDisplaced);
            }

            // Create instance: parented first, no worldPositionStays recompute
            var src = validPrefabs[rng.Next(validPrefabs.Count)];
            GameObject instance = CreateInstance(src, container.transform, linkPrefabAssets);
#if UNITY_EDITOR
            if (recordUndo) Undo.RegisterCreatedObjectUndo(instance, "Spawn Instance");
#endif

            // Local placement directly
            instance.transform.localPosition = localDisplaced;  // local to container whose TRS == this Transform
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // World orientation handled by parent (container under this Transform with identity local TRS)
            spawned++;
        }

#if UNITY_EDITOR
        if (recordUndo) Undo.CollapseUndoOperations(undoGroup);
#endif

        if (spawned < totalInstances)
            Debug.LogWarning($"{nameof(CurveNoiseSpawner)}: Reached attempt cap ({attempts}) with spacing={minSpacing}. Spawned {spawned}/{totalInstances}.");
        else
            Debug.Log($"{nameof(CurveNoiseSpawner)}: Spawned {spawned} instances.");
    }

    // === Instantiation helpers ===
    static List<GameObject> CollectValidPrefabs(List<GameObject> list)
    {
        var result = new List<GameObject>(list != null ? list.Count : 0);
        if (list == null) return result;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (!p) continue;
#if UNITY_EDITOR
            if (!PrefabUtility.IsPartOfPrefabAsset(p))
            {
                Debug.LogWarning($"Skipped non-prefab object: {p.name}");
                continue;
            }
#endif
            result.Add(p);
        }
        return result;
    }

    static GameObject CreateInstance(GameObject src, Transform parent, bool linkPrefab)
    {
#if UNITY_EDITOR
        if (linkPrefab && PrefabUtility.IsPartOfPrefabAsset(src))
            return (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
#endif
        return Object.Instantiate(src, parent, false);
    }

    // === Spacing grid (LOCAL space) ===
    static Vector3Int Cell(Vector3 p, float s)
    {
        return new Vector3Int(
            Mathf.FloorToInt(p.x / s),
            Mathf.FloorToInt(p.y / s),
            Mathf.FloorToInt(p.z / s)
        );
    }

    static bool TooCloseLocal(Vector3 p, float cellSize, float spacing2, Dictionary<Vector3Int, List<Vector3>> g)
    {
        var c = Cell(p, cellSize);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var k = new Vector3Int(c.x + dx, c.y + dy, c.z + dz);
            if (!g.TryGetValue(k, out var list)) continue;
            for (int i = 0; i < list.Count; i++)
                if ((list[i] - p).sqrMagnitude < spacing2) return true;
        }
        return false;
    }

    // === Noise field ===
    static Vector3 FractalPerlin3(Vector3 local, Vector3 boxSize, float baseFreq, int oct, float lac, float g, Vector3 o1, Vector3 o2, Vector3 o3)
    {
        Vector3 nrm = new Vector3(
            boxSize.x > 1e-6f ? local.x / boxSize.x : 0f,
            boxSize.y > 1e-6f ? local.y / boxSize.y : 0f,
            boxSize.z > 1e-6f ? local.z / boxSize.z : 0f
        );

        float ax = 0f, ay = 0f, az = 0f;
        float amp = 1f;
        float freq = baseFreq;

        for (int i = 0; i < oct; i++)
        {
            float nx = Mathf.PerlinNoise(nrm.x * freq + o1.x, nrm.y * freq + o1.y);
            float ny = Mathf.PerlinNoise(nrm.y * freq + o2.y, nrm.z * freq + o2.z);
            float nz = Mathf.PerlinNoise(nrm.z * freq + o3.z, nrm.x * freq + o3.x);

            ax += nx * amp;
            ay += ny * amp;
            az += nz * amp;

            amp *= g;
            freq *= lac;
        }

        float norm = (1f - Mathf.Pow(g, oct)) / (1f - g);
        if (norm <= 1e-6f) norm = 1f;
        return new Vector3(ax / norm, ay / norm, az / norm);
    }

    static float SampleCdf(float[] cdf, float u)
    {
        int lo = 0, hi = cdf.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (u <= cdf[mid]) hi = mid; else lo = mid + 1;
        }
        return (lo + 0.5f) / cdf.Length;
    }

    static Vector3 HashVec(int s)
    {
        uint x = (uint)s * 747796405u + 2891336453u;
        uint y = x ^ 0x68E31DA4u;
        uint z = y ^ 0xB5297A4Du;
        return new Vector3(Frac(Hash(x)), Frac(Hash(y)), Frac(Hash(z)));
    }
    static float Hash(uint x)
    {
        x ^= x >> 17; x *= 0xED5AD4BBu;
        x ^= x >> 11; x *= 0xAC4C1B51u;
        x ^= x >> 15; x *= 0x31848BABu;
        x ^= x >> 14;
        return x * (1.0f / 4294967296.0f);
    }
    static float Frac(float v) => v - Mathf.Floor(v);
}

#if UNITY_EDITOR
[CustomEditor(typeof(CurveNoiseSpawner))]
public sealed class CurveNoiseSpawnerEditor : Editor
{
    SerializedProperty boxSize, drawSolidGizmo, gizmoColor, gizmoWire;
    SerializedProperty heightPdf, totalInstances;
    SerializedProperty frequency, amplitude, octaves, lacunarity, gain, seed;
    SerializedProperty minSpacing, maxAttemptsMultiplier;
    SerializedProperty prefabs, containerName;
    SerializedProperty linkPrefabAssets, recordUndo;

    void OnEnable()
    {
        boxSize        = serializedObject.FindProperty("boxSize");
        drawSolidGizmo = serializedObject.FindProperty("drawSolidGizmo");
        gizmoColor     = serializedObject.FindProperty("gizmoColor");
        gizmoWire      = serializedObject.FindProperty("gizmoWire");

        heightPdf      = serializedObject.FindProperty("heightPdf");
        totalInstances = serializedObject.FindProperty("totalInstances");

        frequency      = serializedObject.FindProperty("frequency");
        amplitude      = serializedObject.FindProperty("amplitude");
        octaves        = serializedObject.FindProperty("octaves");
        lacunarity     = serializedObject.FindProperty("lacunarity");
        gain           = serializedObject.FindProperty("gain");
        seed           = serializedObject.FindProperty("seed");

        minSpacing     = serializedObject.FindProperty("minSpacing");
        maxAttemptsMultiplier = serializedObject.FindProperty("maxAttemptsMultiplier");

        prefabs        = serializedObject.FindProperty("prefabs");
        containerName  = serializedObject.FindProperty("containerName");

        linkPrefabAssets = serializedObject.FindProperty("linkPrefabAssets");
        recordUndo       = serializedObject.FindProperty("recordUndo");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Volume", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(boxSize);
        EditorGUILayout.PropertyField(drawSolidGizmo);
        EditorGUILayout.PropertyField(gizmoColor);
        EditorGUILayout.PropertyField(gizmoWire);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Distribution", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(heightPdf, new GUIContent("Height PDF (AnimationCurve)"));
        EditorGUILayout.PropertyField(totalInstances);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Noise Displacement", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(frequency);
        EditorGUILayout.PropertyField(amplitude);
        EditorGUILayout.PropertyField(octaves);
        EditorGUILayout.PropertyField(lacunarity);
        EditorGUILayout.PropertyField(gain);
        EditorGUILayout.PropertyField(seed);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Clumping Control", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(minSpacing);
        EditorGUILayout.PropertyField(maxAttemptsMultiplier);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(prefabs, true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(containerName);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Editor Perf", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(linkPrefabAssets);
        EditorGUILayout.PropertyField(recordUndo);

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Spawn", GUILayout.Height(28)))
            {
                foreach (Object t in targets)
                {
                    var spawner = (CurveNoiseSpawner)t;
                    spawner.Spawn();
                }
            }
            if (GUILayout.Button("Clear", GUILayout.Height(28)))
            {
                foreach (Object t in targets)
                {
                    var spawner = (CurveNoiseSpawner)t;
                    spawner.ClearSpawned();
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
