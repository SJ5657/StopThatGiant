using System.Collections.Generic;
using UnityEngine;

// 중세 맵의 마을 바깥(성벽 밖 풀밭) 꾸미기. 게임을 시작할 때 seed로 매번 같은 모습으로 생성 (마을을 세운 CityGenerator 다음).
//  - 숲(나무·수풀·바위·통나무·풀), 풀밭 곳곳의 풀꽃·바위, 멀리 둘러싼 산 (구매 에셋: LowPolyNature 4 Seasons)
//  - 성문 길을 따라 시골 마을: 집·헛간(마을 집과 같은 규칙이라 부서짐), 밭(작물 줄)·울타리·건초·허수아비·우물·풍차, 양·소 (구매 에셋: LowPolyFarm)
//  - 야생동물(사슴·멧돼지·늑대)과 몬스터(고블린 야영지·트롤·슬라임) → WildCreature
// 나무·풀·작물·울타리·바위 같은 소품은 거인이 지나가면 쓰러지거나 납작해짐 (거인 주변 칸만 검사).
[DefaultExecutionOrder(-800)]
public class Wilderness : MonoBehaviour
{
    public CityGenerator city;
    public int seed = 4242;

    [Header("범위")]
    [Tooltip("성벽에서 이만큼 떨어진 곳부터 꾸밈 (m)")] public float wallGap = 60f;
    [Tooltip("마을 중심에서 이 거리(사각형 반)까지 꾸밈 — 거인이 갈 수 있는 범위 정도")] public float reach = 2550f;
    [Tooltip("멀리 둘러싼 산이 놓이는 거리 (최소, 최대)")] public Vector2 mountainRing = new Vector2(3200f, 4200f);

    [Header("자연 (구매 에셋)")]
    public GameObject[] trees;
    [Tooltip("거대 나무로 쓸 나무 (수관이 넓은 것)")] public GameObject[] bigTrees;
    public GameObject[] bushes;
    public GameObject[] grass;
    public GameObject[] rocks;
    public GameObject[] logs;
    public GameObject[] mountains;
    [Tooltip("풀밭의 완만한 언덕 (납작하게 눌러서 씀)")] public GameObject[] mounds;

    [Header("시골·야영지 (구매 에셋)")]
    public GameObject[] crops;
    public GameObject[] hay;
    public GameObject[] fences;
    public GameObject[] farmProps;
    public GameObject[] tents;
    public GameObject scarecrow, well, windmill, campfire;

    [Header("수량")]
    public int forests = 30;
    [Tooltip("숲 안 나무 사이 간격 (m). 작을수록 빽빽")] public float treeSpacing = 8f;
    public int hamlets = 14;
    [Tooltip("풀·꽃 무더기 수 (0 = 배치 안 함)")] public int meadowClumps = 0;
    [Tooltip("들판의 작은 나무 무리 수")] public int loneTrees = 120;
    [Tooltip("들판에 홀로 선 거대 나무 수 (0 = 없음)")] public int giantTrees = 0;
    [Tooltip("거대 나무 높이 (최소, 최대, m) — 거인 키는 처음에 약 25m")] public Vector2 giantTreeHeight = new Vector2(50f, 115f);
    public int moundCount = 110;
    public int scatteredRocks = 260;
    public int deerHerds = 16, boarGroups = 12, wolfPacks = 9, goblinCamps = 7, trolls = 6, slimeGroups = 12;

    [Tooltip("동물·몬스터 모델 재질 (OutlineBox, 얇은 윤곽선)")] public Material creatureMat;

    [Header("언덕 지형")]
    [Tooltip("지형 재질 (City/Terrain). 비우면 평지")] public Material terrainMat;
    [Tooltip("지형이 덮는 범위 (마을 중심에서 사각형 반, m)")] public float terrainHalf = 4400f;
    [Tooltip("지형 격자 간격 (m)")] public float terrainCell = 30f;

    System.Random r;
    Transform root;
    float innerX, innerZ, roadX, roadZ;
    readonly List<(Vector2 c, float rad)> zones = new List<(Vector2, float)>();

