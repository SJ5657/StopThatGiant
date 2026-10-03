using System.Collections.Generic;
using UnityEngine;

// 길가 아이템 + 패시브 아이템.
// 거인 주변 도로 위에 아이템 상자가 랜덤으로 나타나고, 거인이 밟으면 아이템 카드 3장 중 1장을 골라 장착 (CardDraft로 표시).
// 패시브 아이템 = 특수 건물 생성기: 장착하면 즉시, 그리고 일정 주기마다 거인 주변 흰 건물이 그 특수 건물로 바뀜.
// 같은 아이템을 또 고르면 강화(Lv +1)되어 한 번에 생기는 건물 수가 늘어남. 보유 개수 제한 없음.
public class PassiveItems : MonoBehaviour
{
    public static PassiveItems Instance { get; private set; }

    [Header("길가 아이템")]
    [Tooltip("동시에 놓여 있을 수 있는 아이템 수")] public int maxPickups = 3;
    [Tooltip("아이템이 새로 생기는 간격(초)")] public float pickupInterval = 12f;
    [Tooltip("거인으로부터 이 거리 범위의 도로 위에 생김 (m)")] public Vector2 pickupDistance = new Vector2(120f, 320f);
    [Tooltip("거인과 이만큼 멀어지면 사라짐 (m)")] public float despawnDistance = 900f;
    [Tooltip("줍는 반경 (거인 크기 기준)")] public float pickupRadius = 0.7f;
    public Color pickupColor = new Color(0.75f, 0.45f, 1f);

    [Header("특수 건물 아이템")]
    [Tooltip("일반 등급 1장으로 늘어나는 '한 번에 생기는 건물 수'")] public float buildingsPerCard = 2f;
    [Tooltip("장착 중 건물이 생기는 주기(초)")] public float spawnPeriod = 15f;
    [Tooltip("종류별로 동시에 있을 수 있는 최대 수")] public int maxAlivePerType = 40;

    class Item
    {
        public string name, icon, desc;
        public Color color;
        public SpecialBuildingSet set;
        public int level;        // 0 = 미보유
        public float perSpawn;   // 한 번에 생기는 건물 수
        public float nextAt;
    }
    readonly List<Item> items = new List<Item>();
    readonly List<Item> owned = new List<Item>();
    readonly List<Transform> pickups = new List<Transform>();
    float nextPickupAt;
    CityGenerator city;
    GiantController ctrl;
    string toast; Color toastColor; float toastUntil;
    static Mesh ringMesh, beamMesh;

    void Awake() { Instance = this; }

    void Start()
    {
        city = FindObjectOfType<CityGenerator>();
        ctrl = FindObjectOfType<GiantController>();
        AddItem(FindObjectOfType<ExplosiveBuildings>(), "폭약 창고", "폭약", "빨간 건물이 생겨남.\n부수면 폭발해 주변까지 파괴.", new Color(1f, 0.35f, 0.3f));
        AddItem(FindObjectOfType<BoostBuildings>(), "발전소", "발전", "노란 건물이 생겨남.\n부수면 스테미나 + 자동 질주.", new Color(1f, 0.85f, 0.2f));
        AddItem(FindObjectOfType<HealBuildings>(), "구호소", "구호", "초록 건물이 생겨남.\n부수면 HP 회복 영역 생성.", new Color(0.35f, 0.9f, 0.45f));
        nextPickupAt = Time.time + 3f;
    }

    void AddItem(SpecialBuildingSet set, string name, string icon, string desc, Color color)
    {
        if (set) items.Add(new Item { set = set, name = name, icon = icon, desc = desc, color = color });
    }

