using UnityEngine;

// 노란 건물(발전소): 거인이 부수면(빨간 건물 폭발에 휘말린 것 포함, 군대 폭탄은 제외)
// 스테미나가 즉시 가득 차고 일정 시간 무한 달리기. 배치/등장은 SpecialBuildingSet 참고.
public class BoostBuildings : SpecialBuildingSet
{
    public static BoostBuildings Instance { get; private set; }
    public override string DisplayName => "노란 건물";

    [Header("효과")]
    [Tooltip("무한 달리기 지속 시간(초)")] public float infiniteRunSeconds = 5f;

    protected override void Awake() { Instance = this; base.Awake(); }
    void Reset() { color = new Color(1f, 0.85f, 0.1f); } // 컴포넌트를 새로 붙일 때 기본 색

    public static bool IsBoost(GameObject building) => Instance && Instance.Contains(building);

    // BuildingDestruction이 건물을 부술 때 호출. byGiant = 거인이 부순 것 (점수가 들어가는 파괴)
    public static void OnBroken(GameObject building, bool byGiant)
    {
        if (!Instance || !Instance.Take(building) || !byGiant) return;
        var st = FindObjectOfType<GiantStamina>();
        if (st) st.Boost(Instance.infiniteRunSeconds);
    }
}
