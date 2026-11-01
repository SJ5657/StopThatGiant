using System.Collections.Generic;
using UnityEngine;

// 병력 마차 내보내기 (중세): 궁수를 태운 마차가 화면 밖에서 달려옴. 거인이 마을 안이면 도로로, 마을 밖이면 언덕으로. 레벨이 오를수록 늘어남.
// 경비대 마차(PoliceUnit)는 도로에서만 움직이고 병력이 내려서 쏘지만, 이 마차는 어디서든 달리면서 쏨. 행동은 TroopWagon.
public class TroopWagonSpawner : MonoBehaviour
{
    [Tooltip("마차 모델 (MedievalBuilder가 만든 TroopWagon)")] public GameObject wagonPrefab;

    [Header("등장")]
    [Tooltip("거인이 이 레벨 이상일 때부터 등장")] public int fromLevel = 1;
    public int baseCount = 2;
    [Tooltip("이 레벨마다 한 대씩 늘어남")] public int levelsPerExtra = 2;
    public int maxCount = 8;
    [Tooltip("한 대 내보내고 다음까지 (초)")] public float spawnInterval = 10f;
    [Tooltip("거인에게서 이 거리 범위에 나타남 (거인 키 × 값)")] public Vector2 spawnDistancePerHeight = new Vector2(7f, 10f);
    public float despawnDistance = 500f;

    [Header("마차")]
    [Tooltip("모델 크기 배율")] public float wagonScale = 3f;
    [Range(1, 4)] public int riderCount = 4;
    [Tooltip("최저 속도 (m/s)")] public float minSpeed = 24f;
    [Tooltip("속도 = 거인 키 × 값 (거인 달리기는 약 키 × 1.9라 달려가면 잡힘)")] public float speedPerHeight = 1.3f;
    [Tooltip("거인과 유지하는 거리 = 거인 키 × 값")] public float preferRangePerHeight = 3.5f;
    [Tooltip("이보다 가까우면 달아남 = 거인 키 × 값")] public float minRangePerHeight = 1.8f;

    [Header("궁수")]
    [Tooltip("사거리 = 거인 키 × 값")] public float rangePerHeight = 6f;
    public float fireInterval = 3f;
    [Range(0f, 1f)] public float accuracy = 0.45f;
    public float arrowDamage = 2f;
    public Color riderTint = new Color(0.62f, 0.25f, 0.2f);
    public int wagonPoints = 200;
    public int riderPoints = 40;

    readonly List<TroopWagon> alive = new List<TroopWagon>();
    float nextSpawn;
    CityGenerator city;
    Vector3 townMin, townMax;
    static readonly Vector3[] Seats =
    {
        new Vector3(-0.55f, 1.2f, -0.8f), new Vector3(0.55f, 1.2f, -0.8f), // 짐칸 윗면 (모델 기준 좌표)
        new Vector3(-0.55f, 1.2f, -2.3f), new Vector3(0.55f, 1.2f, -2.3f),
    };

    void Start()
    {
        city = FindObjectOfType<CityGenerator>();
        nextSpawn = Time.time + 15f;
        if (!city) return;
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        townMin = city.transform.position - new Vector3(sizeX, 0f, sizeZ) * 0.5f;
        townMax = townMin + new Vector3(sizeX, 0f, sizeZ);
    }

    // p가 마을 안인지 (margin만큼 넓혀서 봄)
    public bool InTown(Vector3 p, float margin) => city && p.x > townMin.x - margin && p.x < townMax.x + margin && p.z > townMin.z - margin && p.z < townMax.z + margin;

    // p에서 가장 가까운 성벽 변의 바깥 방향
    public Vector3 OutwardFromTown(Vector3 p)
    {
        float w = Mathf.Abs(p.x - townMin.x), e = Mathf.Abs(townMax.x - p.x), s = Mathf.Abs(p.z - townMin.z), n = Mathf.Abs(townMax.z - p.z);
        float m = Mathf.Min(Mathf.Min(w, e), Mathf.Min(s, n));
        return m == w ? Vector3.left : m == e ? Vector3.right : m == s ? Vector3.back : Vector3.forward;
    }

