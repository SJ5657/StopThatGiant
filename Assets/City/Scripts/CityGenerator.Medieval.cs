using System.Collections.Generic;
using UnityEngine;

// 중세 마을 (style = Medieval). 도시와 같은 도로 격자(블록·도로 크기)를 그대로 써서 투석기·경비대·수레·시민이 같은 길로 다님.
//  - 보통 블록: 좁은 부지마다 2~4층 집 (목조 골조 / 회벽 / 돌집) + 박공지붕 + 굴뚝. 블록 안쪽 부지 일부는 나무가 있는 텃밭
//  - 가운데 교차로(거인 시작 위치)를 둘러싼 4블록은 시장 광장 (노점·우물·길드 회관), 그 동쪽 블록은 대성당
//  - 북쪽 2x2 블록은 성: 성벽·모서리 망루·성문·본성(천수)·대연회장·예배당
//  - 공원 블록은 잔디와 나무, 몇몇 블록은 교구 성당. 도시 둘레엔 성벽과 망루, 네 변 가운데에 성문
// 모든 건물은 상자(+지붕 같은 자식 메시) 구조라 기존 파괴·초고층(단단한 건물) 규칙이 그대로 적용됨:
// 집은 처음부터 부술 수 있고, 높은 탑·성·대성당은 거인이 충분히 커져야 부서짐 (TallBuildings).
public partial class CityGenerator
{
    public enum Style { Modern, Medieval }

    [Header("스타일")]
    [Tooltip("Modern = 현대 도시, Medieval = 중세 마을 (같은 도로 격자)")] public Style style = Style.Modern;

    [Header("중세 마을 (style = Medieval)")]
    [Tooltip("목조 골조 벽 (OutlineBox + Timber)")] public Material timberMat;
    [Tooltip("회벽 + 작은 창 (OutlineBox)")] public Material plasterMat;
    [Tooltip("돌벽 (OutlineBox + Stone)")] public Material stoneMat;
    [Tooltip("무늬 없는 상자 (나무 기둥·노점 등)")] public Material woodMat;
    [Tooltip("지붕 (OutlineMesh + 기와)")] public Material roofMat;
    [Tooltip("성가퀴·나무 수관 (OutlineMesh)")] public Material meshMat;
    [Tooltip("광장·성 안뜰 바닥 (OutlineBox + 자갈)")] public Material plazaMat;
    public float medMinLot = 18f;
    public float medMaxLot = 34f;
    [Tooltip("집과 부지 경계 사이 (작을수록 집들이 다닥다닥)")] public float medSetback = 0.6f;
    public float floorHeight = 3.6f;
    [Tooltip("블록 안쪽(길에 안 닿는) 부지가 텃밭이 될 확률")] [Range(0, 1)] public float gardenChance = 0.5f;
    [Tooltip("마을 가장자리에서 이 거리(m) 안은 외곽: 부지가 작고 대부분 단층인 작은 집 (돌탑 없음)")] public float medOutskirts = 285f;
    [Tooltip("외곽 부지 크기 배율 (medMinLot·medMaxLot에 곱함)")] [Range(0.3f, 1f)] public float outskirtLotScale = 0.6f;
    [Tooltip("외곽이 아닌 마을 안 부지가 돌탑이 될 확률")] [Range(0, 1)] public float medTowerChance = 0.07f;
    [Tooltip("돌탑 중 평평한 옥상(성가퀴)일 확률: 옥상에 수비병이 섬 (WallDefense)")] [Range(0, 1)] public float medTowerFlatChance = 0.75f;
    [Tooltip("교구 성당 블록 수")] public int parishChurches = 6;
    [Tooltip("도시 둘레 성벽")] public bool cityWall = true;
    [Tooltip("마을 바깥 풀밭 재질")] public Material fieldMat;
    [Tooltip("마을 가장자리에서 풀밭이 이어지는 거리 (m)")] public float fieldMargin = 5000f;
    [Tooltip("성문마다 바깥으로 평평한 흙길 상자 (언덕 지형을 쓰면 끔: 지형 셰이더가 길을 그림)")] public bool gateRoads = true;

    enum BlockRole { Houses, Park, Plaza, GuildHall, Cathedral, Church, Castle }

    static readonly Color[] PlasterCols =
    {
        new Color(0.96f, 0.93f, 0.86f), new Color(0.93f, 0.88f, 0.76f), new Color(0.95f, 0.95f, 0.92f),
        new Color(0.91f, 0.83f, 0.69f), new Color(0.94f, 0.87f, 0.81f), new Color(0.89f, 0.88f, 0.80f),
    };
    static readonly Color[] StoneCols = { new Color(0.75f, 0.73f, 0.69f), new Color(0.69f, 0.67f, 0.64f), new Color(0.81f, 0.77f, 0.69f) };
    static readonly Color[] RoofCols =
    {
        new Color(0.71f, 0.33f, 0.22f), new Color(0.60f, 0.25f, 0.19f), new Color(0.47f, 0.31f, 0.22f),
        new Color(0.39f, 0.41f, 0.48f), new Color(0.78f, 0.64f, 0.38f), new Color(0.66f, 0.40f, 0.27f),
    };
    static readonly Color[] LeafCols = { new Color(0.36f, 0.58f, 0.30f), new Color(0.29f, 0.51f, 0.28f), new Color(0.46f, 0.63f, 0.32f), new Color(0.40f, 0.55f, 0.25f) };
    static readonly Color[] AwningCols =
    {
        new Color(0.82f, 0.22f, 0.20f), new Color(0.22f, 0.42f, 0.76f), new Color(0.93f, 0.76f, 0.22f),
        new Color(0.26f, 0.60f, 0.32f), new Color(0.95f, 0.94f, 0.90f), new Color(0.58f, 0.30f, 0.62f),
    };
    static readonly Color WoodCol = new Color(0.55f, 0.38f, 0.24f), TrunkCol = new Color(0.43f, 0.31f, 0.21f);
    static readonly Color CastleStone = new Color(0.73f, 0.71f, 0.67f), ChurchStone = new Color(0.83f, 0.80f, 0.73f);

