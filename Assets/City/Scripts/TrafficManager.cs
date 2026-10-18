using System.Collections.Generic;
using UnityEngine;

// 도시 도로에 자동차(TrafficCar)를 count대 달리게 함. 차는 거인 주변(spawnRadius 안)에 모이게 하고,
// 거인에게서 despawnDistance보다 멀어지거나 밟혀서 사라지면 거인 주변 도로에 다시 채움.
public class TrafficManager : MonoBehaviour
{
    [Tooltip("달릴 자동차 모델들 (무작위로 고름)")] public GameObject[] carPrefabs;
    public CityGenerator city;
    [Tooltip("거인 주변에 유지할 자동차 수")] public int count = 160;
    [Tooltip("자동차 모델 크기 배율 (원래 모델은 거인 도시에 비해 작음)")] public float carScale = 1.6f;
    public Vector2 speedRange = new Vector2(18f, 30f);
    [Tooltip("거인에게서 이 거리(m) 안의 도로에 차를 생성")] public float spawnRadius = 1100f;
    [Tooltip("다시 채울 때 거인에게서 최소 이 거리(m) 떨어진 곳에 생성 (눈앞에서 갑자기 생기지 않게)")] public float respawnMinDistance = 350f;
    [Tooltip("거인에게서 이 거리(m)보다 멀어진 차는 치우고 거인 주변에 다시 채움")] public float despawnDistance = 1500f;
    public float respawnInterval = 0.15f;

    readonly List<TrafficCar> cars = new List<TrafficCar>();
    float nextRespawn, nextFarCheck;

    void Start()
    {
        if (!city) city = FindObjectOfType<CityGenerator>();
        if (!city || carPrefabs == null || carPrefabs.Length == 0) return;
        for (int i = 0; i < count; i++) Spawn(0f);
    }

    void Update()
    {
        if (!city || carPrefabs == null || carPrefabs.Length == 0) return;
        cars.RemoveAll(c => !c || c.IsDead);

        // 거인에게서 너무 멀어진 차는 치움 (그 수만큼 아래에서 거인 주변에 다시 채워짐)
        var giant = GiantHealth.Instance;
        if (giant && Time.time >= nextFarCheck)
        {
            nextFarCheck = Time.time + 1f;
            Vector3 gp = giant.transform.position; gp.y = 0f;
            for (int k = cars.Count - 1; k >= 0; k--)
            {
                Vector3 p = cars[k].transform.position; p.y = 0f;
                if (Vector3.Distance(p, gp) > despawnDistance) { Destroy(cars[k].gameObject); cars.RemoveAt(k); }
            }
        }

        if (cars.Count >= count || Time.time < nextRespawn) return;
        nextRespawn = Time.time + respawnInterval;
        Spawn(respawnMinDistance);
    }

    // 거인 주변(minDistance ~ spawnRadius)의 도로 교차로에 자동차를 세움
    void Spawn(float minDistance)
    {
        var giant = GiantHealth.Instance;
        Vector3 gp = giant ? giant.transform.position : Vector3.zero; gp.y = 0f;
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 o = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        int i = 0, j = 0;
        for (int tries = 0; tries < 30; tries++)
        {
            if (giant)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Random.value) * spawnRadius; // 면적에 고르게 퍼지도록
                Vector3 cand = gp + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                i = Mathf.Clamp(Mathf.RoundToInt((cand.x - o.x) / pitch), 0, city.blocksX);
                j = Mathf.Clamp(Mathf.RoundToInt((cand.z - o.z) / pitch), 0, city.blocksZ);
            }
            else { i = Random.Range(0, city.blocksX + 1); j = Random.Range(0, city.blocksZ + 1); }
            Vector3 p = new Vector3(o.x + i * pitch, 0f, o.z + j * pitch);
            float dist = Vector3.Distance(p, gp);
            if (dist >= minDistance && dist <= spawnRadius * 1.15f) break;
        }

        var go = Instantiate(carPrefabs[Random.Range(0, carPrefabs.Length)], transform);
        go.name = "TrafficCar";
        go.transform.localScale = go.transform.localScale * carScale;
        int layer = LayerMask.NameToLayer("Military"); // 거인 발에 밟히도록
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        // 충돌 박스: 회전이 없는 상태(생성 직후)에서 모델 크기로 재서 붙임. 이후 자동차가 돌아도 박스는 같이 돎
        go.transform.rotation = Quaternion.identity;
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            Vector3 ls = go.transform.lossyScale;
            box.size = new Vector3(b.size.x / Mathf.Max(0.01f, ls.x), b.size.y / Mathf.Max(0.01f, ls.y), b.size.z / Mathf.Max(0.01f, ls.z));
        }
        var car = go.AddComponent<TrafficCar>();
        car.speed = Random.Range(speedRange.x, speedRange.y);
        car.Init(city, i, j);
        Physics.SyncTransforms(); // 충돌 박스를 만든 자리(원점)에 남아 있지 않게: 거인 발과 겹쳐 생성 직후 밟히는 것을 막음
        cars.Add(car);
    }
}
