using System.Collections.Generic;
using UnityEngine;

// 특수 건물(빨강·노랑·초록 등) 공통: 건물을 특정 색으로 칠하고 목록으로 관리.
// 게임 시작 시 ratio 비율만큼 칠하고(기본 0 = 처음엔 흰 건물만), 레벨업 카드로 Spawn을 불러 거인 주변에 새로 등장시킴.
// 서로 다른 특수 건물끼리는 절대 겹치지 않음.
public abstract class SpecialBuildingSet : MonoBehaviour
{
    [Header("배치")]
    [Tooltip("게임 시작 시 이 건물로 바꿀 비율 (0 = 처음엔 없고 레벨업 카드로만 등장)")] [Range(0, 0.2f)] public float ratio = 0f;
    [Tooltip("0이면 매번 다른 배치")] public int seed = 0;
    public Color color = Color.white;
    [Tooltip("카드로 등장시킬 때 거인 주변 이 반경 안의 건물에서 고름 (m)")] public float spawnRadius = 350f;
    [Tooltip("거인 바로 옆에는 생기지 않게 하는 최소 거리 (m)")] public float spawnMinDistance = 60f;

    // 화면 안내용 이름 (예: "빨간 건물")
    public abstract string DisplayName { get; }

    readonly HashSet<GameObject> members = new HashSet<GameObject>();
    static readonly List<SpecialBuildingSet> sets = new List<SpecialBuildingSet>();
    static List<GameObject> buildings; // 건물 본체 목록 (위층 제외) 캐시
    static readonly int ColorId = Shader.PropertyToID("_Color");
    System.Random rng;
    MaterialPropertyBlock mpb;

    public int Count => members.Count;
    public bool Contains(GameObject building) => members.Contains(building);
    protected bool Take(GameObject building) => members.Remove(building);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sets.Clear(); buildings = null; }

    protected virtual void Awake()
    {
        sets.Add(this);
        rng = seed != 0 ? new System.Random(seed) : new System.Random();
        mpb = new MaterialPropertyBlock();
        mpb.SetColor(ColorId, color);
    }

    protected virtual void OnDestroy()
    {
        sets.Remove(this);
        if (sets.Count == 0) buildings = null; // 씬이 바뀌면 다시 수집
    }

    // 모든 특수 건물이 Awake에서 등록된 뒤에 고르므로 실행 순서와 관계없이 겹치지 않음
    protected virtual void Start()
    {
        if (ratio <= 0f) return;
        foreach (var b in Buildings)
            if (b && !IsSpecial(b) && rng.NextDouble() < ratio) Paint(b);
    }

    public static bool IsSpecial(GameObject building)
    {
        foreach (var s in sets) if (s.members.Contains(building)) return true;
        return false;
    }

    static List<GameObject> Buildings
    {
        get
        {
            if (buildings != null) return buildings;
            buildings = new List<GameObject>();
            int layer = LayerMask.NameToLayer("Building");
            foreach (var col in FindObjectsOfType<BoxCollider>())
            {
                var go = col.gameObject;
                if (go.layer != layer || (go.transform.parent && go.transform.parent.gameObject.layer == layer)) continue;
                buildings.Add(go);
            }
            return buildings;
        }
    }

    // 거인 주변의 멀쩡한 흰 건물 count채를 이 건물로 바꿈. 실제로 바꾼 수를 돌려줌.
    public int Spawn(int count)
    {
        var g = GiantHealth.Instance;
        Vector3 c = g ? g.transform.position : Vector3.zero;
        float rMin = spawnMinDistance * spawnMinDistance, rMax = spawnRadius * spawnRadius;

        var near = new List<GameObject>();
        var far = new List<GameObject>();
        foreach (var b in Buildings)
        {
            // 이미 무너졌거나, 특수 건물이거나, 거인에게 맞아 금이 간 건물은 제외
            if (!b || !b.activeInHierarchy || IsSpecial(b) || b.GetComponent<BuildingHealth>()) continue;
            Vector3 d = b.transform.position - c; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < rMin) continue;
            (sq <= rMax ? near : far).Add(b);
        }

        int n = 0;
        n += PaintRandom(near, count);
        if (n < count) n += PaintRandom(far, count - n); // 주변에 모자라면 도시 어디서든
        return n;
    }

    int PaintRandom(List<GameObject> pool, int count)
    {
        int n = 0;
        for (int i = 0; i < pool.Count && n < count; i++)
        {
            int j = rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            Paint(pool[i]);
            n++;
        }
        return n;
    }

    void Paint(GameObject b)
    {
        members.Add(b);
        foreach (var r in b.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
    }
}
