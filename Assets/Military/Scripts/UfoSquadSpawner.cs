using System.Collections.Generic;
using UnityEngine;

// 공격 UFO 편대 (후반, 현대·중세 공용): 거인이 fromLevel 이상이면 먼 하늘에서 작은 UFO가 날아와 거인 주위를 돌며 광선을 쏨 (UfoFighter).
// 레벨이 오를수록 수가 늘어남. 모델은 큰 축소 UFO(UfoEnemy)의 모델을 같이 씀.
public class UfoSquadSpawner : MonoBehaviour
{
    [Tooltip("UFO 모델 (비우면 씬의 UfoEnemy 모델)")] public GameObject ufoPrefab;

    [Header("등장")]
    [Tooltip("거인이 이 레벨 이상일 때부터 등장")] public int fromLevel = 15;
    public int baseCount = 2;
    [Tooltip("이 레벨마다 한 대씩 늘어남")] public int levelsPerExtra = 5;
    public int maxCount = 5;
    [Tooltip("한 대 내보내고 다음까지 (초)")] public float spawnInterval = 8f;

    [Header("UFO")]
    [Tooltip("UFO 폭 = 거인 키 × 값")] public float sizePerHeight = 0.32f;
    [Tooltip("거인 주위를 도는 속도 (도/초)")] public float orbitSpeed = 35f;
    [Tooltip("최대 이동 속도 = 거인 키 × 값 (m/s)")] public float maxSpeedPerHeight = 1.6f;
    [Tooltip("쏜 뒤 그 자리에 떠 있는 시간 (초): 이때 잡기 쉬움")] public float hoverAfterFire = 1.5f;
    [Tooltip("빛을 모으는 시간 (초): 이동이 느려지고 밑이 초록으로 빛남")] public float chargeTime = 0.9f;
    public float fireInterval = 3.5f;
    [Range(0f, 1f)] public float accuracy = 0.5f;
    public float beamDamage = 8f;
    [Tooltip("체력 (방귀 가스 등 지속 피해용, 손으로 치면 바로 떨어짐)")] public float hp = 60f;
    public int points = 1000;

    readonly List<UfoFighter> alive = new List<UfoFighter>();
    float nextSpawn;
    bool wasSafe;

    void Start()
    {
        nextSpawn = Time.time + 5f;
        if (!ufoPrefab) { var big = FindObjectOfType<UfoEnemy>(); if (big) ufoPrefab = big.ufoPrefab; }
    }

    void Update()
    {
        alive.RemoveAll(a => !a || a.IsDead);
        var g = GiantHealth.Instance;
        var lv = LevelSystem.Instance;
        if (!g || g.IsDead || GameStartMenu.InMenu || EnemyFreeze.Active || !lv || lv.Level < fromLevel || !ufoPrefab) return;
        // 거인의 집 안 / 인질 구출 후퇴: 모두 떠나고 새로 안 옴
        if (GiantHome.GiantSafe || MilitarySpawner.RetreatLeft > 0f)
        {
            if (!wasSafe) { foreach (var a in alive) a.Leave(); alive.Clear(); }
            wasSafe = true;
            return;
        }
        wasSafe = false;
        int want = Mathf.Min(maxCount, baseCount + (levelsPerExtra > 0 ? (lv.Level - fromLevel) / levelsPerExtra : 0));
        if (alive.Count >= want || Time.time < nextSpawn) return;
        nextSpawn = Time.time + spawnInterval;
        Spawn(g);
    }

    void Spawn(GiantHealth g)
    {
        float h = Mathf.Max(2f, g.Height);
        float a = Random.Range(0f, Mathf.PI * 2f);
        Vector3 pos = g.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * h * 10f + Vector3.up * h * 4f; // 먼 하늘에서
        var root = new GameObject("UfoFighter").transform;
        root.SetParent(transform, false);
        root.position = pos;
        var model = Instantiate(ufoPrefab, root).transform;
        model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        float width = 10f;
        var rs = model.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            width = Mathf.Max(0.5f, Mathf.Max(b.size.x, b.size.z));
            model.localPosition = -root.InverseTransformPoint(b.center); // 모델 가운데를 원점에
        }
        root.localScale = Vector3.one * (h * sizePerHeight / width);
        var f = root.gameObject.AddComponent<UfoFighter>();
        f.owner = this; f.model = model; f.baseWidth = width;
        alive.Add(f);
    }
}
