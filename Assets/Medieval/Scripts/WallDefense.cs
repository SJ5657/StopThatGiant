using System.Collections.Generic;
using UnityEngine;

// 성벽 수비대 (중세): 도시 성벽 위 길(WallWalk, CityGenerator가 깔음)을 따라 궁수가 오가며 화살을 쏘고, 군데군데 투석기가 돌을 던짐.
// 성벽 조각마다 자리(slot)를 미리 정해 두고, 거인이 activeRange 안에 오면 그 자리에 실제로 세움 (멀어지면 치움, 죽은 자리는 다시 안 세움).
// 수비병 하나하나의 행동은 WallGuard.
[DefaultExecutionOrder(150)] // 마을 생성(-900)·TallBuildings(100) 뒤
public class WallDefense : MonoBehaviour
{
    [Header("배치")]
    [Tooltip("성벽 조각(약 30m)마다 궁수가 있을 확률")] [Range(0f, 1f)] public float archerChance = 0.6f;
    [Tooltip("궁수가 있는 조각에서 두 명일 확률")] [Range(0f, 1f)] public float pairChance = 0.2f;
    [Tooltip("성벽 조각 몇 개마다 투석기 하나")] public int catapultEvery = 6;
    [Tooltip("거인이 이 거리(m) 안에 오면 수비병을 세움")] public float activeRange = 320f;
    [Tooltip("궁수 모델 색 (MaterialPropertyBlock)")] public Color archerTint = new Color(0.35f, 0.55f, 0.3f);

    [Header("탑 옥상 (마을 안 돌탑·성 망루의 TowerTop)")]
    [Tooltip("탑 옥상에 궁수가 있을 확률")] [Range(0f, 1f)] public float towerArcherChance = 0.85f;
    [Tooltip("옥상 바닥 한 변이 이 길이(m) 이상이면 투석기를 놓을 수 있음")] public float towerCatapultMinSize = 12f;
    [Tooltip("넓은 옥상에 투석기가 있을 확률")] [Range(0f, 1f)] public float towerCatapultChance = 0.25f;

    [Header("궁수")]
    public float archerRange = 130f;
    public float archerDamage = 1.5f;
    [Range(0f, 1f)] public float archerAccuracy = 0.4f;
    public float archerInterval = 3.5f;
    public int archerPoints = 40;

    [Header("투석기")]
    public float catapultRange = 200f;
    public float boulderDamage = 12f;
    public float boulderSpeed = 100f;
    public float catapultInterval = 9f;
    public int catapultPoints = 300;
    [Tooltip("투석기 모델 (비우면 MilitarySpawner의 tankPrefab = 투석기)")] public GameObject catapultPrefab;
    [Tooltip("투석기 크기 (가로 최대 길이, m)")] public float catapultSize = 7f;

    [Tooltip("거인이 성벽 위 수비병을 쓸어버리는 거리 = 거인 몸 반지름 + 거인 키 × 이 값")] public float swatReachPerHeight = 0.35f;
    public float SwatReach { get; private set; }

    struct Slot { public Transform wall; public Vector3 pos, a, b, along; public WallGuard.Kind kind; public bool dead; public WallGuard live; }
    readonly List<Slot> slots = new List<Slot>();
    float nextCheck;
    GameObject[] archerPrefabs; RuntimeAnimatorController archerController; float archerScale = 1.2f;

    void Start()
    {
        var city = FindObjectOfType<CityGenerator>();
        if (!city) { enabled = false; return; }
        var cm = CivilianManager.Instance ? CivilianManager.Instance : FindObjectOfType<CivilianManager>();
        if (cm) { archerPrefabs = cm.prefabs; archerController = cm.locomotion; archerScale = cm.scale; }
        if (!catapultPrefab && MilitarySpawner.Instance) catapultPrefab = MilitarySpawner.Instance.tankPrefab;

        int idx = 0, towerSlots = 0;
        foreach (var t in city.GetComponentsInChildren<Transform>())
        {
            if (t.name == "TowerTop" && t.parent) { towerSlots += AddTowerTop(t); continue; }
            if (t.name != "WallWalk" || !t.parent) continue;
            Vector3 s = t.lossyScale;
            bool alongX = s.x >= s.z;
            float len = alongX ? s.x : s.z;
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            Vector3 c = t.position + Vector3.up * s.y * 0.5f;
            Vector3 face = Vector3.Cross(Vector3.up, along); // 처음 바라보는 방향 (아무 쪽이나)
            bool cat = catapultEvery > 0 && idx % catapultEvery == catapultEvery / 2 && len > 16f;
            idx++;
            if (cat) slots.Add(new Slot { wall = t.parent, pos = c, along = face, kind = WallGuard.Kind.Catapult });
            if (Random.value >= archerChance) continue;
            float half = len * 0.5f - 2f;
            if (cat || Random.value < pairChance)
            {
                // 두 명: 왼쪽·오른쪽 반씩 (투석기가 있으면 가운데를 비움)
                float gap = cat ? 5f : 0.5f;
                AddArcher(t.parent, c, along, -half, -gap);
                if (!cat || Random.value < 0.7f) AddArcher(t.parent, c, along, gap, half);
            }
            else AddArcher(t.parent, c, along, -half, half);
        }
        Debug.Log($"[WallDefense] 수비병 자리 {slots.Count}곳 (그중 탑 옥상 {towerSlots}곳)");
    }