    // 생성 중에만 쓰는 것들 (메시는 씬에 함께 저장됨)
    Mesh mRoofX, mRoofZ, mPyramid, mCanopy;
    readonly Dictionary<int, Mesh> crenelMeshes = new Dictionary<int, Mesh>();

    int medLayer;
    Vector3 medOrigin;
    Vector2 medCenter; float medMaxDist;

    T Pick<T>(T[] a) => a[rng.Next(a.Length)];
    // 색을 살짝 어둡게·밝게 (세 단계만: 색마다 재질이 하나씩이라 너무 많아지지 않게)
    Color Tint(Color c) { float g = 0.96f + 0.04f * rng.Next(3); return new Color(c.r * g, c.g * g, c.b * g, 1f); }
    // 도시 모서리 기준 (x, z) + 땅(인도) 위 높이 y → 월드 위치
    Vector3 P(Vector2 xz, float y) => medOrigin + new Vector3(xz.x, sidewalkHeight + y, xz.y);

    void GenerateMedieval()
    {
        Clear();
        rng = new System.Random(seed);
        EnsureCube();
        mRoofX = MedievalMeshes.Gable(true); mRoofZ = MedievalMeshes.Gable(false);
        mPyramid = MedievalMeshes.Pyramid(); mCanopy = MedievalMeshes.Blob();
        crenelMeshes.Clear();
        medLayer = Mathf.Max(0, LayerMask.NameToLayer("Building"));

        // 부지 나누기·날개 계산은 도시용 함수를 그대로 쓰되 중세 값으로 (끝나면 되돌림)
        float keepMin = minLot, keepMax = maxLot, keepSet = setback;
        minLot = medMinLot; maxLot = medMaxLot; setback = medSetback;
        variants.Clear();
        try { BuildTown(); PaintWalkways(); }
        finally { minLot = keepMin; maxLot = keepMax; setback = keepSet; closedSet = null; }
    }