    float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
    T Pick<T>(T[] a) => a == null || a.Length == 0 ? default : a[r.Next(a.Length)]; // 에셋 팩이 없으면(깃에서 받은 경우 등) 그 소품은 건너뜀

    void Awake()
    {
        if (!city) city = FindObjectOfType<CityGenerator>();
        if (!city) return;
        r = new System.Random(seed);
        if (creatureMat) WildCreature.SetBaseMaterial(creatureMat);
        root = new GameObject("WildernessContent").transform;
        root.SetParent(transform, false);

        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 c = city.transform.position;
        innerX = sizeX * 0.5f + 21f + wallGap; innerZ = sizeZ * 0.5f + 21f + wallGap; // 성벽(가장자리 밖 14m, 두께 7m) 바깥
        roadX = c.x - sizeX * 0.5f + city.roadWidth * 0.5f + (city.blocksX / 2) * pitch;
        roadZ = c.z - sizeZ * 0.5f + city.roadWidth * 0.5f + (city.blocksZ / 2) * pitch;

        // 거인 시작 자리는 비워 둠 (마을 밖에서 시작)
        var giant = FindObjectOfType<GiantController>();
        if (giant) { spawn = new Vector2(giant.transform.position.x, giant.transform.position.z); zones.Add((spawn, 70f)); }
        if (terrainMat) WildTerrain.Setup(innerX - wallGap, innerZ - wallGap, roadX, roadZ, seed);

        Build();
        // 거인을 시작 자리(시골 마을 옆) 지형 위에 세움 (땅속에서 시작하지 않게)
        if (giant && !float.IsNaN(spawn.x))
        {
            var p = new Vector3(spawn.x, (WildTerrain.Active ? WildTerrain.Height(spawn.x, spawn.y) : 0f) + 0.3f, spawn.y);
            giant.transform.position = p;
            if (!float.IsNaN(spawnLook.x)) giant.transform.rotation = Quaternion.LookRotation(new Vector3(spawnLook.x - p.x, 0f, spawnLook.y - p.z));
        }
        Debug.Log($"[Wilderness] 소품 {props.Count}개, 동물·몬스터 {WildCreature.All.Count}마리");
    }

    // ───────────── 자리 고르기 ─────────────
    bool Outside(Vector2 p, float margin) => Mathf.Abs(p.x) > innerX + margin || Mathf.Abs(p.y) > innerZ + margin;
    bool InReach(Vector2 p, float margin) => Mathf.Abs(p.x) < reach - margin && Mathf.Abs(p.y) < reach - margin;
    bool OnRoad(Vector2 p, float margin) => (Mathf.Abs(p.x - roadX) < 12f + margin && Mathf.Abs(p.y) > innerZ - 80f) || (Mathf.Abs(p.y - roadZ) < 12f + margin && Mathf.Abs(p.x) > innerX - 80f);
    bool Free(Vector2 p, float rad) { foreach (var z in zones) if ((z.c - p).sqrMagnitude < (z.rad + rad) * (z.rad + rad)) return false; return true; }
    bool Usable(Vector2 p, float rad) => Outside(p, rad) && InReach(p, rad * 0.5f) && !OnRoad(p, rad) && Free(p, rad);

    Vector2 spawn = new Vector2(float.NaN, 0f), spawnLook = new Vector2(float.NaN, 0f);
    [Tooltip("몬스터(고블린 야영지·트롤·늑대)는 거인 시작 자리에서 이 거리(m) 밖에만")] public float monsterSafeRadius = 700f;
    [Tooltip("밟아 쓰러뜨린 나무 하나당 경험치(점수)")] public int trampleXpTree = 6;
    [Tooltip("밟아 부순 바위 하나당 경험치(점수)")] public int trampleXpRock = 4;
    [Tooltip("밟은 덤불·작물 하나당 경험치(점수)")] public int trampleXpSoft = 2;