    // 탑 옥상: 넓으면 가운데 투석기 + 가장자리를 오가는 궁수, 좁으면 대각선으로 오가는 궁수 하나
    int AddTowerTop(Transform t)
    {
        Vector3 s = t.lossyScale;
        float w = Mathf.Min(s.x, s.z);
        if (w < 3f) return 0;
        Vector3 c = t.position + Vector3.up * s.y * 0.5f;
        float q = w * 0.5f - 1.2f;
        int n = 0;
        bool cat = w >= towerCatapultMinSize && Random.value < towerCatapultChance;
        Vector3 face = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f) * Vector3.forward;
        if (cat) { slots.Add(new Slot { wall = t.parent, pos = c, along = face, kind = WallGuard.Kind.Catapult }); n++; }
        if (Random.value < towerArcherChance || cat)
        {
            if (cat) AddArcher(t.parent, c + new Vector3(0f, 0f, q), Vector3.right, -q, q);
            else AddArcher(t.parent, c, new Vector3(1f, 0f, 1f).normalized, -q * 1.3f, q * 1.3f);
            n++;
            if (cat && w >= 18f) { AddArcher(t.parent, c - new Vector3(0f, 0f, q), Vector3.right, -q, q); n++; }
        }
        return n;
    }

    void AddArcher(Transform wall, Vector3 c, Vector3 along, float from, float to)
    {
        if (to - from < 2f) return;
        var a = c + along * from; var b = c + along * to;
        slots.Add(new Slot { wall = wall, a = a, b = b, pos = Vector3.Lerp(a, b, Random.value), along = along, kind = WallGuard.Kind.Archer });
    }

    public void OnGuardDied(int slot)
    {
        if (slot < 0 || slot >= slots.Count) return;
        var s = slots[slot]; s.dead = true; s.live = null; slots[slot] = s;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.4f;
        var g = GiantHealth.Instance;
        if (!g) return;
        var cc = g.GetComponent<CharacterController>();
        float height = (g.ShoulderY - g.transform.position.y) / 0.837f;
        SwatReach = (cc ? cc.radius * Mathf.Max(g.transform.lossyScale.x, 0.01f) : 2f) + height * swatReachPerHeight;
        Vector3 gp = g.transform.position; gp.y = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s.dead) continue;
            if (!s.wall || !s.wall.gameObject.activeInHierarchy) { if (!s.live) { s.dead = true; slots[i] = s; } continue; } // 성벽이 먼저 부서진 자리
            Vector3 p = s.pos; p.y = 0f;
            float d = Vector3.Distance(p, gp);
            if (!s.live && d < activeRange) { s.live = Spawn(s, i); slots[i] = s; }
            else if (s.live && !s.live.Dead && d > activeRange + 120f)
            {
                s.pos = s.live.transform.position; // 다음에 세울 때 그 자리에서
                Destroy(s.live.gameObject); s.live = null; slots[i] = s;
            }
        }
    }

    WallGuard Spawn(Slot s, int index)
    {
        GameObject go;
        if (s.kind == WallGuard.Kind.Archer)
        {
            if (archerPrefabs == null || archerPrefabs.Length == 0) return null;
            go = Instantiate(archerPrefabs[Random.Range(0, archerPrefabs.Length)], s.pos, Quaternion.LookRotation(s.along));
            go.name = "WallArcher";
            go.transform.localScale = Vector3.one * archerScale;
            var anim = go.GetComponentInChildren<Animator>();
            if (anim) { anim.runtimeAnimatorController = archerController; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
            var mpb = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.GetPropertyBlock(mpb); mpb.SetColor("_Color", archerTint); r.SetPropertyBlock(mpb); } // 초록 옷 궁수
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        else
        {
            if (!catapultPrefab) return null;
            // 투석기 모델만 빌려 씀: 비활성 상태로 만들어 원래 행동(TankAI 등)과 충돌체를 빼고 팔(CatapultArm)만 남김
            var holder = new GameObject("WallCatapultHolder");
            holder.SetActive(false);
            go = Instantiate(catapultPrefab, s.pos, Quaternion.LookRotation(s.along), holder.transform);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is CatapultArm)) DestroyImmediate(mb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            go.transform.SetParent(null, true);
            Destroy(holder);
            go.name = "WallCatapult";
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float cur = Mathf.Max(b.size.x, b.size.z);
                if (cur > 0.1f) go.transform.localScale *= catapultSize / cur;
            }
        }
        var gd = go.AddComponent<WallGuard>();
        gd.kind = s.kind; gd.wall = s.wall; gd.endA = s.a; gd.endB = s.b; gd.owner = this; gd.slot = index; gd.scale = archerScale; gd.onTower = s.wall.Find("TowerTop");
        return gd;
    }
}
