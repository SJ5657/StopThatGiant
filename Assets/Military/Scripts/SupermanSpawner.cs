using UnityEngine;

// 슈퍼맨 등장: 거인이 appearLevel 레벨이 되면 firstDelay초 뒤 멀리서 날아옴. 한 번에 한 명.
// 마지막 돌진까지 모두 피해서 슈퍼맨이 날아가 버리면 respawnDelay초 뒤 다시 등장.
public class SupermanSpawner : MonoBehaviour
{
    static SupermanSpawner instance;

    [Tooltip("슈퍼맨 모델 (Assets/Models/SuperMan.vrm)")] public GameObject supermanPrefab;
    [Tooltip("떠 있을 때 애니메이션 (허리에 손 얹은 자세)")] public RuntimeAnimatorController animator;
    [Tooltip("거인이 이 레벨이 되면 처음 등장")] public int appearLevel = 20;
    [Tooltip("등장 레벨이 된 뒤 실제로 나타나기까지(초)")] public float firstDelay = 3f;
    [Tooltip("날아가 버린 뒤 다시 등장까지(초)")] public float respawnDelay = 180f;
    [Tooltip("등장 위치: 거인에게서 이 거리(거인 키 배수)만큼 떨어진 하늘")] public float spawnDistanceRatio = 10f;

    float nextAt = -1f;

    void Awake() { instance = this; }

    void Update()
    {
        if (GameStartMenu.InMenu || !supermanPrefab) return;
        var g = GiantHealth.Instance;
        if (!g || g.IsDead || GiantShrink.Active) return;
        if (nextAt < 0f)
        {
            if (!LevelSystem.Instance || LevelSystem.Instance.Level < appearLevel) return; // 후반(레벨 appearLevel)부터 등장
            nextAt = Time.time + firstDelay;
        }
        if (SupermanAI.Active || Time.time < nextAt || GiantHome.GiantSafe) return;
        Spawn(g);
    }

    void Spawn(GiantHealth g)
    {
        float H = Mathf.Max(5f, g.Height);
        float a = Random.Range(0f, Mathf.PI * 2f);
        Vector3 pos = g.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * H * spawnDistanceRatio + Vector3.up * H * 2.5f;
        var go = Instantiate(supermanPrefab, pos, Quaternion.identity);
        go.name = "Superman";
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = animator; anim.applyRootMotion = false; }
        go.AddComponent<SupermanAI>().Init(H);
        nextAt = float.MaxValue; // 날아가 버릴 때 다시 정함
    }

    public static void OnSupermanDied() { if (instance) instance.nextAt = Time.time + instance.respawnDelay; }
}
