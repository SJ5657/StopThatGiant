using System.Collections.Generic;
using UnityEngine;

// 마법사 내보내기 (중세): 거인이 fromLevel 이상이면 화면 밖에서 마법사가 걸어 들어옴. 레벨이 오를수록 수가 늘어남.
// 마법사는 세 종류, 로브 색으로 구분: 비전(보라, 따라오는 마법 구슬) / 얼음(하늘색, 맞으면 잠시 느려짐) / 폭풍(남색, 노란 원에 번개).
// 모델은 시민 모델에 로브 색 + 뾰족 모자 + 지팡이. 행동은 Wizard.
public class WizardSpawner : MonoBehaviour
{
    [Header("등장")]
    [Tooltip("거인이 이 레벨 이상일 때부터 등장")] public int fromLevel = 4;
    public int baseCount = 1;
    [Tooltip("이 레벨마다 한 명씩 늘어남")] public int levelsPerExtra = 4;
    public int maxCount = 4;
    [Tooltip("한 명 내보내고 다음까지 (초)")] public float spawnInterval = 14f;
    [Tooltip("거인에게서 이 거리 범위에 나타남 (거인 키 × 값)")] public Vector2 spawnDistancePerHeight = new Vector2(6f, 8f);
    public float despawnDistance = 600f;

    [Header("마법사")]
    public float runSpeed = 7f;
    [Tooltip("거인과 유지하는 거리 = 거인 키 × 값")] public float preferRangePerHeight = 4f;
    [Tooltip("이보다 가까우면 순간이동으로 달아남 = 거인 키 × 값")] public float minRangePerHeight = 1.8f;
    [Tooltip("사거리 = 거인 키 × 값")] public float castRangePerHeight = 7f;
    public float castInterval = 6f;
    [Tooltip("주문을 모으는 시간 (초): 지팡이 끝이 밝아짐")] public float castTime = 1f;
    [Tooltip("순간이동 뒤 다시 할 수 있기까지 (초)")] public float blinkCooldown = 6f;
    public int points = 150;

    [Header("비전 (보라)")]
    public float boltDamage = 14f;
    public float boltSpeed = 55f;
    [Tooltip("구슬이 거인 쪽으로 휘는 정도 (라디안/초)")] public float boltHoming = 1.2f;
    public Color arcaneColor = new Color(0.75f, 0.35f, 1f);
    public Color arcaneRobe = new Color(0.42f, 0.22f, 0.62f);

    [Header("얼음 (하늘색)")]
    public float frostDamage = 5f;
    [Tooltip("맞으면 속도 × 이 값")] [Range(0.1f, 1f)] public float frostSlow = 0.5f;
    public float frostTime = 4f;
    public Color frostColor = new Color(0.55f, 0.9f, 1f);
    public Color frostRobe = new Color(0.55f, 0.8f, 0.95f);

    [Header("폭풍 (남색)")]
    public float lightningDamage = 22f;
    [Tooltip("노란 원이 뜨고 번개가 떨어지기까지 (초)")] public float lightningDelay = 1.5f;
    [Tooltip("번개 원 반경 = 거인 키 × 값")] public float lightningRadiusPerHeight = 0.35f;
    public Color stormColor = new Color(1f, 0.9f, 0.4f);
    public Color stormRobe = new Color(0.32f, 0.4f, 0.78f);

    readonly List<Wizard> alive = new List<Wizard>();
    float nextSpawn;
    CityGenerator city;
    static Mesh coneMesh;

    void Start() { city = FindObjectOfType<CityGenerator>(); nextSpawn = Time.time + 8f; }

    void Update()
    {
        alive.RemoveAll(a => !a || a.IsDead);
        var g = GiantHealth.Instance;
        var lv = LevelSystem.Instance;
        if (!g || g.IsDead || GameStartMenu.InMenu || GiantHome.GiantSafe || EnemyFreeze.Active || !lv || lv.Level < fromLevel) return;
        int want = Mathf.Min(maxCount, baseCount + (levelsPerExtra > 0 ? (lv.Level - fromLevel) / levelsPerExtra : 0));
        if (alive.Count >= want || Time.time < nextSpawn) return;
        nextSpawn = Time.time + spawnInterval;
        // 지금 없는 종류를 먼저
        var have = new HashSet<Wizard.School>();
        foreach (var a in alive) have.Add(a.school);
        var pool = new List<Wizard.School>();
        foreach (Wizard.School s in System.Enum.GetValues(typeof(Wizard.School))) if (!have.Contains(s)) pool.Add(s);
        if (pool.Count == 0) pool.AddRange((Wizard.School[])System.Enum.GetValues(typeof(Wizard.School)));
        Spawn(g, pool[Random.Range(0, pool.Count)]);
    }

    public Color SpellColor(Wizard.School s) => s == Wizard.School.Arcane ? arcaneColor : s == Wizard.School.Frost ? frostColor : stormColor;
    Color Robe(Wizard.School s) => s == Wizard.School.Arcane ? arcaneRobe : s == Wizard.School.Frost ? frostRobe : stormRobe;

    // 마법사가 서 있을 만한 곳 (거인에게서 키 × dist, 화면 밖 우선, 도시 안이면 도로 위)
    public Vector3 PickSpot(Vector3 gp, float dist, bool preferHidden)
    {
        Vector3 pos = gp;
        for (int tries = 0; tries < 10; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            pos = RopeGunnerSpawner.OnRoad(city, gp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist);
            if (!preferHidden || MilitarySpawner.OutOfSight(pos)) break;
        }
        pos.y = WildTerrain.Active ? WildTerrain.Height(pos.x, pos.z) : 0f;
        return pos;
    }

