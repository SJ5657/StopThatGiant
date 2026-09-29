using System.Collections.Generic;
using UnityEngine;

// 거인이 부순 건물 수에 따라 군대(탱크/헬기)를 점점 더 많이 투입.
// 처음엔 등장하지 않다가 firstTankAt 채를 부수면 탱크 1대 → 부술수록 증원. 파괴된 유닛도 목표 수량까지 보충.
public class MilitarySpawner : MonoBehaviour
{
    public GameObject tankPrefab;
    public GameObject[] heliPrefabs;
    public GameObject[] jetPrefabs;
    public CityGenerator city;

    [Header("탱크 등장 조건")]
    public int firstTankAt = 20;          // 이만큼 부수면 첫 탱크
    public int buildingsPerExtraTank = 40; // 이만큼 더 부술 때마다 +1
    public int maxTanks = 10;

    [Header("헬기 등장 조건")]
    public int firstHeliAt = 60;
    public int buildingsPerExtraHeli = 60;
    public int maxHelis = 6;

    [Header("전투기 등장 조건")]
    public int firstJetAt = 100;
    public int buildingsPerExtraJet = 80;
    public int maxJets = 4;

    [Header("스폰")]
    public float tankSpawnDistance = 420f;
    public float heliSpawnDistance = 500f;
    public float spawnInterval = 1.5f;     // 한 대씩 순차 투입 간격

    readonly List<TankAI> tanks = new List<TankAI>();
    readonly List<HeliAI> helis = new List<HeliAI>();
    readonly List<JetAI> jets = new List<JetAI>();
    int lastWantedJ;
    public float jetSpawnDistance = 1300f;
    float nextSpawn;
    int lastWantedT, lastWantedH;
    string banner; float bannerUntil;

    public static MilitarySpawner Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        if (!city) city = FindObjectOfType<CityGenerator>();
        int g = LayerMask.NameToLayer("Giant"), m = LayerMask.NameToLayer("Military");
        if (g >= 0 && m >= 0) Physics.IgnoreLayerCollision(g, m, true);
    }

    int Destroyed => BuildingDestruction.Instance ? BuildingDestruction.Instance.DestroyedCount : 0;
    public int WantedTanks => Destroyed < firstTankAt ? 0 : Mathf.Min(maxTanks, 1 + (Destroyed - firstTankAt) / buildingsPerExtraTank);
    public int WantedHelis => Destroyed < firstHeliAt ? 0 : Mathf.Min(maxHelis, 1 + (Destroyed - firstHeliAt) / buildingsPerExtraHeli);
    public int WantedJets => Destroyed < firstJetAt ? 0 : Mathf.Min(maxJets, 1 + (Destroyed - firstJetAt) / buildingsPerExtraJet);

    void Update()
    {
        var g = GiantHealth.Instance;
        if (!g || g.IsDead) return;
        tanks.RemoveAll(t => !t || t.IsDead);
        helis.RemoveAll(h => !h || h.IsDead);
        jets.RemoveAll(j => !j || j.IsDead);
        int wj = WantedJets;
        if (wj > lastWantedJ) lastWantedJ = wj;

        int wt = WantedTanks, wh = WantedHelis;
        if (wt > lastWantedT || wh > lastWantedH)
        {
            lastWantedT = wt; lastWantedH = wh;
        }

        if (Time.time < nextSpawn) return;
        if (tanks.Count < wt) { SpawnTank(g); nextSpawn = Time.time + spawnInterval; }
        else if (helis.Count < wh) { SpawnHeli(g); nextSpawn = Time.time + spawnInterval; }
        else if (jets.Count < wj) { SpawnJet(g); nextSpawn = Time.time + spawnInterval * 2f; }
    }

    [Tooltip("화면 중앙 경고 문구(폭격 경고 등) 표시 여부")] public bool showBanners = false;
    public void ShowBanner(string text) { if (!showBanners) return; banner = text; bannerUntil = Time.time + 3f; }

    void SpawnTank(GiantHealth g)
    {
        if (!tankPrefab) return;
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 o = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;

        // 거인에게서 일정 거리 떨어진 도로 교차로
        Vector3 pos = g.transform.position;
        for (int tries = 0; tries < 12; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 cand = g.transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * tankSpawnDistance;
            int i = Mathf.Clamp(Mathf.RoundToInt((cand.x - o.x) / pitch), 0, city.blocksX);
            int j = Mathf.Clamp(Mathf.RoundToInt((cand.z - o.z) / pitch), 0, city.blocksZ);
            pos = new Vector3(o.x + i * pitch, 0, o.z + j * pitch);
            Vector3 d = pos - g.transform.position; d.y = 0;
            if (d.magnitude > tankSpawnDistance * 0.6f) break;
        }
        Vector3 look = g.transform.position - pos; look.y = 0;
        var go = Instantiate(tankPrefab, pos, Quaternion.LookRotation(look.sqrMagnitude > 1 ? look : Vector3.forward), transform);
        go.name = "Tank";
        var ai = go.GetComponent<TankAI>();
        ai.city = city;
        tanks.Add(ai);
    }

    void SpawnHeli(GiantHealth g)
    {
        if (heliPrefabs == null || heliPrefabs.Length == 0) return;
        float a = Random.Range(0f, 360f);
        Vector3 dir = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad));
        Vector3 pos = g.transform.position + dir * heliSpawnDistance + Vector3.up * g.ShoulderY * 0.9f;
        var go = Instantiate(heliPrefabs[Random.Range(0, heliPrefabs.Length)], pos, Quaternion.LookRotation(-dir), transform);
        go.name = "Helicopter";
        var ai = go.GetComponent<HeliAI>();
        ai.startAngle = a;
        ai.direction = Random.value < 0.5f ? 1f : -1f;
        ai.orbitRadius = Random.Range(130f, 200f);
        ai.altitude = g.ShoulderY * Random.Range(0.65f, 1f); // 어깨 높이 이하
        ai.angularSpeed = Random.Range(9f, 15f);
        helis.Add(ai);
    }

    void SpawnJet(GiantHealth g)
    {
        if (jetPrefabs == null || jetPrefabs.Length == 0) return;
        float a = Random.Range(0f, Mathf.PI * 2f);
        Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        Vector3 pos = g.transform.position + dir * jetSpawnDistance + Vector3.up * (g.ShoulderY + 60f);
        var go = Instantiate(jetPrefabs[Random.Range(0, jetPrefabs.Length)], pos, Quaternion.LookRotation(-dir), transform);
        go.name = "Jet";
        jets.Add(go.GetComponent<JetAI>());
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return; // 시작 메뉴 중에는 HUD 숨김
        if (Time.time < bannerUntil && !string.IsNullOrEmpty(banner))
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float blink = Mathf.PingPong(Time.time * 3f, 1f);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(2, Screen.height * 0.22f + 2, Screen.width, 50), banner, big);
            GUI.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.3f), blink);
            GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 50), banner, big);
            GUI.color = Color.white;
        }
    }
}