    void Update()
    {
        alive.RemoveAll(a => !a || a.IsDead);
        var g = GiantHealth.Instance;
        var lv = LevelSystem.Instance;
        if (!g || g.IsDead || GameStartMenu.InMenu || GiantHome.GiantSafe || EnemyFreeze.Active || MilitarySpawner.RetreatLeft > 0f || !lv || lv.Level < fromLevel || !wagonPrefab) return;
        int want = Mathf.Min(maxCount, baseCount + (levelsPerExtra > 0 ? (lv.Level - fromLevel) / levelsPerExtra : 0));
        if (alive.Count >= want || Time.time < nextSpawn) return;
        nextSpawn = Time.time + spawnInterval;
        Spawn(g);
    }

    void Spawn(GiantHealth g)
    {
        var cm = CivilianManager.Instance ? CivilianManager.Instance : FindObjectOfType<CivilianManager>();
        if (!cm || cm.prefabs == null || cm.prefabs.Length == 0) return;
        float h = Mathf.Max(2f, (g.ShoulderY - g.transform.position.y) / 0.837f);
        Vector3 gp = g.transform.position;
        bool town = InTown(gp, 0f);
        Vector3 pos = gp;
        for (int tries = 0; tries < 12; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            pos = gp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * h * Random.Range(spawnDistancePerHeight.x, spawnDistancePerHeight.y);
            if (town) { if (!InTown(pos, 0f)) continue; pos = SnapToNode(pos); }
            else if (InTown(pos, 60f)) continue; // 마을 밖 마차는 성벽 바깥에만
            if (MilitarySpawner.OutOfSight(pos)) break;
        }
        if (town != InTown(pos, 0f)) return; // 알맞은 자리를 못 찾음: 다음 차례에
        pos.y = WildTerrain.Active ? WildTerrain.Height(pos.x, pos.z) : 0f;
        Vector3 look = gp - pos; look.y = 0f;

        var root = new GameObject("TroopWagon");
        root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));
        var model = Instantiate(wagonPrefab, root.transform).transform;
        model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one * wagonScale;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        var w = root.AddComponent<TroopWagon>();
        w.owner = this; w.model = model; w.riderScale = cm.scale;
        for (int i = 0; i < riderCount && i < Seats.Length; i++) w.AddRider(MakeRider(cm, root.transform, Seats[i] * wagonScale));
        alive.Add(w);
    }

    Vector3 SnapToNode(Vector3 p)
    {
        float pitch = city.blockSize + city.roadWidth;
        Vector3 o = townMin + new Vector3(city.roadWidth, 0f, city.roadWidth) * 0.5f;
        return new Vector3(o.x + Mathf.Clamp(Mathf.Round((p.x - o.x) / pitch), 0, city.blocksX) * pitch, 0f, o.z + Mathf.Clamp(Mathf.Round((p.z - o.z) / pitch), 0, city.blocksZ) * pitch);
    }

    // 짐칸에 선 궁수: 시민 모델 + 옷 색 + 손에 든 활 (세 토막으로 휜 모양)
    Transform MakeRider(CivilianManager cm, Transform parent, Vector3 seat)
    {
        var go = Instantiate(cm.prefabs[Random.Range(0, cm.prefabs.Length)], parent);
        go.name = "WagonArcher";
        go.transform.localPosition = seat;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * cm.scale;
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = cm.locomotion; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
        var mpb = new MaterialPropertyBlock();
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.GetPropertyBlock(mpb); mpb.SetColor("_Color", riderTint); r.SetPropertyBlock(mpb); }
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        var fx = CombatFX.Instance;
        Material wood = fx ? fx.javelinMaterial : null;
        BowPart(go.transform, new Vector3(0.45f, 2.6f, 0.75f), new Vector3(0.09f, 1.3f, 0.09f), 0f, wood);
        BowPart(go.transform, new Vector3(0.45f, 3.4f, 0.6f), new Vector3(0.08f, 0.6f, 0.08f), -32f, wood);
        BowPart(go.transform, new Vector3(0.45f, 1.8f, 0.6f), new Vector3(0.08f, 0.6f, 0.08f), 32f, wood);
        return go.transform;
    }

    static void BowPart(Transform parent, Vector3 pos, Vector3 size, float tilt, Material mat)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(part.GetComponent<Collider>());
        part.name = "Bow";
        part.transform.SetParent(parent, false);
        part.transform.localPosition = pos;
        part.transform.localRotation = Quaternion.Euler(tilt, 0f, 0f);
        part.transform.localScale = size;
        if (mat) part.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
