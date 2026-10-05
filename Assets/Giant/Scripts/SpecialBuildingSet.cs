using System.Collections.Generic;
using UnityEngine;

// 특수 건물(빨강·노랑·초록 등) 공통: 건물을 특정 색으로 칠하고 목록으로 관리.
// 게임 시작 시 ratio 비율만큼 칠하고(기본 0 = 처음엔 흰 건물만), 패시브 아이템이 등장 확률(Chance)을 정하면
// 거인 주변에 들어온 건물마다 그 확률로 이 건물로 바뀜.
// 서로 다른 특수 건물끼리는 절대 겹치지 않음.
public abstract class SpecialBuildingSet : MonoBehaviour
{
    [Header("배치")]
    [Tooltip("게임 시작 시 이 건물로 바꿀 비율 (0 = 처음엔 없고 레벨업 카드로만 등장)")] [Range(0, 0.2f)] public float ratio = 0f;
    [Tooltip("0이면 매번 다른 배치")] public int seed = 0;
    public Color color = Color.white;
    [Tooltip("등장 확률을 굴리는 거인 주변 반경 (m). 이 안에 처음 들어온 건물마다 한 번씩 굴림")] public float rollRadius = 350f;

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

    // ───────────── 등장 확률 (패시브 아이템) ─────────────
    // 거인 주변 rollRadius 안에 들어온 건물마다 딱 한 번 확률을 굴려, 당첨되면 이 건물로 바뀜.
    public float Chance { get; private set; }
    readonly HashSet<GameObject> rolled = new HashSet<GameObject>();

    // 이미 무너졌거나, 특수 건물이거나, 거인에게 맞아 금이 간 건물은 바꾸지 않음
    static bool Paintable(GameObject b) => b && b.activeInHierarchy && !IsSpecial(b) && !b.GetComponent<BuildingHealth>();

    // 아직 굴리지 않은 주변 건물에 확률 적용. 새로 바뀐 수를 돌려줌
    public int RollNearby(Vector3 center)
    {
        if (Chance <= 0f) return 0;
        float r2 = rollRadius * rollRadius;
        int n = 0;
        foreach (var b in Buildings)
        {
            if (!b) continue;
            Vector3 d = b.transform.position - center; d.y = 0f;
            if (d.sqrMagnitude > r2 || !rolled.Add(b)) continue;
            if (Paintable(b) && rng.NextDouble() < Chance) { Paint(b); n++; }
        }
        return n;
    }

    // 확률 변경. 올라간 경우 이미 굴린 건물에도 늘어난 만큼 추가로 굴려서 '처음부터 이 확률이었던 것'처럼 맞춤
    public int SetChance(float p)
    {
        float old = Chance;
        Chance = Mathf.Clamp01(p);
        if (Chance <= old || old >= 1f) return 0;
        float extra = (Chance - old) / (1f - old);
        int n = 0;
        foreach (var b in rolled)
            if (Paintable(b) && rng.NextDouble() < extra) { Paint(b); n++; }
        return n;
    }

    void Paint(GameObject b)
    {
        members.Add(b);
        foreach (var r in b.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
    }
}