    Vector2 RandomSpot(float rad, float minFromCenter = 0f, float minFromSpawn = 0f)
    {
        for (int i = 0; i < 80; i++)
        {
            var p = new Vector2(R(-reach, reach), R(-reach, reach));
            if (p.magnitude < minFromCenter) continue;
            if (minFromSpawn > 0f && !float.IsNaN(spawn.x) && (p - spawn).magnitude < minFromSpawn) continue;
            if (Usable(p, rad)) return p;
        }
        return new Vector2(float.NaN, 0f);
    }

    // 땅 위 위치 (언덕 지형이 있으면 그 높이)
    static Vector3 V(Vector2 p) => new Vector3(p.x, WildTerrain.Active ? WildTerrain.Height(p.x, p.y) : 0f, p.y);

    // ───────────── 생성 ─────────────
    readonly List<Vector2> hamletSpots = new List<Vector2>(), campSpots = new List<Vector2>();

    void Build()
    {
        // 1) 시골 마을·야영지 자리를 먼저 정하고 2) 그 자리를 평평하게 다진 지형을 만든 뒤 3) 땅 높이에 맞춰 채움
        PlanHamlets();
        PlanCamps();
        if (WildTerrain.Active)
        {
            if (!float.IsNaN(spawn.x)) WildTerrain.AddFlat(spawn, 110f);
            foreach (var p in hamletSpots) WildTerrain.AddFlat(p, 175f);
            foreach (var p in campSpots) WildTerrain.AddFlat(p, 75f);
            WildTerrain.Build(transform, terrainMat, terrainHalf, terrainCell, 32, reach + 200f);
            terrainMat.SetVector("_Road", new Vector4(roadX, roadZ, innerX - wallGap, innerZ - wallGap));
        }
        for (int i = 0; i < hamletSpots.Count; i++) Hamlet(hamletSpots[i], i);
        for (int i = 0; i < campSpots.Count; i++) Camp(campSpots[i], i);
        for (int i = 0; i < forests; i++)
        {
            float rad = R(130f, 300f);
            var p = RandomSpot(rad * 0.8f);
            if (float.IsNaN(p.x)) continue;
            zones.Add((p, rad * 0.8f));
            Forest(p, rad);
        }
        Meadows();
        Mountains();
        Wildlife();
    }

    // 숲: 몇 개의 둥근 덩어리를 겹쳐 가장자리가 들쭉날쭉한 빽빽한 숲 (나무끼리 treeSpacing 간격)
    void Forest(Vector2 c, float rad)
    {
        int blobs = r.Next(3, 6);
        var centers = new List<(Vector2 c, float r)>();
        for (int i = 0; i < blobs; i++) centers.Add((c + Random2(rad * 0.55f), rad * R(0.45f, 0.7f)));
        float area = 0f; foreach (var b in centers) area += Mathf.PI * b.r * b.r * 0.7f;
        int n = Mathf.Clamp(Mathf.RoundToInt(area / (treeSpacing * treeSpacing * 2.2f)), 40, 1400);
        var cellOf = new Dictionary<long, List<Vector2>>(); // 간격 검사용 칸
        int placedN = 0;
        for (int tries = 0; tries < n * 3 && placedN < n; tries++)
        {
            var b = centers[r.Next(centers.Count)];
            var p = b.c + Random2(b.r);
            if (OnRoad(p, 4f) || !Outside(p, 4f)) continue;
            int gx = Mathf.FloorToInt(p.x / treeSpacing), gz = Mathf.FloorToInt(p.y / treeSpacing);
            bool ok = true;
            for (int x = gx - 1; x <= gx + 1 && ok; x++)
                for (int z = gz - 1; z <= gz + 1 && ok; z++)
                    if (cellOf.TryGetValue(Key(x, z), out var list))
                        foreach (var q in list) if ((q - p).sqrMagnitude < treeSpacing * treeSpacing) { ok = false; break; }
            if (!ok) continue;
            long k = Key(gx, gz);
            if (!cellOf.TryGetValue(k, out var l)) cellOf[k] = l = new List<Vector2>();
            l.Add(p);
            placedN++;
            Prop(Pick(trees), p, R(13f, 26f), Kind.Tree);
        }
        for (int i = 0; i < n * 0.03f; i++) Prop(Pick(rocks), c + Random2(rad), R(2f, 6f), Kind.Rock);
        for (int i = 0; i < n * 0.04f; i++) Prop(Pick(logs), c + Random2(rad), R(1.2f, 2f), Kind.Soft);
    }

