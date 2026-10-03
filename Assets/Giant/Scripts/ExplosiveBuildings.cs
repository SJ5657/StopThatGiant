using System.Collections;
using UnityEngine;

// 폭발 건물(빨강): 무너지면(거인·폭탄 무엇이든) 잠깐 뒤 폭발해서 반경 안 건물을 모두 부숨.
// 폭발에 휘말린 다른 빨간 건물도 연쇄 폭발. 배치/등장은 SpecialBuildingSet 참고.
public class ExplosiveBuildings : SpecialBuildingSet
{
    public static ExplosiveBuildings Instance { get; private set; }
    public override string DisplayName => "빨간 건물";

    [Header("폭발")]
    public float blastRadius = 45f;
    public float explosionSize = 35f;
    [Tooltip("무너진 뒤 폭발까지 걸리는 시간 (연쇄 폭발 간격)")] public float delay = 0.2f;
    [Tooltip("폭발로 부서지는 건물의 파편 양 배율")] [Range(0, 1)] public float shardMultiplier = 0.35f;

    static readonly Collider[] hits = new Collider[256];

    protected override void Awake() { Instance = this; base.Awake(); }
    void Reset() { color = new Color(0.9f, 0.13f, 0.1f); } // 컴포넌트를 새로 붙일 때 기본 색

    public static bool IsExplosive(GameObject building) => Instance && Instance.Contains(building);

    // BuildingDestruction이 건물을 부술 때 호출
    public static void OnBroken(GameObject building)
    {
        if (!Instance || !Instance.Take(building)) return;
        Instance.StartCoroutine(Instance.Detonate(building.transform.position, building.transform.lossyScale));
    }

    IEnumerator Detonate(Vector3 center, Vector3 size)
    {
        yield return new WaitForSeconds(delay);
        Vector3 ground = new Vector3(center.x, center.y - size.y * 0.5f, center.z);

        CombatFX.Explosion(ground + Vector3.up * explosionSize * 0.25f, explosionSize);
        CombatFX.Explosion(ground + Vector3.up * explosionSize * 0.8f, explosionSize * 0.7f);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            CombatFX.Explosion(ground + new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * blastRadius * 0.45f, explosionSize * 0.5f);
        }
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.7f);

        int n = Physics.OverlapSphereNonAlloc(ground, blastRadius, hits, LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (hits[i]) BuildingDestruction.Break(hits[i].gameObject, ground, Vector3.zero, true, shardMultiplier);
    }
}
