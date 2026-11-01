using System.Collections.Generic;
using UnityEngine;

// 작살병(루프건) 내보내기: 거인이 fromLevel 이상이면 거인 주변 화면 밖 쪽에서 작살병이 걸어 들어옴.
// 레벨이 오를수록 동시에 나오는 수가 늘어남. 도시 안이면 가까운 도로 위에 세움. (현대·중세 공용, 모델은 시민 모델에 색만 입힘)
public class RopeGunnerSpawner : MonoBehaviour
{
    [Header("등장")]
    [Tooltip("거인이 이 레벨 이상일 때부터 등장")] public int fromLevel = 2;
    [Tooltip("처음 동시에 있는 작살병 수")] public int baseCount = 2;
    [Tooltip("이 레벨마다 한 명씩 늘어남")] public int levelsPerExtra = 4;
    public int maxCount = 5;
    [Tooltip("한 명 내보내고 다음까지 (초)")] public float spawnInterval = 10f;
    [Tooltip("거인에게서 이 거리 범위(m)에 나타남 (거인 키 비례: 키 × 값)")] public Vector2 spawnDistancePerHeight = new Vector2(6f, 8f);
    public float despawnDistance = 600f;

    [Header("작살병")]
    public float runSpeed = 9f;
    [Tooltip("거인과 유지하는 거리 = 거인 키 × 값")] public float preferRangePerHeight = 3.5f;
    [Tooltip("이보다 가까우면 도망 = 거인 키 × 값")] public float minRangePerHeight = 1.6f;
    [Tooltip("사거리 = 거인 키 × 값")] public float fireRangePerHeight = 5.5f;
    public float fireInterval = 6f;
    [Range(0f, 1f)] public float accuracy = 0.65f;
    public float harpoonSpeed = 140f;
    [Tooltip("작살이 꽂힌 뒤 말뚝을 세워 박는 데 걸리는 시간 (초)")] public float anchorTime = 1.6f;
    [Tooltip("말뚝을 다 박고 다음 작살을 쏘기까지 (초)")] public float reloadAfterAnchor = 2.5f;
    public int points = 80;
    public Color modernTint = new Color(0.32f, 0.36f, 0.26f);
    public Color medievalTint = new Color(0.55f, 0.38f, 0.22f);

    readonly List<RopeGunner> alive = new List<RopeGunner>();
    float nextSpawn;
    CityGenerator city;

    void Start() { city = FindObjectOfType<CityGenerator>(); nextSpawn = Time.time + 5f; }

    void Update()
    {
        alive.RemoveAll(a => !a || a.IsDead);
        var g = GiantHealth.Instance;
        var lv = LevelSystem.Instance;
        if (!g || g.IsDead || GameStartMenu.InMenu || GiantHome.GiantSafe || EnemyFreeze.Active || !lv || lv.Level < fromLevel) return;
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
        Vector3 pos = gp;
        for (int tries = 0; tries < 10; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            pos = gp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * h * Random.Range(spawnDistancePerHeight.x, spawnDistancePerHeight.y);
            pos = OnRoad(city, pos);
            if (MilitarySpawner.OutOfSight(pos)) break; // 화면 밖에서 걸어 들어오게
        }
        pos.y = WildTerrain.Active ? WildTerrain.Height(pos.x, pos.z) : 0f;
        Vector3 look = gp - pos; look.y = 0f;
        var go = Instantiate(cm.prefabs[Random.Range(0, cm.prefabs.Length)], pos, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));
        go.name = "RopeGunner";
        go.transform.localScale = Vector3.one * cm.scale;
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = cm.locomotion; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
        var mpb = new MaterialPropertyBlock();
        Color tint = GameEra.Medieval ? medievalTint : modernTint;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.GetPropertyBlock(mpb); mpb.SetColor("_Color", tint); r.SetPropertyBlock(mpb); }
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        // 들고 있는 루프건 (몸 앞의 굵은 막대)
        var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(gun.GetComponent<Collider>());
        gun.name = "RopeGun";
        gun.transform.SetParent(go.transform, false);
        gun.transform.localPosition = new Vector3(0.25f, 1.25f, 0.45f);
        gun.transform.localScale = new Vector3(0.18f, 0.22f, 1.1f);
        var fx = CombatFX.Instance;
        if (fx) gun.GetComponent<MeshRenderer>().sharedMaterial = GameEra.Medieval && fx.javelinMaterial ? fx.javelinMaterial : fx.shellMaterial;
        var rg = go.AddComponent<RopeGunner>();
        rg.scale = cm.scale; rg.owner = this;
        alive.Add(rg);
    }

    // 도시 안이면 가장 가까운 도로(격자선) 위로 옮김
    public static Vector3 OnRoad(CityGenerator city, Vector3 p)
    {
        if (!city) return p;
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 o = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        if (p.x < o.x || p.z < o.z || p.x > o.x + city.blocksX * pitch || p.z > o.z + city.blocksZ * pitch) return p; // 도시 밖
        float sx = o.x + Mathf.Round((p.x - o.x) / pitch) * pitch, sz = o.z + Mathf.Round((p.z - o.z) / pitch) * pitch;
        if (Mathf.Abs(sx - p.x) < Mathf.Abs(sz - p.z)) p.x = sx; else p.z = sz;
        return p;
    }
}
