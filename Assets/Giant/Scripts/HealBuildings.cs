using UnityEngine;

// 초록 건물: 거인이 부수면(빨간 건물 폭발에 휘말린 것 포함, 군대 폭탄은 제외)
// 그 자리에 초록 회복 영역(HealZone)이 생김. 배치/등장은 SpecialBuildingSet 참고.
public class HealBuildings : SpecialBuildingSet
{
    public static HealBuildings Instance { get; private set; }
    public override string DisplayName => "초록 건물";

    [Header("회복 영역")]
    public float zoneRadius = 60f;
    [Tooltip("영역 유지 시간(초)")] public float zoneDuration = 8f;
    [Tooltip("영역 안에 있을 때 초당 HP 회복량")] public float healPerSecond = 60f;

    protected override void Awake() { Instance = this; base.Awake(); }
    void Reset() { color = new Color(0.15f, 0.8f, 0.3f); } // 컴포넌트를 새로 붙일 때 기본 색

    public static bool IsHeal(GameObject building) => Instance && Instance.Contains(building);

    // BuildingDestruction이 건물을 부술 때 호출. byGiant = 거인이 부순 것 (점수가 들어가는 파괴)
    public static void OnBroken(GameObject building, bool byGiant)
    {
        if (!Instance || !Instance.Take(building) || !byGiant) return;
        HealZone.Create(building.transform.position, Instance.zoneRadius, Instance.zoneDuration, Instance.healPerSecond);
    }
}
