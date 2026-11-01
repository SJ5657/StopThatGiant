using System.Collections.Generic;
using UnityEngine;

// 중세 맵 마을 밖 언덕 지형. 높이는 노이즈로 계산 (Height): 성벽 가까이는 평평, 멀어질수록 언덕, 가장자리는 산 쪽으로 솟음.
// 성문 길을 따라서는 낮은 골짜기, 시골 마을·야영지 자리는 평평하게 다짐(AddFlat). 흙길은 지형 셰이더가 바닥에 그림.
// Wilderness가 Setup → AddFlat → Build 순서로 부르고, 소품·동물·거인 이외의 높이는 Height로 맞춤.
// 지형 조각은 Default 레이어 + MeshCollider (거인이 위를 걸음, ItemDrops.GroundY 레이캐스트에도 걸림).
public static class WildTerrain
{
    public static bool Active => built && GameEra.Medieval; // 게임 안에서 현대 도시 맵으로 바꾸면 꺼짐 (static이라 씬이 바뀌어도 남으므로)
    static bool built;
    static float innerX, innerZ, roadX, roadZ, seedX, seedZ;
    static readonly List<(Vector2 c, float r, float h)> flats = new List<(Vector2, float, float)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { built = false; flats.Clear(); }

    public static void Setup(float wallX, float wallZ, float gateRoadX, float gateRoadZ, int seed)
    {
        innerX = wallX; innerZ = wallZ; roadX = gateRoadX; roadZ = gateRoadZ;
        var r = new System.Random(seed);
        seedX = (float)r.NextDouble() * 1000f; seedZ = (float)r.NextDouble() * 1000f;
        flats.Clear();
        built = true;
    }

    // 이 자리를 반경 r 안에서 평평하게 (가운데 높이로)
    public static void AddFlat(Vector2 c, float r) => flats.Add((c, r, Raw(c.x, c.y)));

    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    // 성문 길(마을 밖 부분) 가운데선까지 거리
    public static float RoadDistance(float x, float z)
    {
        float d = float.MaxValue;
        if (Mathf.Abs(z) > innerZ - 10f) d = Mathf.Min(d, Mathf.Abs(x - roadX));
        if (Mathf.Abs(x) > innerX - 10f) d = Mathf.Min(d, Mathf.Abs(z - roadZ));
        return d;
    }

    static float Raw(float x, float z)
    {
        float n = Mathf.PerlinNoise(x * 0.0016f + seedX, z * 0.0016f + seedZ) * 0.45f
                + Mathf.PerlinNoise(x * 0.0042f + seedX * 0.7f, z * 0.0042f + seedZ * 1.3f) * 0.3f
                + Mathf.PerlinNoise(x * 0.0075f + seedX * 1.3f, z * 0.0075f + seedZ * 0.8f) * 0.15f  // 화면 안에 보이는 작은 언덕
                + Mathf.PerlinNoise(x * 0.012f + seedX * 1.9f, z * 0.012f + seedZ * 0.4f) * 0.1f;
        float hills = Mathf.Max(0f, n - 0.22f) * 175f;                                    // 언덕 (최고 약 110m)
        float edge = Smooth(2300f, 3400f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))) * 150f; // 가장자리는 산 쪽으로 솟음
        float outside = Mathf.Max(Mathf.Abs(x) - innerX, Mathf.Abs(z) - innerZ);       // 성벽 밖으로 떨어진 거리
        float town = Smooth(50f, 380f, outside);                                          // 성벽 가까이는 평평
        float road = Mathf.Lerp(0.3f, 1f, Smooth(15f, 160f, RoadDistance(x, z)));         // 길을 따라 낮은 골짜기
        return -0.1f + (hills * road + edge) * town;
    }

    public static float Height(float x, float z)
    {
        float h = Raw(x, z);
        foreach (var f in flats)
        {
            float d = Vector2.Distance(new Vector2(x, z), f.c);
            if (d < f.r) h = Mathf.Lerp(h, f.h, Smooth(f.r, f.r * 0.55f, d));
        }
        return h;
    }

    public static float Height(Vector3 p) => Height(p.x, p.z);

    // 지형 메시: half(사각형 반) 범위를 cell 간격 격자로, chunk칸씩 나눠 만듦. 마을 안쪽(성벽 안)은 비움.
    // colliderReach 안의 조각만 충돌 메시 (거인이 갈 수 있는 곳)
    public static void Build(Transform parent, Material mat, float half, float cell, int chunk, float colliderReach)
    {
        int n = Mathf.CeilToInt(half * 2f / cell);
        var root = new GameObject("Terrain").transform;
        root.SetParent(parent, false);
        for (int cz = 0; cz < n; cz += chunk)
            for (int cx = 0; cx < n; cx += chunk)
            {
                float x0 = -half + cx * cell, z0 = -half + cz * cell;
                float x1 = x0 + chunk * cell, z1 = z0 + chunk * cell;
                // 조각 전체가 성벽 안쪽(마을)이면 건너뜀
                if (x0 > -innerX + 40f && x1 < innerX - 40f && z0 > -innerZ + 40f && z1 < innerZ - 40f) continue;
                var mesh = ChunkMesh(cx, cz, Mathf.Min(chunk, n - cx), Mathf.Min(chunk, n - cz), half, cell);
                if (!mesh) continue;
                var go = new GameObject($"Terrain_{cx}_{cz}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                // 조각에서 마을 중심에 가장 가까운 점이 거인 이동 범위 안이면 충돌 메시
                float nx = x0 < 0f && x1 > 0f ? 0f : Mathf.Min(Mathf.Abs(x0), Mathf.Abs(x1));
                float nz = z0 < 0f && z1 > 0f ? 0f : Mathf.Min(Mathf.Abs(z0), Mathf.Abs(z1));
                if (nx < colliderReach && nz < colliderReach) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                else mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
    }

    static Mesh ChunkMesh(int cx, int cz, int w, int d, float half, float cell)
    {
        var verts = new Vector3[(w + 1) * (d + 1)];
        for (int j = 0; j <= d; j++)
            for (int i = 0; i <= w; i++)
            {
                float x = -half + (cx + i) * cell, z = -half + (cz + j) * cell;
                verts[j * (w + 1) + i] = new Vector3(x, Height(x, z), z);
            }
        var tris = new List<int>();
        for (int j = 0; j < d; j++)
            for (int i = 0; i < w; i++)
            {
                // 칸 네 꼭짓점이 모두 성벽 안쪽(마을 바닥 아래)이면 비움
                float x = -half + (cx + i + 0.5f) * cell, z = -half + (cz + j + 0.5f) * cell;
                if (Mathf.Abs(x) < innerX - 40f && Mathf.Abs(z) < innerZ - 40f) continue;
                int a = j * (w + 1) + i, b = a + 1, c = a + w + 1, e = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(e);
            }
        if (tris.Count == 0) return null;
        var m = new Mesh { name = "WildTerrain" };
        if (verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = verts;
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