    void Spawn(GiantHealth g, Wizard.School school)
    {
        var cm = CivilianManager.Instance ? CivilianManager.Instance : FindObjectOfType<CivilianManager>();
        if (!cm || cm.prefabs == null || cm.prefabs.Length == 0) return;
        float h = Mathf.Max(2f, (g.ShoulderY - g.transform.position.y) / 0.837f);
        Vector3 gp = g.transform.position;
        Vector3 pos = PickSpot(gp, h * Random.Range(spawnDistancePerHeight.x, spawnDistancePerHeight.y), true);
        Vector3 look = gp - pos; look.y = 0f;
        var go = Instantiate(cm.prefabs[Random.Range(0, cm.prefabs.Length)], pos, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));
        go.name = "Wizard_" + school;
        go.transform.localScale = Vector3.one * cm.scale;
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = cm.locomotion; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
        var mpb = new MaterialPropertyBlock();
        Color robe = Robe(school);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.GetPropertyBlock(mpb); mpb.SetColor("_Color", robe); r.SetPropertyBlock(mpb); }
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);

        float s = cm.scale;
        // 모델 키 (모델 기준 단위): 모자·지팡이 크기를 맞춤
        float bodyH = 1.8f;
        var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (skins.Length > 0) { Bounds bb = skins[0].bounds; foreach (var r in skins) bb.Encapsulate(r.bounds); bodyH = bb.size.y / Mathf.Max(0.01f, s); }
        Transform head = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        // 뾰족 모자 (로브보다 조금 어두운 색, 챙 + 원뿔). 머리뼈 위치는 머리 아래쪽이라 조금 올림
        Color hatCol = robe * 0.75f; hatCol.a = 1f;
        Vector3 headTop = head ? head.position + Vector3.up * 0.11f * bodyH * s : pos + Vector3.up * 0.97f * bodyH * s;
        var brim = Shape("HatBrim", Cone(), hatCol, headTop, new Vector3(0.25f, 0.015f, 0.25f) * bodyH * s);
        var hat = Shape("Hat", Cone(), hatCol, headTop, new Vector3(0.13f, 0.36f, 0.13f) * bodyH * s);
        hat.rotation = Quaternion.Euler(-12f, go.transform.eulerAngles.y, 0f); // 살짝 뒤로 꺾인 끝
        brim.SetParent(head ? head : go.transform, true);
        hat.SetParent(head ? head : go.transform, true);
        // 지팡이 + 끝의 빛나는 구슬: 몸 오른쪽 앞에 곧게 세움 (손뼈에 붙이면 걷는 동작에 따라 눕기 때문에 몸에 붙임)
        var staff = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(staff.GetComponent<Collider>());
        staff.name = "Staff";
        var fx = CombatFX.Instance;
        if (fx && fx.javelinMaterial) staff.GetComponent<MeshRenderer>().sharedMaterial = fx.javelinMaterial;
        float len = bodyH * 1.1f, side = bodyH * 0.2f, fwd = bodyH * 0.1f;
        staff.transform.SetParent(go.transform, false);
        staff.transform.localPosition = new Vector3(side, len * 0.5f, fwd);
        staff.transform.localScale = new Vector3(bodyH * 0.035f, len, bodyH * 0.035f);
        var orb = new GameObject("StaffOrb").transform; // 빛은 Wizard가 MagicFX 입자로 그림
        orb.SetParent(go.transform, false);
        orb.localPosition = new Vector3(side, len + bodyH * 0.04f, fwd);

        var w = go.AddComponent<Wizard>();
        w.school = school; w.scale = s; w.owner = this; w.orb = orb;
        alive.Add(w);
    }

    // 모양 하나. 색은 꼭짓점 색(빛 방향 음영) × _Color (Sprites/Default 재질 = WarningZone.material)
    static Transform Shape(string name, Mesh mesh, Color col, Vector3 pos, Vector3 size)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = WarningZone.material;
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", col); mr.SetPropertyBlock(mpb);
        go.transform.position = pos;
        go.transform.localScale = size;
        return go.transform;
    }

    // 밑면 반지름 1, 높이 1인 원뿔. 면마다 밝기를 꼭짓점 색에 구워 넣음 (조명 없는 재질에서도 입체감)
    static Mesh Cone()
    {
        if (coneMesh) return coneMesh;
        const int N = 12;
        var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
        Vector3 light = new Vector3(0.4f, 0.8f, -0.45f).normalized;
        for (int i = 0; i < N; i++)
        {
            float a0 = i * Mathf.PI * 2f / N, a1 = (i + 1) * Mathf.PI * 2f / N;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)), top = Vector3.up;
            Vector3 n = Vector3.Cross(top - p0, p1 - p0).normalized;
            if (Vector3.Dot(n, (p0 + p1) * 0.5f) < 0f) n = -n;
            float b = 0.55f + 0.45f * Mathf.Clamp01(Vector3.Dot(n, light));
            int k = v.Count;
            v.Add(p0); v.Add(top); v.Add(p1);
            for (int j = 0; j < 3; j++) c.Add(new Color(b, b, b, 1f));
            t.Add(k); t.Add(k + 1); t.Add(k + 2);
            // 밑면
            k = v.Count;
            v.Add(Vector3.zero); v.Add(p0); v.Add(p1);
            for (int j = 0; j < 3; j++) c.Add(new Color(0.45f, 0.45f, 0.45f, 1f));
            t.Add(k); t.Add(k + 1); t.Add(k + 2);
        }
        coneMesh = new Mesh { name = "WizardHat" };
        coneMesh.SetVertices(v); coneMesh.SetColors(c); coneMesh.SetTriangles(t, 0);
        coneMesh.RecalculateNormals(); coneMesh.RecalculateBounds();
        return coneMesh;
    }
}