    void BuildTown()
    {
        float pitch = blockSize + roadWidth;
        float sizeX = blocksX * pitch + roadWidth, sizeZ = blocksZ * pitch + roadWidth;
        Vector3 origin = medOrigin = transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f;
        medCenter = new Vector2(sizeX, sizeZ) * 0.5f; medMaxDist = medCenter.magnitude;

        // 마을 안 바닥(흙길) + 그 아래로 아주 넓게 펼쳐진 풀밭, 성문마다 바깥으로 이어지는 흙길
        var ground = Box("Ground", transform, origin + new Vector3(sizeX / 2, -0.5f, sizeZ / 2), new Vector3(sizeX + 16f, 1f, sizeZ + 16f), groundMat, null);
        ground.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (fieldMat)
        {
            var field = Box("Field", transform, origin + new Vector3(sizeX / 2, -0.55f, sizeZ / 2), new Vector3(sizeX + fieldMargin * 2f, 1f, sizeZ + fieldMargin * 2f), fieldMat, null);
            field.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float cxw = roadWidth * 0.5f + (blocksX / 2) * (blockSize + roadWidth), czw = roadWidth * 0.5f + (blocksZ / 2) * (blockSize + roadWidth);
            float len = gateRoads ? fieldMargin : 0f; // 언덕 지형이 있으면 길은 지형 셰이더가 그림
            if (len > 0f)
            foreach (var (c, s) in new[]
            {
                (new Vector2(cxw, -len / 2), new Vector2(roadWidth, len)), (new Vector2(cxw, sizeZ + len / 2), new Vector2(roadWidth, len)),
                (new Vector2(-len / 2, czw), new Vector2(len, roadWidth)), (new Vector2(sizeX + len / 2, czw), new Vector2(len, roadWidth)),
            })
            {
                var road = Box("Road", transform, origin + new Vector3(c.x, -0.5f, c.y), new Vector3(s.x, 1f, s.y), groundMat, null);
                road.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        var blocksRoot = new GameObject("Blocks").transform;
        blocksRoot.SetParent(transform, false);

        // ── 블록 역할 정하기 ──
        int cx = blocksX / 2, cz = blocksZ / 2; // 가운데 교차로 번호 (거인 시작 위치)
        var role = new BlockRole[blocksX, blocksZ];
        for (int x = 0; x < blocksX; x++)
            for (int z = 0; z < blocksZ; z++) role[x, z] = rng.NextDouble() < parkChance ? BlockRole.Park : BlockRole.Houses;
        bool In(int x, int z) => x >= 0 && z >= 0 && x < blocksX && z < blocksZ;
        void Set(int x, int z, BlockRole r) { if (In(x, z)) role[x, z] = r; }
        Set(cx - 1, cz - 1, BlockRole.Plaza); Set(cx, cz - 1, BlockRole.Plaza); Set(cx, cz, BlockRole.Plaza);
        Set(cx - 1, cz, BlockRole.GuildHall);
        Set(cx + 1, cz, BlockRole.Cathedral);
        var castle = new Vector2Int(cx - 1, cz + 4);
        bool hasCastle = In(castle.x, castle.y) && In(castle.x + 1, castle.y + 1);
        if (hasCastle) for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++) Set(castle.x + x, castle.y + z, BlockRole.Castle);
        for (int k = 0, tries = 0; k < parishChurches && tries < 200; tries++)
        {
            int x = rng.Next(blocksX), z = rng.Next(blocksZ);
            if (role[x, z] != BlockRole.Houses || Mathf.Abs(x - cx) + Mathf.Abs(z - cz) < 5) continue;
            role[x, z] = BlockRole.Church; k++;
        }

        // 집 블록만 이웃과 붙이고, 성 2x2 안쪽 도로는 없앰 (하나의 성터)
        var reserved = new bool[blocksX, blocksZ];
        for (int x = 0; x < blocksX; x++) for (int z = 0; z < blocksZ; z++) reserved[x, z] = role[x, z] != BlockRole.Houses;
        PlanBlockMerges(reserved);
        var castleRoads = new HashSet<Vector3Int>();
        if (hasCastle)
        {
            castleRoads.Add(new Vector3Int(castle.x + 1, castle.y, 1)); castleRoads.Add(new Vector3Int(castle.x + 1, castle.y + 1, 1));
            castleRoads.Add(new Vector3Int(castle.x, castle.y + 1, 0)); castleRoads.Add(new Vector3Int(castle.x + 1, castle.y + 1, 0));
            closedRoads.AddRange(castleRoads);
        }

        // ── 블록마다 세우기 ──
        int count = 0;
        for (int bx = 0; bx < blocksX; bx++)
        for (int bz = 0; bz < blocksZ; bz++)
        {
            float x0 = roadWidth + bx * pitch, z0 = roadWidth + bz * pitch;
            var block = new GameObject($"Block_{bx}_{bz}").transform;
            block.SetParent(blocksRoot, false);
            var r = role[bx, bz];
            Material baseMat = r == BlockRole.Park ? parkMat : r == BlockRole.Houses || r == BlockRole.Church ? sidewalkMat : plazaMat;
            Box("Sidewalk", block, origin + new Vector3(x0 + blockSize / 2, sidewalkHeight / 2, z0 + blockSize / 2), new Vector3(blockSize, sidewalkHeight, blockSize), baseMat, null);
            AddWalkways(block, origin + new Vector3(x0, 0, z0));
            var inner = new Rect(x0 + 2f, z0 + 2f, blockSize - 4f, blockSize - 4f);
            float down = Downtown(inner.center, medCenter, medMaxDist);
            switch (r)
            {
                case BlockRole.Houses: count += HouseLots(block, inner, down); break;
                case BlockRole.Park: count += Park(block, inner); break;
                case BlockRole.Plaza: count += Market(block, inner, new Vector2(roadWidth + cx * pitch - roadWidth * 0.5f, roadWidth + cz * pitch - roadWidth * 0.5f)); break;
                case BlockRole.GuildHall: count += GuildHall(block, inner); break;
                case BlockRole.Cathedral: count += Cathedral(block, inner); break;
                case BlockRole.Church: count += ParishChurch(block, inner); break;
            }
        }

        // 붙은 블록 사이(없어진 도로 자리): 성 안이면 안뜰 바닥만, 아니면 집
        foreach (var e in closedRoads)
        {
            Rect r = StripRect(e);
            var link = new GameObject($"Link_{e.x}_{e.y}_{(e.z == 0 ? "X" : "Z")}").transform;
            link.SetParent(blocksRoot, false);
            bool inCastle = castleRoads.Contains(e);
            Box("Sidewalk", link, origin + new Vector3(r.center.x, sidewalkHeight / 2, r.center.y), new Vector3(r.width, sidewalkHeight, r.height), inCastle ? plazaMat : sidewalkMat, null);
            if (inCastle) continue;
            var inner = e.z == 1 ? new Rect(r.x, r.y + 2f, r.width, r.height - 4f) : new Rect(r.x + 2f, r.y, r.width - 4f, r.height);
            count += HouseLots(link, inner, Downtown(r.center, medCenter, medMaxDist), false);
        }

        // 성 (2x2 블록 + 사이 도로 자리 전체)
        if (hasCastle)
        {
            var root = new GameObject("Castle").transform;
            root.SetParent(blocksRoot, false);
            var area = new Rect(roadWidth + castle.x * pitch, roadWidth + castle.y * pitch, blockSize * 2 + roadWidth, blockSize * 2 + roadWidth);
            // 가운데 교차로 자리도 안뜰 바닥으로
            Box("Sidewalk", root, origin + new Vector3(area.center.x, sidewalkHeight / 2, area.center.y), new Vector3(roadWidth, sidewalkHeight, roadWidth), plazaMat, null);
            count += Castle(root, area);
        }

        if (cityWall)
        {
            var root = new GameObject("CityWall").transform;
            root.SetParent(blocksRoot, false);
            count += CityWall(root, sizeX, sizeZ, cx, cz, pitch);
        }
        Debug.Log($"[CityGenerator] 중세 마을 {blocksX}x{blocksZ} 블록, 건물 {count}채, 막힌 도로 {closedRoads.Count}");
    }

    // ───────────── 마을 밖 시골 건물 (Wilderness가 부름) ─────────────
    // 월드 좌표 그대로 세우도록 기준점을 땅(y = groundY)에 맞춤. 마을 집과 같은 재질·규칙이라 똑같이 부서짐
    void BeginOutside(int seedValue, float groundY)
    {
        rng = new System.Random(seedValue);
        EnsureCube();
        if (!mRoofX) { mRoofX = MedievalMeshes.Gable(true); mRoofZ = MedievalMeshes.Gable(false); mPyramid = MedievalMeshes.Pyramid(); mCanopy = MedievalMeshes.Blob(); }
        medLayer = Mathf.Max(0, LayerMask.NameToLayer("Building"));
        medOrigin = new Vector3(0f, groundY - sidewalkHeight, 0f);
    }

    // 시골집: c = 바닥 가운데 (월드), w·d = 크기. 작은 단층 오두막 + 박공지붕 (+굴뚝)
    public void RuralHouse(Transform parent, Vector3 c, float w, float d, int seedValue)
    {
        BeginOutside(seedValue, c.y);
        var lot = new Rect(c.x - w / 2 - medSetback, c.z - d / 2 - medSetback, w + medSetback * 2, d + medSetback * 2);
        House(parent, new LotShape { main = lot }, 0f, 1);
    }

    // 헛간: 붉은 나무 벽 + 높은 지붕
    public void Barn(Transform parent, Vector3 c, float w, float d, int seedValue)
    {
        BeginOutside(seedValue, c.y);
        var xz = new Vector2(c.x, c.z);
        float h = 5f;
        var body = Part("Building", parent, P(xz, h / 2), new Vector3(w, h, d), null, woodMat, Tint(new Color(0.62f, 0.24f, 0.18f)));
        Roof(body.transform, xz, w, d, h, Tint(RoofCols[2]), 1.25f);
        Part("Door", body.transform, P(xz + (w >= d ? new Vector2(0f, -d / 2) : new Vector2(-w / 2, 0f)), 1.7f), w >= d ? new Vector3(3f, 3.4f, 0.4f) : new Vector3(0.4f, 3.4f, 3f), null, woodMat, new Color(0.38f, 0.25f, 0.16f));
    }

    // 나무 한 그루 (시골·공원과 같은 각진 나무). 쓰러뜨리기는 Wilderness가 따로 관리하지 않음 (건물처럼 부서짐)
    public void RuralTree(Transform parent, Vector3 c, float size, int seedValue)
    {
        BeginOutside(seedValue, c.y);
        Tree(parent, new Vector2(c.x, c.z), size);
    }

    // ───────────── 부품 ─────────────
    // 상자(또는 상자에 맞춘 메시) 하나: 크기 = 스케일, Building 레이어, 색은 MaterialPropertyBlock
    GameObject Part(string name, Transform parent, Vector3 center, Vector3 size, Mesh mesh, Material mat, Color col)
    {
        var go = Box(name, parent, center, size, Variant(mat, col), null);
        if (mesh) go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.layer = medLayer;
        return go;
    }

    // 색마다 재질 하나 (MaterialPropertyBlock 색은 씬 저장·플레이 시작 때 사라지므로 재질에 색을 넣음). 같은 색끼리 GPU 인스턴싱
    readonly Dictionary<(int, Color32), Material> variants = new Dictionary<(int, Color32), Material>();
    Material Variant(Material src, Color c)
    {
        if (!src) return null;
        var key = (src.GetInstanceID(), (Color32)c);
        if (!variants.TryGetValue(key, out var m))
        {
            m = new Material(src) { name = src.name + "_" + ColorUtility.ToHtmlStringRGB(c), color = c, enableInstancing = true };
            variants[key] = m;
        }
        return m;
    }

    // 인도(블록 가장자리)도 색 재질로
    void PaintWalkways()
    {
        var mat = Variant(sidewalkMat, walkwayColor);
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            if (r.gameObject.name == "Walkway") { r.SetPropertyBlock(null); r.sharedMaterial = mat; }
    }

    // 박공지붕 (용마루는 긴 쪽 방향). baseY = 땅에서 지붕 아랫면 높이. 지붕 높이를 돌려줌
    float Roof(Transform parent, Vector2 c, float w, float d, float baseY, Color col, float pitchMul = 1f, float maxRh = 0f)
    {
        const float over = 0.7f; // 처마가 벽 밖으로 나온 길이
        bool alongX = w >= d;
        float rh = (alongX ? d : w) * R(0.42f, 0.68f) * pitchMul;
        if (maxRh > 0f) rh = Mathf.Min(rh, maxRh); // 외곽 작은 집: 지붕이 너무 높지 않게
        Part("Roof", parent, P(c, baseY + rh / 2), new Vector3(w + over * 2, rh, d + over * 2), alongX ? mRoofX : mRoofZ, roofMat, col);
        return rh;
    }

    void SpireRoof(Transform parent, Vector2 c, float s, float baseY, float h, Color col)
        => Part("Roof", parent, P(c, baseY + h / 2), new Vector3(s, h, s), mPyramid, roofMat, col);

    Mesh CrenelMesh(int n, bool alongX)
    {
        int key = n * 2 + (alongX ? 1 : 0);
        if (!crenelMeshes.TryGetValue(key, out var m)) crenelMeshes[key] = m = MedievalMeshes.Crenels(n, alongX);
        return m;
    }

    // 성가퀴 한 줄 (c = 줄 가운데, topY = 벽 윗면 높이)
    void CrenelStrip(Transform parent, Vector2 c, float len, float thick, float topY, bool alongX, Color col)
    {
        const float ch = 2.4f;
        int n = Mathf.Clamp(Mathf.RoundToInt(len / 4.2f), 1, 48);
        Vector3 size = alongX ? new Vector3(len, ch, thick) : new Vector3(thick, ch, len);
        Part("Crenel", parent, P(c, topY + ch / 2), size, CrenelMesh(n, alongX), meshMat, col);
    }

    // 상자 윗면 둘레 성가퀴
    void CrenelRing(Transform parent, Vector2 c, float w, float d, float topY, Color col)
    {
        const float t = 1.4f;
        CrenelStrip(parent, c + new Vector2(0, d / 2 - t / 2), w, t, topY, true, col);
        CrenelStrip(parent, c - new Vector2(0, d / 2 - t / 2), w, t, topY, true, col);
        CrenelStrip(parent, c + new Vector2(w / 2 - t / 2, 0), d - t * 2, t, topY, false, col);
        CrenelStrip(parent, c - new Vector2(w / 2 - t / 2, 0), d - t * 2, t, topY, false, col);
    }

    // 사각 돌탑: 성가퀴 또는 사각뿔 지붕
    // post = 성가퀴 옥상에 나무 바닥(TowerTop)을 깔아 수비병 자리로 씀 (WallDefense)
    GameObject Tower(Transform parent, Vector2 c, float s, float h, Color stone, bool crenels, bool post = false)
    {
        var t = Part("Building", parent, P(c, h / 2), new Vector3(s, h, s), null, stoneMat, stone);
        if (crenels) CrenelRing(t.transform, c, s, s, h, stone * 0.97f);
        if (crenels && post) Part("TowerTop", t.transform, P(c, h + 0.15f), new Vector3(s - 2.8f, 0.3f, s - 2.8f), null, woodMat, Tint(WoodCol));
        else SpireRoof(t.transform, c, s + 1.4f, h, s * R(1.1f, 1.5f), Pick(RoofCols));
        return t;
    }

    int Tree(Transform parent, Vector2 c, float size)
    {
        float th = size * 0.55f;
        var trunk = Part("Tree", parent, P(c, th / 2), new Vector3(size * 0.14f, th, size * 0.14f), null, woodMat, TrunkCol);
        Part("Leaves", trunk.transform, P(c, th + size * 0.32f), new Vector3(size, size * 0.85f, size), mCanopy, meshMat, Tint(Pick(LeafCols)));
        return 1;
    }

    // ───────────── 집 ─────────────
    // 블록(또는 붙은 블록 사이)을 부지로 나눠 집을 세움. 길에 안 닿는 안쪽 부지는 일부 텃밭(나무)
    int HouseLots(Transform parent, Rect inner, float down, bool gardens = true)
    {
        // 외곽(성벽 쪽): 부지를 잘게 나눠 작은 집 위주
        Vector2 ic = inner.center, size = medCenter * 2f;
        bool outskirts = Mathf.Min(Mathf.Min(ic.x, size.x - ic.x), Mathf.Min(ic.y, size.y - ic.y)) < medOutskirts;
        var lots = new List<Rect>();
        float keepMin = minLot, keepMax = maxLot;
        if (outskirts) { minLot *= outskirtLotScale; maxLot *= outskirtLotScale; }
        try { Split(inner, lots, 0); }
        finally { minLot = keepMin; maxLot = keepMax; }
        int n = 0;
        foreach (var shape in MergeLots(lots))
        {
            Rect lot = shape.main;
            bool street = lot.xMin <= inner.xMin + 0.5f || lot.xMax >= inner.xMax - 0.5f || lot.yMin <= inner.yMin + 0.5f || lot.yMax >= inner.yMax - 0.5f;
            if (gardens && !street && shape.wing.width <= 0f && rng.NextDouble() < gardenChance)
            {
                int trees = rng.Next(0, 3);
                for (int i = 0; i < trees; i++) n += Tree(parent, new Vector2(R(lot.xMin + 3f, lot.xMax - 3f), R(lot.yMin + 3f, lot.yMax - 3f)), R(6f, 9f));
                continue;
            }
            if (!outskirts && rng.NextDouble() < medTowerChance && lot.width > 10f && lot.height > 10f)
            {
                Tower(parent, lot.center, Mathf.Min(Mathf.Min(lot.width, lot.height) - 3f, 22f), R(18f, 28f), Tint(Pick(StoneCols)), rng.NextDouble() < medTowerFlatChance, true);
                n++;
                continue;
            }
            if (outskirts)
            {
                // 외곽은 70% 단층(지붕까지 9.5m 이하), 나머지 2층(12.5m 이하)
                bool one = rng.NextDouble() < 0.7;
                n += House(parent, shape, down, one ? 1 : 2, one ? 9.5f : 12.5f);
            }
            else n += House(parent, shape, down);
        }
        return n;
    }

    // fixedFloors > 0이면 그 층수 (시골 오두막 등)
    // maxHeight > 0이면 지붕 꼭대기까지 그 높이(m)를 넘지 않게 지붕을 낮춤
    int House(Transform parent, LotShape shape, float down, int fixedFloors = 0, float maxHeight = 0f)
    {
        Rect lot = shape.main;
        float w = lot.width - medSetback * 2, d = lot.height - medSetback * 2;
        if (w < 4f || d < 4f) return 0;
        int floors = 2;
        if (rng.NextDouble() < 0.35 + 0.35 * down) floors++;
        if (rng.NextDouble() < 0.08 + 0.2 * down) floors++;
        if (fixedFloors > 0) floors = fixedFloors;
        float h = floors * floorHeight;
        double k = rng.NextDouble();
        Material mat = k < 0.55 ? timberMat : k < 0.85 ? plasterMat : stoneMat;
        Color wall = Tint(mat == stoneMat ? Pick(StoneCols) : Pick(PlasterCols));
        Color roof = Tint(Pick(RoofCols));

        var body = Part("Building", parent, P(lot.center, h / 2), new Vector3(w, h, d), null, mat, wall);
        float maxRh = maxHeight > 0f ? Mathf.Max(1.5f, maxHeight - h) : 0f;
        float rh = Roof(body.transform, lot.center, w, d, h, roof, 1f, maxRh);

        // ㄴ/ㄱ자 날개: 한 층 낮고 지붕은 따로
        if (shape.wing.width > 0f)
        {
            Rect wr = WingRect(shape.main, shape.wing);
            if (wr.width >= 4f && wr.height >= 4f)
            {
                float wh = Mathf.Max(floorHeight * Mathf.Min(2, floors), h - floorHeight); // 단층 집의 날개가 본채보다 높지 않게
                var wing = Part("Wing", body.transform, P(wr.center, wh / 2), new Vector3(wr.width, wh, wr.height), null, mat, wall);
                Roof(wing.transform, wr.center, wr.width, wr.height, wh, roof, 1f, maxHeight > 0f ? Mathf.Max(1.5f, maxHeight - wh) : 0f);
            }
        }

        // 굴뚝: 지붕 비탈 위로 솟음
        if (rng.NextDouble() < 0.3)
        {
            bool alongX = w >= d;
            float along = (alongX ? w : d) * 0.5f - 1.8f, across = (alongX ? d : w) * 0.18f;
            float s1 = rng.NextDouble() < 0.5 ? -1f : 1f, s2 = rng.NextDouble() < 0.5 ? -1f : 1f;
            Vector2 c = lot.center + (alongX ? new Vector2(along * s1, across * s2) : new Vector2(across * s2, along * s1));
            float top = h + rh * 0.85f + 1.6f;
            Part("Chimney", body.transform, P(c, (h - 0.5f + top) * 0.5f), new Vector3(1.4f, top - h + 0.5f, 1.4f), null, stoneMat, Tint(Pick(StoneCols) * 0.92f));
        }
        return 1;
    }

    // ───────────── 공원·광장 ─────────────
    int Park(Transform parent, Rect inner)
    {
        int n = 0, want = rng.Next(6, 13);
        var placed = new List<Vector2>();
        for (int tries = 0; tries < 60 && placed.Count < want; tries++)
        {
            var p = new Vector2(R(inner.xMin + 5f, inner.xMax - 5f), R(inner.yMin + 5f, inner.yMax - 5f));
            bool ok = true;
            foreach (var q in placed) if ((q - p).sqrMagnitude < 9f * 9f) { ok = false; break; }
            if (!ok) continue;
            placed.Add(p);
            n += Tree(parent, p, R(7f, 11f));
        }
        return n;
    }

    // 시장: 노점 줄과 우물. toCenter = 가운데 교차로 쪽 (거인 시작 위치는 비워 둠)
    int Market(Transform parent, Rect inner, Vector2 cross)
    {
        int n = 0;
        Vector2 c = inner.center;
        // 우물 (블록 가운데)
        var well = Part("Building", parent, P(c, 0.8f), new Vector3(4.6f, 1.6f, 4.6f), null, stoneMat, Tint(Pick(StoneCols)));
        Part("Post", well.transform, P(c + new Vector2(-1.8f, 0), 2.6f), new Vector3(0.35f, 2.4f, 0.35f), null, woodMat, WoodCol);
        Part("Post", well.transform, P(c + new Vector2(1.8f, 0), 2.6f), new Vector3(0.35f, 2.4f, 0.35f), null, woodMat, WoodCol);
        Part("Roof", well.transform, P(c, 4.6f), new Vector3(5.4f, 1.8f, 3.4f), mRoofX, roofMat, Tint(Pick(RoofCols)));
        n++;
        // 노점: 블록 가장자리를 따라 둘러섬 (가운데 교차로 쪽 모서리는 비움)
        const int perSide = 4;
        for (int side = 0; side < 4; side++)
        for (int i = 0; i < perSide; i++)
        {
            float t = (i + 0.5f) / perSide;
            Vector2 p; bool alongX;
            switch (side)
            {
                case 0: p = new Vector2(Mathf.Lerp(inner.xMin + 8f, inner.xMax - 8f, t), inner.yMin + 9f); alongX = true; break;
                case 1: p = new Vector2(Mathf.Lerp(inner.xMin + 8f, inner.xMax - 8f, t), inner.yMax - 9f); alongX = true; break;
                case 2: p = new Vector2(inner.xMin + 9f, Mathf.Lerp(inner.yMin + 16f, inner.yMax - 16f, t)); alongX = false; break;
                default: p = new Vector2(inner.xMax - 9f, Mathf.Lerp(inner.yMin + 16f, inner.yMax - 16f, t)); alongX = false; break;
            }
            if ((p - cross).sqrMagnitude < 24f * 24f || rng.NextDouble() < 0.2) continue;
            n += Stall(parent, p, alongX);
        }
        // 모서리 나무
        n += Tree(parent, new Vector2(inner.xMin + 4f, inner.yMin + 4f), R(7f, 9f));
        n += Tree(parent, new Vector2(inner.xMax - 4f, inner.yMax - 4f), R(7f, 9f));
        return n;
    }

    int Stall(Transform parent, Vector2 c, bool alongX)
    {
        Vector3 counter = alongX ? new Vector3(5f, 1.2f, 2.4f) : new Vector3(2.4f, 1.2f, 5f);
        var stall = Part("Building", parent, P(c, 0.6f), counter, null, woodMat, Tint(WoodCol));
        Vector2 off = alongX ? new Vector2(2.3f, 0f) : new Vector2(0f, 2.3f);
        Part("Post", stall.transform, P(c + off, 1.6f), new Vector3(0.25f, 3.2f, 0.25f), null, woodMat, WoodCol);
        Part("Post", stall.transform, P(c - off, 1.6f), new Vector3(0.25f, 3.2f, 0.25f), null, woodMat, WoodCol);
        Vector3 aw = alongX ? new Vector3(6f, 1.4f, 3.6f) : new Vector3(3.6f, 1.4f, 6f);
        Part("Roof", stall.transform, P(c, 3.2f + 0.7f), aw, alongX ? mRoofX : mRoofZ, meshMat, Pick(AwningCols));
        return 1;
    }

    // 길드 회관: 광장 한쪽의 큰 목조 건물 + 나머지는 노점
    int GuildHall(Transform parent, Rect inner)
    {
        Vector2 c = new Vector2(inner.center.x - 8f, inner.center.y + 8f);
        float w = 44f, d = 26f, h = floorHeight * 4;
        var hall = Part("Building", parent, P(c, h / 2), new Vector3(w, h, d), null, timberMat, Tint(PlasterCols[0]));
        Roof(hall.transform, c, w, d, h, Tint(RoofCols[0]), 1.2f);
        // 시계탑
        var clock = Part("Tower", hall.transform, P(c + new Vector2(0, -d / 2 + 4f), (h + 10f) / 2), new Vector3(7f, h + 10f, 7f), null, stoneMat, Tint(StoneCols[0]));
        SpireRoof(clock.transform, c + new Vector2(0, -d / 2 + 4f), 8.4f, h + 10f, 10f, Tint(RoofCols[3]));
        int n = 1;
        n += Stall(parent, new Vector2(inner.xMin + 10f, inner.yMin + 9f), true);
        n += Stall(parent, new Vector2(inner.xMin + 24f, inner.yMin + 9f), true);
        n += Tree(parent, new Vector2(inner.xMax - 6f, inner.yMax - 6f), R(8f, 10f));
        return n;
    }

    // ───────────── 성당 ─────────────
    int Cathedral(Transform parent, Rect inner)
    {
        Vector2 c = inner.center + new Vector2(0f, 4f);
        Color stone = Tint(ChurchStone), slate = Tint(RoofCols[3]);
        // 신랑(긴 본당, 남북) + 수랑(동서 십자) + 정면 종탑과 첨탑
        float nw = 20f, nl = 54f, nh = 20f;
        var nave = Part("Building", parent, P(c, nh / 2), new Vector3(nw, nh, nl), null, stoneMat, stone);
        Roof(nave.transform, c, nw, nl, nh, slate, 1.25f);
        Vector2 tc = c + new Vector2(0f, 8f);
        float tl = 46f, tw = 16f, th = 18f;
        var transept = Part("Wing", nave.transform, P(tc, th / 2), new Vector3(tl, th, tw), null, stoneMat, stone);
        Roof(transept.transform, tc, tl, tw, th, slate, 1.25f);
        Vector2 bc = c + new Vector2(0f, -nl / 2 - 7f);
        var bell = Part("Building", parent, P(bc, 20f), new Vector3(15f, 40f, 15f), null, stoneMat, stone);
        CrenelRing(bell.transform, bc, 15f, 15f, 40f, stone * 0.97f);
        SpireRoof(bell.transform, bc, 11f, 40f, 30f, slate);
        int n = 2;
        n += Tree(parent, new Vector2(inner.xMin + 6f, inner.yMax - 6f), R(8f, 10f));
        n += Tree(parent, new Vector2(inner.xMax - 6f, inner.yMax - 6f), R(8f, 10f));
        return n;
    }

    int ParishChurch(Transform parent, Rect inner)
    {
        bool alongX = rng.NextDouble() < 0.5;
        Vector2 c = inner.center;
        Color stone = Tint(Pick(StoneCols)), roof = Tint(Pick(RoofCols));
        Vector3 size = alongX ? new Vector3(28f, 11f, 13f) : new Vector3(13f, 11f, 28f);
        var nave = Part("Building", parent, P(c, size.y / 2), size, null, stoneMat, stone);
        Roof(nave.transform, c, size.x, size.z, size.y, roof, 1.3f);
        Vector2 tcen = c + (alongX ? new Vector2(-size.x / 2 - 4f, 0f) : new Vector2(0f, -size.z / 2 - 4f));
        var tower = Part("Building", parent, P(tcen, 11f), new Vector3(8f, 22f, 8f), null, stoneMat, stone);
        SpireRoof(tower.transform, tcen, 8.6f, 22f, 14f, roof);
        int n = 2;
        // 둘레에 나무 (묘지 느낌)
        for (int i = 0; i < 6; i++)
        {
            var p = new Vector2(R(inner.xMin + 5f, inner.xMax - 5f), R(inner.yMin + 5f, inner.yMax - 5f));
            if (Mathf.Abs(p.x - c.x) < 22f && Mathf.Abs(p.y - c.y) < 22f) continue;
            n += Tree(parent, p, R(7f, 10f));
        }
        return n;
    }

    // ───────────── 성 ─────────────
    int Castle(Transform parent, Rect area)
    {
        int n = 0;
        Color stone = Tint(CastleStone);
        const float inset = 6f, wallT = 6f, wallH = 12f, towerS = 15f, towerH = 24f;
        var r = new Rect(area.x + inset, area.y + inset, area.width - inset * 2, area.height - inset * 2);
        // 모서리 망루
        foreach (var p in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax) })
        { Tower(parent, p, towerS, towerH, stone, true, true); n++; }
        // 성벽 (남쪽 가운데는 성문)
        float gx = r.center.x, gate = 14f, gt = 11f;
        n += WallRun(parent, new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), wallT, wallH, stone, towerS);
        n += WallRun(parent, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), wallT, wallH, stone, towerS);
        n += WallRun(parent, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), wallT, wallH, stone, towerS);
        n += WallRun(parent, new Vector2(r.xMin, r.yMin), new Vector2(gx - gate / 2 - gt / 2, r.yMin), wallT, wallH, stone, towerS, gt);
        n += WallRun(parent, new Vector2(gx + gate / 2 + gt / 2, r.yMin), new Vector2(r.xMax, r.yMin), wallT, wallH, stone, gt, towerS);
        n += Gatehouse(parent, new Vector2(gx, r.yMin), gate, gt, 20f, wallT, stone, true, true);

        // 본성(천수): 안뜰 북쪽 가운데. 네 모서리에 뾰족지붕 첨탑
        Vector2 kc = new Vector2(r.center.x, r.center.y + r.height * 0.14f);
        float kw = 38f, kd = 32f, kh = 34f;
        var keep = Part("Building", parent, P(kc, kh / 2), new Vector3(kw, kh, kd), null, stoneMat, stone);
        CrenelRing(keep.transform, kc, kw, kd, kh, stone * 0.97f);
        Color slate = Tint(RoofCols[3]);
        foreach (var o in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            Vector2 tc = kc + new Vector2(o.x * kw / 2, o.y * kd / 2);
            var turret = Part("Turret", keep.transform, P(tc, (kh + 8f) / 2), new Vector3(9f, kh + 8f, 9f), null, stoneMat, stone);
            SpireRoof(turret.transform, tc, 10.4f, kh + 8f, 11f, slate);
        }
        // 가운데 큰 탑
        var donjon = Part("Turret", keep.transform, P(kc, (kh + 16f) / 2), new Vector3(14f, kh + 16f, 14f), null, stoneMat, stone);
        SpireRoof(donjon.transform, kc, 15.4f, kh + 16f, 16f, slate);
        n++;

        // 대연회장(서쪽)·예배당(동쪽)·마구간
        Vector2 hc = new Vector2(r.xMin + 30f, r.center.y - 18f);
        var hall = Part("Building", parent, P(hc, 7f), new Vector3(18f, 14f, 48f), null, stoneMat, stone);
        Roof(hall.transform, hc, 18f, 48f, 14f, Tint(RoofCols[0]), 1.2f); n++;
        Vector2 cc = new Vector2(r.xMax - 26f, r.center.y - 22f);
        var chapel = Part("Building", parent, P(cc, 6f), new Vector3(12f, 12f, 26f), null, stoneMat, Tint(ChurchStone));
        Roof(chapel.transform, cc, 12f, 26f, 12f, slate, 1.3f);
        var bell = Part("Tower", chapel.transform, P(cc + new Vector2(0, -15f), 9f), new Vector3(6f, 18f, 6f), null, stoneMat, Tint(ChurchStone));
        SpireRoof(bell.transform, cc + new Vector2(0, -15f), 6.6f, 18f, 10f, slate); n++;
        Vector2 sc = new Vector2(r.xMax - 24f, r.yMax - 24f);
        var stable = Part("Building", parent, P(sc, 3.5f), new Vector3(30f, 7f, 12f), null, timberMat, Tint(PlasterCols[1]));
        Roof(stable.transform, sc, 30f, 12f, 7f, Tint(RoofCols[4])); n++;
        // 안뜰 우물과 나무
        var well = Part("Building", parent, P(r.center + new Vector2(0, -26f), 0.8f), new Vector3(4.6f, 1.6f, 4.6f), null, stoneMat, stone);
        Part("Roof", well.transform, P(r.center + new Vector2(0, -26f), 3.6f), new Vector3(5.4f, 1.8f, 3.4f), mRoofX, roofMat, Tint(RoofCols[0])); n++;
        n += Tree(parent, new Vector2(r.xMin + 18f, r.yMin + 18f), 9f);
        n += Tree(parent, new Vector2(r.xMax - 18f, r.yMin + 18f), 9f);
        return n;
    }

    // a→b (축 방향) 성벽: 30m 남짓 조각으로 나눠 세우고 위에 성가퀴. trimA·trimB = 양 끝 탑 크기 (탑과 겹치지 않게 반만큼 뺌)
    // outward ≠ 0 (도시 성벽): 성가퀴를 바깥 가장자리로 옮기고 안쪽에 사람이 다니는 나무 길(WallWalk)을 깔음 (WallDefense가 궁수·투석기를 세움)
    int WallRun(Transform parent, Vector2 a, Vector2 b, float thick, float h, Color stone, float trimA, float trimB = -1f, Vector2 outward = default)
    {
        if (trimB < 0f) trimB = trimA;
        bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
        Vector2 dir = (b - a).normalized;
        float len = Vector2.Distance(a, b) - trimA / 2 - trimB / 2;
        if (len < 2f) return 0;
        Vector2 start = a + dir * trimA / 2;
        int pieces = Mathf.Max(1, Mathf.RoundToInt(len / 32f));
        float pl = len / pieces;
        for (int i = 0; i < pieces; i++)
        {
            Vector2 c = start + dir * pl * (i + 0.5f);
            Vector3 size = alongX ? new Vector3(pl, h, thick) : new Vector3(thick, h, pl);
            var seg = Part("Building", parent, P(c, h / 2), size, null, stoneMat, stone);
            if (outward == Vector2.zero) { CrenelStrip(seg.transform, c, pl, 1.4f, h, alongX, stone * 0.97f); continue; }
            // 바깥쪽(성 밖) 가장자리를 따라 성가퀴, 그 안쪽은 성벽 위 길
            CrenelStrip(seg.transform, c + outward * (thick / 2 - 0.7f), pl, 1.4f, h, alongX, stone * 0.97f);
            float ww = thick - 1.8f;
            Vector2 wc = c - outward * 0.7f;
            Part("WallWalk", seg.transform, P(wc, h + 0.15f), alongX ? new Vector3(pl, 0.3f, ww) : new Vector3(ww, 0.3f, pl), null, woodMat, Tint(WoodCol));
        }
        return pieces;
    }

    // 성문: 양옆 탑 + 문 위를 잇는 다리(통로). 문 아래는 비어 있어 지나갈 수 있음
    int Gatehouse(Transform parent, Vector2 c, float gate, float towerS, float towerH, float wallT, Color stone, bool alongX, bool post = false)
    {
        Vector2 side = alongX ? new Vector2(gate / 2 + towerS / 2, 0f) : new Vector2(0f, gate / 2 + towerS / 2);
        Tower(parent, c - side, towerS, towerH, stone, true, post);
        Tower(parent, c + side, towerS, towerH, stone, true, post);
        float bh = 6f, by = towerH - 4f - bh;
        Vector3 size = alongX ? new Vector3(gate + 2f, bh, wallT + 2f) : new Vector3(wallT + 2f, bh, gate + 2f);
        var bridge = Part("Building", parent, P(c, by + bh / 2), size, null, stoneMat, stone);
        CrenelStrip(bridge.transform, c, gate + 2f, 1.4f, by + bh, alongX, stone * 0.97f);
        return 3;
    }

    // 도시 둘레 성벽: 도시 가장자리에서 조금 밖. 두 블록마다 망루, 네 변 가운데에 성문
    int CityWall(Transform parent, float sizeX, float sizeZ, int cx, int cz, float pitch)
    {
        int n = 0;
        Color stone = Tint(CastleStone);
        // 성문은 크게: 문 폭 40m, 양옆 탑 18m·36m → 문 위 다리 아랫면이 약 26m라 처음 거인(약 18m)이 고개 숙이지 않고 지나감
        const float off = 14f, thick = 7f, h = 13f, towerS = 14f, towerH = 24f, gate = 40f, gateTowerS = 18f, gateTowerH = 36f;
        float x0 = -off, x1 = sizeX + off, z0 = -off, z1 = sizeZ + off;
        // 변 하나: 두 블록마다 망루 (cornerTowers면 양 끝 모서리에도), gateAt(시작점에서 거리)에는 성문
        void Side(Vector2 a, Vector2 b, float gateAt, bool cornerTowers)
        {
            Vector2 dir = (b - a).normalized;
            float len = Vector2.Distance(a, b);
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            // 성 밖 방향 (도시 가운데에서 멀어지는 쪽)
            Vector2 mid = (a + b) * 0.5f, ctr = new Vector2(sizeX, sizeZ) * 0.5f;
            Vector2 outward = alongX ? new Vector2(0f, Mathf.Sign(mid.y - ctr.y)) : new Vector2(Mathf.Sign(mid.x - ctr.x), 0f);
            var stops = new List<float> { 0f };
            for (float s = pitch * 2f; s < len - pitch; s += pitch * 2f) if (Mathf.Abs(s - gateAt) > pitch) stops.Add(s);
            stops.Add(len);
            for (int i = 0; i < stops.Count; i++)
            {
                bool corner = i == 0 || i == stops.Count - 1;
                if (!corner || cornerTowers) { Tower(parent, a + dir * stops[i], towerS, towerH, stone, true); n++; }
                if (i == stops.Count - 1) break;
                float s0 = stops[i], s1 = stops[i + 1];
                if (gateAt > s0 && gateAt < s1)
                {
                    // 성문 양옆 탑 가운데까지 성벽 (탑 반만큼 덜 세움)
                    n += WallRun(parent, a + dir * s0, a + dir * (gateAt - gate / 2 - gateTowerS / 2), thick, h, stone, towerS, gateTowerS, outward);
                    n += Gatehouse(parent, a + dir * gateAt, gate, gateTowerS, gateTowerH, thick, stone, alongX);
                    n += WallRun(parent, a + dir * (gateAt + gate / 2 + gateTowerS / 2), a + dir * s1, thick, h, stone, gateTowerS, towerS, outward);
                }
                else n += WallRun(parent, a + dir * s0, a + dir * s1, thick, h, stone, towerS, -1f, outward);
            }
        }
        float gateX = roadWidth * 0.5f + cx * pitch + off, gateZ = roadWidth * 0.5f + cz * pitch + off; // 가운데 큰길과 만나는 곳 (변 시작점에서 거리)
        Side(new Vector2(x0, z0), new Vector2(x1, z0), gateX, true);   // 남 (모서리 탑 포함)
        Side(new Vector2(x0, z1), new Vector2(x1, z1), gateX, true);   // 북
        Side(new Vector2(x0, z0), new Vector2(x0, z1), gateZ, false);  // 서 (모서리 탑은 이미 세움)
        Side(new Vector2(x1, z0), new Vector2(x1, z1), gateZ, false);  // 동
        return n;
    }
}
