using UnityEngine;

// 드래곤 불길: JetAI처럼 멀리서 날아와 거인 머리 위를 지나가는데, 지나가며 거인이 앞쪽 원뿔 안에 들어오면 불을 뿜음.
// 불길에 닿아 있는 동안 조금씩 피해 (tick마다). 거인이 드래곤 몸에 닿으면 JetAI처럼 격추됨.
public class DragonBreath : MonoBehaviour
{
    public Transform mouth;
    [Tooltip("불길이 닿는 거리 (m)")] public float range = 140f;
    [Tooltip("불을 뿜기 시작하는 원뿔 각도 (드래곤 앞쪽 기준)")] public float coneAngle = 40f;
    [Tooltip("불길 속에 있을 때 초당 피해")] public float dps = 30f;
    public float breathTime = 2.4f;
    public float cooldown = 3f;
    JetAI jet;
    float breathUntil, nextAt, tickAt;

    void Awake() { jet = GetComponent<JetAI>(); }

    void Update()
    {
        if (!mouth || (jet && jet.IsDead) || EnemyFreeze.Active || GameStartMenu.InMenu) return;
        var g = GiantHealth.Instance;
        if (!g || g.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden) return;
        Vector3 m = mouth.position, aim = g.GetAimPoint(), to = aim - m;
        float d = to.magnitude;
        bool inCone = d < range && Vector3.Angle(transform.forward, to) < coneAngle;
        if (Time.time >= nextAt && inCone) { breathUntil = Time.time + breathTime; nextAt = breathUntil + cooldown; }
        if (Time.time >= breathUntil) return;
        // 고개를 거인 쪽으로 반쯤 돌려 뿜음
        Vector3 dir = Vector3.Slerp(transform.forward, to.normalized, 0.65f).normalized;
        CombatFX.FireBreath(m, dir, range);
        bool burning = d < range * 0.85f && Vector3.Angle(dir, to) < 18f; // 불꽃이 실제로 닿는 거리 안에서만 피해
        if (burning && Time.time >= tickAt)
        {
            tickAt = Time.time + 0.25f;
            g.TakeDamage(dps * 0.25f, false);
        }
    }
}