    void Update()
    {
        if (GameStartMenu.InMenu || CardDraft.IsOpen) return;
        var g = GiantHealth.Instance;
        if (!g || g.IsDead) return;
        Vector3 gp = g.transform.position;

        // 장착한 아이템: 주기마다 특수 건물 생성
        foreach (var it in owned)
            if (Time.time >= it.nextAt) { it.nextAt = Time.time + spawnPeriod; SpawnBuildings(it, false); }

        // 길가 아이템: 줍기 / 멀어지면 제거 / 모자라면 생성
        float pickR = pickupRadius * (ctrl ? ctrl.GameScale : 27f);
        for (int i = pickups.Count - 1; i >= 0; i--)
        {
            var p = pickups[i];
            if (!p) { pickups.RemoveAt(i); continue; }
            Vector3 d = p.position - gp; d.y = 0f;
            if (d.sqrMagnitude <= pickR * pickR) { Collect(p); pickups.RemoveAt(i); }
            else if (d.sqrMagnitude > despawnDistance * despawnDistance) { Destroy(p.gameObject); pickups.RemoveAt(i); }
        }
        if (items.Count > 0 && pickups.Count < maxPickups && Time.time >= nextPickupAt)
        {
            nextPickupAt = Time.time + pickupInterval;
            if (TryRoadPoint(gp, out Vector3 pos)) pickups.Add(CreatePickup(pos));
        }

        // 아이템 상자 애니메이션 (둥실둥실 + 회전)
        foreach (var p in pickups)
        {
            if (!p) continue;
            var gem = p.GetChild(0);
            gem.localPosition = Vector3.up * (16f + Mathf.Sin(Time.time * 2.5f + p.GetInstanceID()) * 3f);
            gem.localRotation = Quaternion.Euler(45f, Time.time * 90f, 45f);
        }
    }

    // ───────────── 패시브 아이템 ─────────────
    void SpawnBuildings(Item it, bool announce)
    {
        int want = Mathf.Min(Mathf.RoundToInt(it.perSpawn), maxAlivePerType - it.set.Count);
        if (want <= 0) return;
        int n = it.set.Spawn(want);
        if (announce && n > 0) Toast($"주변에 {it.set.DisplayName} {n}채가 나타났다!", it.color);
    }

