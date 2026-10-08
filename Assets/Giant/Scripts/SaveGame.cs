using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// 세이브: 거인의 집 상점에서 '저장하기' → 시작 화면 '이어하기'로 이어서 플레이.
// 저장 내용: 성별·모드, 레벨·경험치·점수·돈, HP, 능력치 강화(LevelUpCards), 패시브 아이템(PassiveItems),
// 부서진 건물, 공장 위치. 군대는 저장하지 않고 부순 건물 수에 맞춰 다시 몰려옴. 거인은 집 마당에서 시작.
// 건물은 도시(City/Blocks) 계층 순서로 번호를 매겨 저장 (도시가 씬에 구워져 있어 매번 같은 순서).
// 도시를 다시 생성해서 건물 수가 달라졌으면 건물 상태는 건너뛰고 나머지만 불러옴.
public static class SaveGame
{
    [System.Serializable]
    public class Data
    {
        public int version = 1;
        public string savedAt;
        public int gender;
        public bool infinite;
        public int level, xp, score, money, destroyedCount;
        public float hp;
        public LevelUpCards.State stats;
        public PassiveItems.State items;
        public int buildingCount;                          // 도시가 바뀌었는지 확인용
        public List<int> gone = new List<int>();           // 부서졌거나 숨겨진 건물
        public List<int> factories = new List<int>();      // 공장
        public List<int> factoryRolled = new List<int>();  // 공장 등장 확률을 이미 굴린 건물
    }

    static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");
    public static bool Exists => File.Exists(FilePath);

    static Data pending;                 // 이어하기로 불러올 데이터 (씬을 다시 불러온 뒤 적용)
    static List<GameObject> buildings;   // 씬을 불러온 직후의 건물 목록 (번호 = 목록 순서)

    // 불러오는 중이면 특수 건물의 무작위 배치를 건너뜀 (저장된 공장 자리로 되돌리므로)
    public static bool Loading => pending != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        pending = null; buildings = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 씬의 Awake 뒤·Start 전에 호출: 아무것도 부서지기 전에 건물 번호를 매김
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        buildings = CollectBuildings();
        if (pending != null) new GameObject("SaveGameLoader").AddComponent<Loader>();
    }

    static List<GameObject> CollectBuildings()
    {
        var list = new List<GameObject>();
        var city = Object.FindObjectOfType<CityGenerator>();
        var root = city ? city.transform.Find("Blocks") : null;
        if (!root) return list;
        int layer = LayerMask.NameToLayer("Building");
        foreach (Transform block in root)
            foreach (Transform b in block)
                if (b.gameObject.layer == layer) list.Add(b.gameObject);
        return list;
    }

    // 시작 화면 버튼에 보여줄 요약 (없거나 읽을 수 없으면 null)
    public static Data Peek()
    {
        if (!Exists) return null;
        try { return JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)); }
        catch (System.Exception e) { Debug.LogWarning($"[SaveGame] 세이브 파일을 읽을 수 없음: {e.Message}"); return null; }
    }

    // ───────────── 저장 ─────────────
    public static bool Save()
    {
        var d = new Data
        {
            savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            gender = GameStartMenu.CurrentChoice,
            infinite = GameStartMenu.InfiniteMode,
        };
        if (LevelSystem.Instance) { d.level = LevelSystem.Instance.Level; d.xp = LevelSystem.Instance.Xp; }
        if (ScoreManager.Instance) d.score = ScoreManager.Instance.Score;
        if (Money.Instance) d.money = Money.Instance.Amount;
        if (BuildingDestruction.Instance) d.destroyedCount = BuildingDestruction.Instance.DestroyedCount;
        if (GiantHealth.Instance) d.hp = GiantHealth.Instance.HP;
        var cards = Object.FindObjectOfType<LevelUpCards>();
        if (cards) d.stats = cards.Capture();
        if (PassiveItems.Instance) d.items = PassiveItems.Instance.Capture();

        if (buildings != null)
        {
            d.buildingCount = buildings.Count;
            var index = new Dictionary<GameObject, int>(buildings.Count);
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (!b || !b.activeSelf) d.gone.Add(i);
                else index[b] = i;
            }
            var ex = ExplosiveBuildings.Instance;
            if (ex)
            {
                foreach (var b in ex.Members) if (b && index.TryGetValue(b, out int i)) d.factories.Add(i);
                foreach (var b in ex.RolledBuildings) if (b && index.TryGetValue(b, out int i)) d.factoryRolled.Add(i);
            }
        }

        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(d));
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveGame] 저장 실패: {e.Message}");
            return false;
        }
    }

    // ───────────── 불러오기 ─────────────
    // 시작 화면 '이어하기': 저장된 성별·모드로 씬을 다시 불러온 뒤 Loader가 나머지를 적용
    public static bool Continue()
    {
        var d = Peek();
        if (d == null) return false;
        pending = d;
        GameStartMenu.RestartWith(d.gender, d.infinite);
        return true;
    }

    class Loader : MonoBehaviour
    {
        // 모든 Start(능력치 목록·아이템 목록·집 생성 등)가 끝난 다음 프레임에 적용
        IEnumerator Start()
        {
            yield return null;
            var d = pending;
            pending = null;
            if (d != null) Apply(d);
            Destroy(gameObject);
        }
    }

    static void Apply(Data d)
    {
        if (buildings != null && d.buildingCount == buildings.Count)
        {
            // 부서진 건물은 효과·점수 없이 치움 (먼저 치워야 공장이 커질 때 이미 없는 건물을 건드리지 않음)
            foreach (int i in d.gone)
            {
                var b = At(i);
                if (!b) continue;
                b.SetActive(false);
                Object.Destroy(b);
            }
            var ex = ExplosiveBuildings.Instance;
            if (ex) ex.Restore(Pick(d.factories), Pick(d.factoryRolled));
        }
        else if (d.buildingCount > 0)
            Debug.LogWarning($"[SaveGame] 도시가 바뀌어(건물 {d.buildingCount} → {buildings?.Count ?? 0}채) 부서진 건물은 불러오지 않음");

        if (BuildingDestruction.Instance) BuildingDestruction.Instance.RestoreCount(d.destroyedCount);
        if (ScoreManager.Instance) ScoreManager.Instance.Restore(d.score);
        if (LevelSystem.Instance) LevelSystem.Instance.Restore(d.level, d.xp);
        if (Money.Instance) Money.Instance.Restore(d.money);
        var cards = Object.FindObjectOfType<LevelUpCards>();
        if (cards && d.stats != null) cards.Restore(d.stats);
        if (PassiveItems.Instance && d.items != null) PassiveItems.Instance.Restore(d.items);
        if (GiantHealth.Instance && d.hp > 0f) GiantHealth.Instance.SetHP(d.hp); // 최대 HP를 되돌린 뒤에
    }

    static GameObject At(int i) => i >= 0 && i < buildings.Count ? buildings[i] : null;

    static List<GameObject> Pick(List<int> ids)
    {
        var list = new List<GameObject>(ids.Count);
        foreach (int i in ids) { var b = At(i); if (b) list.Add(b); }
        return list;
    }
}