    // 거대 나무: 거인보다 훨씬 큰 나무 (밟고 지나가면 천천히 쓰러짐)
    void GiantTree(Vector2 p)
    {
        if (OnRoad(p, 20f) || !Outside(p, 20f)) return;
        var prefab = Pick(bigTrees);
        if (!prefab) prefab = Pick(trees);
        var go = Prop(prefab, p, R(giantTreeHeight.x, giantTreeHeight.y), Kind.Tree);
        if (go) foreach (var rr in go.GetComponentsInChildren<Renderer>()) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    Vector2 Random2(float rad) { float a = R(0f, Mathf.PI * 2f), d = Mathf.Sqrt((float)r.NextDouble()) * rad; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d; }

    void Meadows()
    {
        // 완만한 언덕 (낮게 눌러서 거인 발목 아래): 풀밭이 밋밋하지 않게
        if (mounds != null && mounds.Length > 0)
            for (int i = 0; i < moundCount; i++)
            {
                var c = RandomSpot(60f);
                if (float.IsNaN(c.x)) continue;
                var go = Instantiate(Pick(mounds), V(c), Quaternion.Euler(0f, R(0f, 360f), 0f), root);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds);
                float wdt = R(90f, 220f), hgt = R(2f, 5f);
                var s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x * wdt / Mathf.Max(0.1f, b.size.x), s.y * hgt / Mathf.Max(0.1f, b.size.y), s.z * wdt / Mathf.Max(0.1f, b.size.z));
                foreach (var rr in rs) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        // 풀숲 (meadowClumps > 0일 때만): 풀·꽃·수풀이 모여 난 자리
        for (int i = 0; i < meadowClumps; i++)
        {
            var c = RandomSpot(10f);
            if (float.IsNaN(c.x)) continue;
            int k = r.Next(3, 8);
            for (int j = 0; j < k; j++)
            {
                bool bush = r.NextDouble() < 0.35;
                Prop(Pick(bush ? bushes : grass), c + Random2(14f), bush ? R(4f, 8f) : R(3f, 6f), Kind.Soft);
            }
        }
        // 들판에 홀로 우뚝 선 거대 나무 (둘레에 보통 나무 몇 그루)
        for (int i = 0; i < giantTrees; i++)
        {
            var c = RandomSpot(45f);
            if (float.IsNaN(c.x)) continue;
            zones.Add((c, 45f));
            GiantTree(c);
            int k = r.Next(0, 5);
            for (int j = 0; j < k; j++) Prop(Pick(trees), c + Random2(40f), R(12f, 22f), Kind.Tree);
        }
        // 작은 나무 무리 (들판 곳곳에 대여섯 그루씩 모여 선 나무)
        for (int i = 0; i < loneTrees; i++)
        {
            var c = RandomSpot(25f);
            if (float.IsNaN(c.x)) continue;
            zones.Add((c, 25f));
            int k = r.Next(4, 10);
            for (int j = 0; j < k; j++) Prop(Pick(trees), c + Random2(22f), R(12f, 24f), Kind.Tree);
        }
        for (int i = 0; i < scatteredRocks; i++)
        {
            var c = RandomSpot(4f);
            if (float.IsNaN(c.x)) continue;
            bool big = r.NextDouble() < 0.12;
            Prop(Pick(rocks), c, big ? R(10f, 18f) : R(2f, 6f), Kind.Rock);
            if (big) for (int j = 0; j < 3; j++) Prop(Pick(rocks), c + Random2(14f), R(4f, 9f), Kind.Rock);
        }
    }