    void Collect(Transform p)
    {
        Destroy(p.gameObject);
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.15f);
        CardDraft.Enqueue(new CardDraft.Request
        {
            title = "아이템 획득!",
            titleColor = pickupColor,
            subtitle = "장착할 패시브 아이템을 고르세요",
            build = BuildItemCards
        });
    }

    List<CardDraft.Card> BuildItemCards()
    {
        var pool = new List<Item>(items);
        var result = new List<CardDraft.Card>();
        if (pool.Count == 0) return result;
        for (int i = pool.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

        for (int i = 0; i < 3; i++)
        {
            var it = pool[i % pool.Count];
            int r = CardDraft.RollRarity();
            float amount = buildingsPerCard * CardDraft.RarityPower[r];
            bool isNew = it.level == 0;
            result.Add(new CardDraft.Card
            {
                badge = isNew ? "NEW  새 아이템" : $"강화  Lv {it.level} → {it.level + 1}",
                badgeColor = isNew ? new Color(1f, 0.85f, 0.3f) : new Color(0.55f, 0.9f, 1f),
                name = it.name, stat = $"{it.set.DisplayName} 생성", color = it.color, rarity = r,
                amount = $"+{amount:0.#}채", desc = it.desc,
                current = isNew ? "미보유" : $"{it.perSpawn:0.#}채", preview = $"{it.perSpawn + amount:0.#}채",
                footer = $"장착 즉시 + {spawnPeriod:0}초마다 주변에 생성",
                pick = () => Equip(it, amount)
            });
        }
        return result;
    }

    void Equip(Item it, float amount)
    {
        if (it.level == 0) owned.Add(it);
        it.level++;
        it.perSpawn += amount;
        it.nextAt = Time.time + spawnPeriod;
        SpawnBuildings(it, true);
    }

    // ───────────── 길가 아이템 ─────────────
    // 거인 주변 거리 범위 안의 도로 한가운데 지점 찾기
    bool TryRoadPoint(Vector3 center, out Vector3 pos)
    {
        pos = Vector3.zero;
        if (!city) return false;
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 origin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f;
        float half = city.roadWidth * 0.5f;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f), dist = Random.Range(pickupDistance.x, pickupDistance.y);
            Vector3 p = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
            // 가장 가까운 세로/가로 도로 중심선으로 붙임
            float rx = origin.x + half + Mathf.Round((p.x - origin.x - half) / pitch) * pitch;
            float rz = origin.z + half + Mathf.Round((p.z - origin.z - half) / pitch) * pitch;
            if (Mathf.Abs(p.x - rx) < Mathf.Abs(p.z - rz)) p.x = rx; else p.z = rz;
            if (p.x < origin.x || p.x > origin.x + sizeX || p.z < origin.z || p.z > origin.z + sizeZ) continue;
            if (ctrl && ctrl.areaHalfExtent.x > 0 && (Mathf.Abs(p.x) > ctrl.areaHalfExtent.x || Mathf.Abs(p.z) > ctrl.areaHalfExtent.y)) continue;
            Vector3 d = p - center; d.y = 0f;
            if (d.magnitude < pickupDistance.x * 0.8f) continue;
            bool crowded = false;
            foreach (var q in pickups) if (q && (q.position - p).sqrMagnitude < 80f * 80f) { crowded = true; break; }
            if (crowded) continue;
            pos = new Vector3(p.x, 0.5f, p.z);
            return true;
        }
        return false;
    }

    // 아이템 상자: 빛나는 보석 + 하늘로 뻗는 빛기둥 + 바닥 원
    Transform CreatePickup(Vector3 pos)
    {
        if (!ringMesh)
        {
            Color c = pickupColor;
            ringMesh = WarningZone.Annulus(0.8f, 1f, new Color(c.r, c.g, c.b, 0.9f), new Color(c.r, c.g, c.b, 0.9f));
            beamMesh = WarningZone.Wall(new Color(c.r, c.g, c.b, 0.55f), new Color(c.r, c.g, c.b, 0f));
        }
        var root = new GameObject("ItemPickup").transform;
        root.position = pos;

        var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(gem.GetComponent<Collider>());
        gem.name = "Gem";
        gem.transform.SetParent(root, false);
        gem.transform.localScale = Vector3.one * 9f;
        var gr = gem.GetComponent<MeshRenderer>();
        gr.sharedMaterial = WarningZone.material;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_Color", Color.Lerp(pickupColor, Color.white, 0.25f));
        gr.SetPropertyBlock(mpb);
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Part(root, "Beam", beamMesh, new Vector3(5f, 140f, 5f), Vector3.zero);
        Part(root, "Ring", ringMesh, new Vector3(14f, 1f, 14f), Vector3.up * 0.3f);
        return root;
    }

    static void Part(Transform parent, string name, Mesh mesh, Vector3 scale, Vector3 localPos)
    {
        var p = new GameObject(name);
        p.transform.SetParent(parent, false);
        p.transform.localPosition = localPos;
        p.transform.localScale = scale;
        p.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = p.AddComponent<MeshRenderer>();
        mr.sharedMaterial = WarningZone.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // ───────────── 화면 ─────────────
    void Toast(string text, Color c) { toast = text; toastColor = c; toastUntil = Time.unscaledTime + 3f; }

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return; // 시작 메뉴 중에는 HUD 숨김
        DrawOwned();
        float left = toastUntil - Time.unscaledTime;
        if (!CardDraft.IsOpen && !string.IsNullOrEmpty(toast) && left > 0f)
        {
            var c = toastColor; c.a = Mathf.Clamp01(left / 0.5f);
            CardDraft.ShadowLabel(new Rect(0, Screen.height * 0.16f, Screen.width, 34), toast, CardDraft.Style(22, FontStyle.Bold, TextAnchor.MiddleCenter), c);
        }
    }

    // 장착한 패시브 아이템 (HP/스테미나/공격력 아래, 한 줄에 5개씩)
    void DrawOwned()
    {
        const float size = 46f, gap = 8f, x0 = 20f, y0 = 92f;
        const int perRow = 5;
        for (int i = 0; i < owned.Count; i++)
        {
            var it = owned[i];
            var r = new Rect(x0 + (i % perRow) * (size + gap), y0 + (i / perRow) * (size + gap), size, size);
            CardDraft.Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.6f));
            CardDraft.Box(r, new Color(it.color.r * 0.45f, it.color.g * 0.45f, it.color.b * 0.45f, 0.95f));
            // 다음 생성까지 남은 시간 (아래에서 차오름)
            float k = 1f - Mathf.Clamp01((it.nextAt - Time.time) / spawnPeriod);
            CardDraft.Box(new Rect(r.x, r.yMax - r.height * k, r.width, r.height * k), new Color(1f, 1f, 1f, 0.12f));
            CardDraft.ShadowLabel(new Rect(r.x, r.y + 2, r.width, r.height - 14), it.icon, CardDraft.Style(14, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
            CardDraft.ShadowLabel(new Rect(r.x, r.yMax - 16, r.width - 3, 15), $"Lv{it.level}", CardDraft.Style(11, FontStyle.Bold, TextAnchor.MiddleRight), it.color);
        }
    }
}
