using UnityEngine;

// 중세 버전 씬 표시: 이 컴포넌트가 있는 씬(Medieval.unity)에서는 GameEra.Medieval이 켜짐.
// 공용 스크립트들은 이 값을 보고 모양만 중세풍으로 바꿈 (포탄 → 돌덩이, 총알 궤적 → 화살 등).
// 다른 스크립트의 Awake·Start보다 먼저 켜지도록 실행 순서를 가장 앞으로.
// 맵을 바꾸거나 다시 시작하며 씬을 불러올 때 새 씬의 Awake와 옛 씬의 OnDestroy 순서가 섞여도 맞게 개수로 셈.
[DefaultExecutionOrder(-1000)]
public class MedievalTheme : MonoBehaviour
{
    static int alive;
    void Awake() { alive++; GameEra.Medieval = true; }
    void OnDestroy() { alive = Mathf.Max(0, alive - 1); GameEra.Medieval = alive > 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCount() { alive = 0; }
}

public static class GameEra
{
    public static bool Medieval;

    // 도메인 리로드를 끈 채로 플레이해도 매번 꺼진 상태에서 시작 (중세 씬이면 MedievalTheme가 다시 켬)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() { Medieval = false; }
}
