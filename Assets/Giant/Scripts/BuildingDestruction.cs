using UnityEngine;

// 건물을 '깨진 유리' 파편으로 부수는 매니저.
// 물리엔진(Rigidbody) 없이 간단한 탄도 계산으로 움직이고, GPU 인스턴싱으로 한꺼번에 그림 (GameObject 생성 없음).
public class BuildingDestruction : MonoBehaviour
{
    public static BuildingDestruction Instance { get; private set; }

    [Header("파편")]
    public Material shardMaterial;           // City/GlassShard (GPU Instancing 켜짐)
    public int maxShards = 8000;
    public int shardVariants = 8;             // 서로 다른 모양 개수
    public int minShardsPerBuilding = 30;
    public int maxShardsPerBuilding = 70;
    public Vector2 shardSizeRange = new Vector2(7f, 20f);
    public float lifetime = 6f;
    public float gravity = 30f;
    public float groundY = 0.3f;
    public float pushForce = 1.0f;
    public float upForce = 22f;
    [Range(0, 1)] public float colorTintFromBuilding = 0.35f; // 건물 색 반영 비율

    [Header("먼지")]
    public bool dust = true;
    public Material dustMaterial;

    public int DestroyedCount { get; private set; }

    // 파편 데이터 (구조체 배열 = GC 없음)
    struct Shard
    {
        public Vector3 pos, vel, scale, spinAxis;
        public Quaternion rot;
        public float spin, dieAt, life;
        public Vector4 color;
        public int variant;
        public bool resting;
    }
    Shard[] shards;
    int count;              // 활성 파편 수 (0..count-1)
    Mesh[] meshes;
    Matrix4x4[][] batchM;
    Vector4[][] batchC;
    int[] batchN;
    MaterialPropertyBlock mpb, readMpb;
    ParticleSystem dustPs;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        Instance = this;
        shards = new Shard[maxShards];
        mpb = new MaterialPropertyBlock();
        readMpb = new MaterialPropertyBlock();
        meshes = new Mesh[shardVariants];
        batchM = new Matrix4x4[shardVariants][];
        batchC = new Vector4[shardVariants][];
        batchN = new int[shardVariants];
        for (int i = 0; i < shardVariants; i++)
        {
            meshes[i] = MakeShardMesh(i * 7919 + 13);
            batchM[i] = new Matrix4x4[1023];
            batchC[i] = new Vector4[1023];
        }
        if (shardMaterial) shardMaterial.enableInstancing = true;
        if (dust && dustMaterial) dustPs = CreateDust();
    }

    // 불규칙한 얇은 사면체 = 깨진 유리 조각. 면마다 정점을 분리하고 버텍스 컬러에 무게중심 좌표를 넣어 모서리 윤곽선 표현
    static Mesh MakeShardMesh(int seed)
    {
        var r = new System.Random(seed);
        float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
        // 길쭉하고 뾰족한 삼각형
        Vector3 a = new Vector3(R(-0.5f, -0.2f), R(-0.5f, -0.3f), 0);
        Vector3 b = new Vector3(R(0.2f, 0.5f), R(-0.45f, -0.1f), 0);
        Vector3 c = new Vector3(R(-0.25f, 0.25f), R(0.4f, 0.75f), 0);
        Vector3 d = (a + b + c) / 3f + new Vector3(R(-0.1f, 0.1f), R(-0.1f, 0.1f), R(0.08f, 0.16f));
        Vector3 center = (a + b + c + d) / 4f;
        a -= center; b -= center; c -= center; d -= center;

        var v = new Vector3[12]; var col = new Color[12]; var tri = new int[12];
        Vector3[][] faces = { new[] { a, c, b }, new[] { a, b, d }, new[] { b, c, d }, new[] { c, a, d } };
        Color[] bary = { new Color(1, 0, 0), new Color(0, 1, 0), new Color(0, 0, 1) };
        for (int f = 0; f < 4; f++)
            for (int k = 0; k < 3; k++) { v[f * 3 + k] = faces[f][k]; col[f * 3 + k] = bary[k]; tri[f * 3 + k] = f * 3 + k; }
        var m = new Mesh { name = "GlassShard" + seed };
        m.vertices = v; m.colors = col; m.triangles = tri;
        m.RecalculateNormals(); m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        return m;
    }

    // shardMultiplier: 파편/먼지 양 배율 (폭탄처럼 한꺼번에 많이 부술 때 줄여서 사용)
    public static void Break(GameObject building, Vector3 hitPoint, Vector3 push, bool countIt = true, float shardMultiplier = 1f)
    {
        if (Instance == null) { Destroy(building); return; }
        Instance.shardMul = shardMultiplier;
        Instance.dustMul = shardMultiplier;
        Instance.DoBreak(building, hitPoint, push, countIt);
        Instance.shardMul = 1f;
        Instance.dustMul = 1f;
    }

    float shardMul = 1f;

    void DoBreak(GameObject b, Vector3 hitPoint, Vector3 push, bool countIt)
    {
        if (!b.activeInHierarchy) return;
        foreach (Transform child in b.transform)
            if (child.gameObject.activeSelf && child.GetComponent<Collider>()) SpawnShards(child, hitPoint, push);
        SpawnShards(b.transform, hitPoint, push);
        b.SetActive(false);
        Destroy(b, 0.1f);
        if (countIt) DestroyedCount++;
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.25f);
    }

    void SpawnShards(Transform t, Vector3 hitPoint, Vector3 push)
    {
        Vector3 size = t.lossyScale;
        Vector3 c = t.position;
        Color baseCol = Color.white;
        var srcR = t.GetComponent<Renderer>();
        if (srcR)
        {
            srcR.GetPropertyBlock(readMpb);
            baseCol = readMpb.isEmpty ? srcR.sharedMaterial.GetColor(ColorId) : (Color)readMpb.GetVector(ColorId);
        }
        Color glass = new Color(0.82f, 0.92f, 1f);
        Color tint = Color.Lerp(glass, baseCol, colorTintFromBuilding);

        float volume = size.x * size.y * size.z;
        int n = Mathf.Max(3, Mathf.RoundToInt(Mathf.Clamp(Mathf.RoundToInt(volume / 250f), minShardsPerBuilding, maxShardsPerBuilding) * shardMul));
        float minDim = Mathf.Min(size.x, Mathf.Min(size.y, size.z));

        for (int k = 0; k < n; k++)
        {
            int idx = Alloc();
            ref Shard s = ref shards[idx];
            s.pos = c + Vector3.Scale(size, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.45f, 0.5f), Random.Range(-0.5f, 0.5f)));
            float sz = Mathf.Clamp(minDim * Random.Range(0.3f, 0.7f), shardSizeRange.x, shardSizeRange.y);
            s.scale = new Vector3(sz * Random.Range(0.7f, 1.3f), sz * Random.Range(0.8f, 1.6f), sz);
            s.rot = Random.rotation;
            s.spinAxis = Random.onUnitSphere;
            s.spin = Random.Range(180f, 720f);
            Vector3 away = s.pos - hitPoint; away.y = 0;
            float heightFactor = Mathf.Clamp01((s.pos.y - c.y) / Mathf.Max(size.y, 0.01f) + 0.5f);
            s.vel = away.normalized * Random.Range(4f, 16f) + push * pushForce * Random.Range(0.3f, 1f)
                    + Vector3.up * upForce * Random.Range(0.3f, 1f) * (0.4f + heightFactor);
            s.life = lifetime * Random.Range(0.8f, 1.2f);
            s.dieAt = Time.time + s.life;
            float g = Random.Range(0.85f, 1.1f);
            s.color = new Vector4(tint.r * g, tint.g * g, tint.b * g, 1);
            s.variant = Random.Range(0, shardVariants);
            s.resting = false;
        }

        if (dustPs) EmitDust(c, size);
    }

    [Header("먼지 양")]
    public int dustPerBuilding = 8;
    public float dustSizeScale = 1.4f;

    // 건물 바닥 주변으로 퍼지는 먼지 + 위로 피어오르는 먼지 기둥
    float dustMul = 1f;
    void EmitDust(Vector3 c, Vector3 size)
    {
        float foot = Mathf.Max(size.x, size.z);
        float groundY0 = c.y - size.y * 0.5f;
        int n = Mathf.RoundToInt((dustPerBuilding + foot / 20f) * dustMul);
        for (int i = 0; i < n; i++)
        {
            bool plume = i < n / 3;
            Vector3 pos = new Vector3(c.x + Random.Range(-0.5f, 0.5f) * size.x, groundY0 + (plume ? Random.Range(0.2f, 0.9f) * size.y : Random.Range(0f, 0.25f) * size.y), c.z + Random.Range(-0.5f, 0.5f) * size.z);
            Vector3 outDir = pos - c; outDir.y = 0; outDir = outDir.sqrMagnitude > 0.01f ? outDir.normalized : Random.insideUnitSphere;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = plume ? Vector3.up * Random.Range(3f, 8f) + outDir * Random.Range(1f, 4f)
                                 : outDir * Random.Range(8f, 20f) + Vector3.up * Random.Range(0.5f, 3f),
                startSize = foot * dustSizeScale * Random.Range(0.5f, 1.1f),
                startLifetime = Random.Range(3f, 5.5f),
                rotation = Random.Range(0f, 360f),
                startColor = new Color32((byte)Random.Range(215, 240), (byte)Random.Range(212, 236), (byte)Random.Range(205, 228), (byte)Random.Range(90, 135))
            };
            dustPs.Emit(ep, 1);
        }
    }

    // 빈 슬롯 확보 (가득 차면 가장 오래된 것부터 덮어씀)
    int oldestCursor;
    int Alloc()
    {
        if (count < maxShards) return count++;
        oldestCursor = (oldestCursor + 1) % maxShards;
        return oldestCursor;
    }

    void Update()
    {
        float dt = Time.deltaTime, now = Time.time;
        for (int v = 0; v < shardVariants; v++) batchN[v] = 0;

        for (int i = 0; i < count; i++)
        {
            ref Shard s = ref shards[i];
            float left = s.dieAt - now;
            if (left <= 0)
            {
                // 마지막 요소와 교체해서 제거
                count--;
                shards[i] = shards[count];
                if (oldestCursor >= count) oldestCursor = 0;
                i--;
                continue;
            }

            if (!s.resting)
            {
                s.vel.y -= gravity * dt;
                s.pos += s.vel * dt;
                s.rot = Quaternion.AngleAxis(s.spin * dt, s.spinAxis) * s.rot;
                float floor = groundY + s.scale.z * 0.15f;
                if (s.pos.y <= floor)
                {
                    s.pos.y = floor;
                    if (Mathf.Abs(s.vel.y) < 4f) { s.resting = true; s.vel = Vector3.zero; }
                    else { s.vel.y = -s.vel.y * 0.25f; s.vel.x *= 0.5f; s.vel.z *= 0.5f; s.spin *= 0.4f; }
                }
            }

            float shrink = left < 1f ? left : 1f;
            int vi = s.variant;
            int bn = batchN[vi];
            batchM[vi][bn] = Matrix4x4.TRS(s.pos, s.rot, s.scale * shrink);
            batchC[vi][bn] = s.color;
            batchN[vi] = bn + 1;
            if (batchN[vi] == 1023) Flush(vi);
        }
        for (int v = 0; v < shardVariants; v++) if (batchN[v] > 0) Flush(v);
    }

    void Flush(int v)
    {
        if (shardMaterial)
        {
            mpb.Clear();
            mpb.SetVectorArray(ColorId, batchC[v]);
            Graphics.DrawMeshInstanced(meshes[v], 0, shardMaterial, batchM[v], batchN[v], mpb,
                UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
        batchN[v] = 0;
    }

    ParticleSystem CreateDust()
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f);
        main.startColor = new Color(0.8f, 0.8f, 0.8f, 0.55f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4000;
        main.gravityModifier = -0.02f;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 4f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                     new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1) });
        col.color = grad;
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.3f, 1.2f), new Keyframe(1, 2f)));
        var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 0f; lv.dampen = 0.04f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial;
        ps.Play();
        return ps;
    }
}