    // 멀리 둘러싼 산 (닿을 수 없는 곳, 배경)
    void Mountains()
    {
        if (mountains == null || mountains.Length == 0) return;
        int n = 40;
        for (int i = 0; i < n; i++)
        {
            float a = (i + R(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
            float d = R(mountainRing.x, mountainRing.y);
            var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            var go = Spawn(Pick(mountains), p, R(180f, 340f));
            if (go) { var s = go.transform.localScale; go.transform.localScale = new Vector3(s.x * 1.3f, s.y, s.z * 1.3f); } // 조금 더 넓게
        }
    }

    // ───────────── 시골 마을 ─────────────
    void PlanHamlets()
    {
        // 거인 시작 자리 바로 옆에 시골 마을 하나 (시작하자마자 보이게)
        if (!float.IsNaN(spawn.x))
            for (int k = 0; k < 24; k++)
            {
                float a = R(0f, 360f) * Mathf.Deg2Rad;
                var p = spawn + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(220f, 300f);
                if (!Usable(p, 140f)) continue;
                zones.Add((p, 150f));
                hamletSpots.Add(p);
                // 시작 자리를 그 마을 가장자리로 옮기고 마을 쪽을 바라보게
                spawn = p + (spawn - p).normalized * 100f;
                spawnLook = p;
                zones.Add((spawn, 40f));
                break;
            }
        // 성문 길 네 개를 따라 두 곳씩 (길 옆으로 조금 비켜서), 나머지는 아무 데나
        var spots = new List<Vector2>();
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        foreach (var dir in dirs)
            foreach (var along in new[] { R(260f, 420f), R(700f, 950f) })
            {
                Vector2 side = new Vector2(dir.y, dir.x) * (r.NextDouble() < 0.5 ? -1f : 1f) * R(120f, 170f);
                Vector2 start = dir.y != 0f ? new Vector2(roadX, dir.y * innerZ) : new Vector2(dir.x * innerX, roadZ);
                spots.Add(start + dir * along + side);
            }
        foreach (var p in spots)
        {
            if (hamletSpots.Count >= hamlets) break;
            if (!Usable(p, 140f)) continue;
            zones.Add((p, 150f));
            hamletSpots.Add(p);
        }
        // 나머지는 들판 아무 데나 (고를 때마다 자리를 차지해서 서로 겹치지 않게)
        for (int fails = 0; hamletSpots.Count < hamlets && fails < 20;)
        {
            var p = RandomSpot(150f);
            if (float.IsNaN(p.x)) { fails++; continue; }
            zones.Add((p, 150f));
            hamletSpots.Add(p);
        }
    }

    void Hamlet(Vector2 c, int idx)
    {
        var h = new GameObject($"Hamlet_{idx}").transform;
        h.SetParent(root, false);
        // 작은 오두막만 두 겹으로 빙 둘러 세움 (큰 헛간 같은 건물은 없음: 거인이 처음부터 다 부술 수 있게)
        int n = 0;
        foreach (var (count, rMin, rMax) in new[] { (r.Next(7, 10), 22f, 30f), (r.Next(11, 15), 42f, 54f) })
        {
            float a0 = R(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                float a = (a0 + i * 360f / count + R(-8f, 8f)) * Mathf.Deg2Rad;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(rMin, rMax);
                city.RuralHouse(h, V(p), R(6.5f, 9f), R(5.5f, 7f), seed * 31 + idx * 101 + n++); // 작은 오두막
            }
        }
        if (well) Prop(well, c, R(5f, 6.5f), Kind.Soft, h);
        Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        float bA = R(0f, 360f);
        for (int i = 0; i < 3; i++) Prop(Pick(hay), c + Dir(bA + R(-25f, 25f)) * R(68f, 95f), R(3.5f, 4.5f), Kind.Soft, h);

        // 밭 두세 곳: 작물 줄 + 두 변에 울타리 + 허수아비
        int fields = r.Next(2, 4);
        for (int f = 0; f < fields; f++)
        {
            float fa = bA + 120f + f * 70f + R(-15f, 15f);
            Vector2 fc = c + Dir(fa) * R(95f, 125f);
            Field(h, fc, R(34f, 46f), R(26f, 34f));
        }
        // 집 둘레 소품
        for (int i = 0; i < 5; i++) Prop(Pick(farmProps), c + Dir(R(0f, 360f)) * R(32f, 40f), R(1.5f, 3f), Kind.Soft, h); // 두 겹 집 사이
        for (int i = 0; i < 4; i++) Prop(Pick(trees), c + Dir(R(0f, 360f)) * R(64f, 85f), R(10f, 16f), Kind.Tree, h);
        if (windmill && r.NextDouble() < 0.5) Prop(windmill, c + Dir(bA + 200f) * R(100f, 120f), R(9f, 10f), Kind.Tree, h); // 작은 풍차 (밟으면 쓰러짐)

        // 목초지: 양 또는 소
        var pasture = c + Dir(bA + R(140f, 220f)) * R(80f, 110f);
        var kind = r.NextDouble() < 0.5 ? WildCreature.Kind.Sheep : WildCreature.Kind.Cow;
        int herd = r.Next(4, 8);
        for (int i = 0; i < herd; i++) WildCreature.Create(kind, V(pasture + Random2(20f)), 35f, h, r);
    }

    void Field(Transform parent, Vector2 c, float w, float d)
    {
        var crop = Pick(crops);
        float sx = 4.5f, sz = 3.6f;
        for (float x = -w / 2 + 3f; x <= w / 2 - 3f; x += sx)
            for (float z = -d / 2 + 2f; z <= d / 2 - 2f; z += sz)
                Prop(crop, c + new Vector2(x + R(-0.6f, 0.6f), z), R(3f, 4.2f), Kind.Soft, parent);
        // 울타리: 두 변 (긴 변 하나 + 짧은 변 하나)
        if (fences != null && fences.Length > 0)
        {
            var fence = Pick(fences);
            float seg = 4.4f;
            for (float x = -w / 2; x < w / 2; x += seg) Prop(fence, c + new Vector2(x + seg / 2, -d / 2 - 2f), 2.8f, Kind.Soft, parent, 0f);
            for (float z = -d / 2; z < d / 2; z += seg) Prop(fence, c + new Vector2(-w / 2 - 2f, z + seg / 2), 2.8f, Kind.Soft, parent, 90f);
        }
        if (scarecrow && r.NextDouble() < 0.7) Prop(scarecrow, c + Random2(6f), R(6f, 7f), Kind.Soft, parent);
    }

    // ───────────── 고블린 야영지 ─────────────
    void PlanCamps()
    {
        for (int i = 0; i < goblinCamps; i++)
        {
            var p = RandomSpot(70f, 1700f, monsterSafeRadius);
            if (float.IsNaN(p.x)) continue;
            zones.Add((p, 70f));
            campSpots.Add(p);
        }
    }

    void Camp(Vector2 p, int i)
    {
        {
            var camp = new GameObject($"GoblinCamp_{i}").transform;
            camp.SetParent(root, false);
            if (campfire) Prop(campfire, p, R(2.5f, 3f), Kind.Soft, camp);
            int tentsN = r.Next(3, 5);
            for (int t = 0; t < tentsN; t++)
            {
                float a = (t * 360f / tentsN + R(-15f, 15f)) * Mathf.Deg2Rad;
                var tp = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(18f, 26f);
                var go = Prop(Pick(tents), tp, R(6f, 8f), Kind.Soft, camp);
                if (go) go.transform.rotation = Quaternion.LookRotation(new Vector3(p.x - tp.x, 0f, p.y - tp.y)); // 입구가 모닥불 쪽
            }
            for (int k = 0; k < 3; k++) Prop(Pick(logs), p + Random2(12f), R(1.2f, 1.6f), Kind.Soft, camp);
            int gob = r.Next(5, 9);
            for (int k = 0; k < gob; k++) WildCreature.Create(WildCreature.Kind.Goblin, V(p + Random2(30f)), 40f, camp, r);
        }
    }

    // ───────────── 야생동물·몬스터 ─────────────
    void Wildlife()
    {
        var parent = new GameObject("Creatures").transform;
        parent.SetParent(root, false);
        void Group(WildCreature.Kind k, int groups, int min, int max, float spread, float home, float minFromCenter)
        {
            bool monster = k == WildCreature.Kind.Troll || k == WildCreature.Kind.Wolf || k == WildCreature.Kind.Slime;
            for (int i = 0; i < groups; i++)
            {
                var p = RandomSpot(spread, minFromCenter, monster ? monsterSafeRadius : 0f);
                if (float.IsNaN(p.x)) continue;
                int n = r.Next(min, max + 1);
                for (int j = 0; j < n; j++) WildCreature.Create(k, V(p + Random2(spread)), home, parent, r);
            }
        }
        Group(WildCreature.Kind.Deer, deerHerds, 3, 6, 25f, 90f, 0f);
        Group(WildCreature.Kind.Boar, boarGroups, 2, 4, 15f, 70f, 0f);
        Group(WildCreature.Kind.Wolf, wolfPacks, 3, 5, 15f, 160f, 1200f);
        Group(WildCreature.Kind.Troll, trolls, 1, 1, 5f, 140f, 1500f);
        Group(WildCreature.Kind.Slime, slimeGroups, 4, 6, 20f, 60f, 0f);
    }

    // ───────────── 소품 + 밟기 ─────────────
    enum Kind : byte { Tree, Soft, Rock }

    struct PropData
    {
        public Transform t; public Vector3 pos; public float r; public Kind kind;
        public bool hit; public float t0; public Quaternion rot0; public Vector3 scale0, axis;
    }
    readonly List<PropData> props = new List<PropData>();
    readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
    readonly List<int> moving = new List<int>();
    const float Cell = 64f;
    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    GameObject Spawn(GameObject prefab, Vector2 p, float targetSize, Transform parent = null, float yaw = -1f)
    {
        if (!prefab) return null;
        var go = Instantiate(prefab, V(p), Quaternion.Euler(0f, yaw >= 0f ? yaw : R(0f, 360f), 0f), parent ? parent : root);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c); // 물리 충돌 없음 (밟기는 직접 검사)
        // 높이를 원하는 크기에 맞춤 (산은 폭 기준)
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds);
            float cur = Mathf.Max(0.05f, b.size.y > 0.2f ? b.size.y : Mathf.Max(b.size.x, b.size.z));
            go.transform.localScale = prefab.transform.localScale * (targetSize / cur);
        }
        return go;
    }

    GameObject Prop(GameObject prefab, Vector2 p, float targetSize, Kind kind, Transform parent = null, float yaw = -1f)
    {
        var go = Spawn(prefab, p, targetSize, parent, yaw);
        if (!go) return null;
        var rs = go.GetComponentsInChildren<Renderer>();
        float rad = 2f;
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds);
            rad = Mathf.Max(1f, Mathf.Min(b.extents.x, b.extents.z) * (kind == Kind.Tree ? 0.5f : 0.8f));
            if (kind == Kind.Rock) foreach (var rr in rs) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        int i = props.Count;
        props.Add(new PropData { t = go.transform, pos = go.transform.position, r = rad, kind = kind, rot0 = go.transform.rotation, scale0 = go.transform.localScale });
        long key = Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
        list.Add(i);
        return go;
    }

    void Update()
    {
        AnimateHits();
        if (GameStartMenu.InMenu) return;
        var g = GiantController.Instance;
        if (!g || GiantShrink.Active) return;
        var cc = g.GetComponent<CharacterController>();
        float body = cc ? cc.radius * 1.15f : 5f;
        Vector3 gp = g.transform.position;
        int cx = Mathf.FloorToInt(gp.x / Cell), cz = Mathf.FloorToInt(gp.z / Cell);
        int span = Mathf.CeilToInt((body + 30f) / Cell);
        for (int x = cx - span; x <= cx + span; x++)
            for (int z = cz - span; z <= cz + span; z++)
            {
                if (!grid.TryGetValue(Key(x, z), out var list)) continue;
                foreach (int i in list)
                {
                    var pd = props[i];
                    if (pd.hit || !pd.t) continue;
                    Vector3 d = pd.pos - gp; d.y = 0f;
                    if (d.magnitude > body + pd.r) continue;
                    pd.hit = true; pd.t0 = Time.time;
                    Vector3 away = d.sqrMagnitude > 0.01f ? d.normalized : g.transform.forward;
                    pd.axis = Vector3.Cross(Vector3.up, away);
                    props[i] = pd;
                    moving.Add(i);
                    if (pd.kind == Kind.Rock) CombatFX.Muzzle(pd.pos + Vector3.up * 2f, Vector3.up, 4f); // 흙먼지
                    int xp = pd.kind == Kind.Tree ? trampleXpTree : pd.kind == Kind.Rock ? trampleXpRock : trampleXpSoft;
                    if (xp > 0) ScoreManager.Add(xp, null, pd.pos + Vector3.up * Mathf.Max(2f, pd.r)); // 부순 소품에서 경험치 구슬
                }
            }
    }

    [Tooltip("쓰러지거나 부서진 소품이 그대로 남아 있는 시간 (초). 그 뒤 땅속으로 가라앉으며 사라짐")] public float wreckHoldTime = 2.5f;
    [Tooltip("가라앉아 사라지는 데 걸리는 시간 (초)")] public float wreckFadeTime = 1.2f;

    // 쓰러지는 나무 · 납작해지는 풀·작물 · 부서져 가라앉는 바위 → 잠시 뒤 땅속으로 가라앉으며 사라짐
    void AnimateHits()
    {
        for (int k = moving.Count - 1; k >= 0; k--)
        {
            var pd = props[moving[k]];
            if (!pd.t) { moving.RemoveAt(k); continue; }
            float t = Time.time - pd.t0;
            float dur;
            switch (pd.kind)
            {
                case Kind.Tree:
                {
                    dur = 0.8f + pd.r * 0.05f; // 큰 나무일수록 천천히 넘어감
                    float u = Mathf.Clamp01(t / dur);
                    float ang = 84f * u * u; // 처음엔 천천히, 점점 빨리 넘어감
                    pd.t.rotation = Quaternion.AngleAxis(ang, pd.axis) * pd.rot0;
                    break;
                }
                case Kind.Soft:
                {
                    dur = 0.2f;
                    float u = Mathf.Clamp01(t / dur);
                    pd.t.localScale = new Vector3(pd.scale0.x * (1f + 0.25f * u), pd.scale0.y * Mathf.Lerp(1f, 0.15f, u), pd.scale0.z * (1f + 0.25f * u));
                    break;
                }
                default:
                {
                    dur = 0.6f;
                    float u = Mathf.Clamp01(t / dur);
                    pd.t.localScale = pd.scale0 * Mathf.Lerp(1f, 0.35f, u);
                    pd.t.position = pd.pos - Vector3.up * u * 1.5f;
                    break;
                }
            }

            // 사라지기: 남아 있다가 작아지며 땅속으로 가라앉음
            float f = (t - dur - (pd.kind == Kind.Soft ? wreckHoldTime * 0.6f : wreckHoldTime)) / Mathf.Max(0.05f, wreckFadeTime);
            if (f <= 0f) continue;
            if (f >= 1f)
            {
                Destroy(pd.t.gameObject);
                moving.RemoveAt(k);
                continue;
            }
            float e = f * f;
            Vector3 sc = pd.kind == Kind.Soft ? new Vector3(pd.scale0.x * 1.25f, pd.scale0.y * 0.15f, pd.scale0.z * 1.25f)
                       : pd.kind == Kind.Rock ? pd.scale0 * 0.35f : pd.scale0;
            pd.t.localScale = sc * (1f - 0.8f * e);
            float sink = pd.kind == Kind.Rock ? 1.5f : 0f;
            pd.t.position = pd.pos - Vector3.up * (sink + e * (pd.r * 1.5f + 2f));
        }
    }
}
