using UnityEngine;

// 투석기·트레뷰셋 팔: TankAI가 한 발 쏠 때마다(Fired) 팔을 앞으로 휘둘렀다가 천천히 다시 당겨 놓음.
// 팔 회전축(arm)의 로컬 X축으로 돎: restAngle = 쏘기 전(뒤로 젖힘), throwAngle = 다 던진 자세.
public class CatapultArm : MonoBehaviour
{
    public Transform arm;
    public float restAngle = -75f;
    public float throwAngle = 13f;
    [Tooltip("휘두르는 시간(초)")] public float throwTime = 0.2f;
    [Tooltip("다시 당겨 놓는 시간(초)")] public float reloadTime = 1.8f;
    float t = 99f;
    TankAI ai;

    void Awake()
    {
        ai = GetComponent<TankAI>();
        if (ai) ai.Fired += OnFired;
        Apply(restAngle);
    }

    void OnDestroy() { if (ai) ai.Fired -= OnFired; }
    void OnFired() { t = 0f; }
    public void Swing() { t = 0f; } // TankAI 없이 쓰는 투석기(성벽 위 WallGuard)가 직접 부름

    void Update()
    {
        if (!arm || (ai && ai.IsDead)) return;
        t += Time.deltaTime;
        float a = t < throwTime ? Mathf.Lerp(restAngle, throwAngle, t / throwTime)
                                : Mathf.Lerp(throwAngle, restAngle, Mathf.SmoothStep(0f, 1f, (t - throwTime) / reloadTime));
        Apply(a);
    }

    void Apply(float a) { if (arm) arm.localRotation = Quaternion.Euler(a, 0f, 0f); }
}
