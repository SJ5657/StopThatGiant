using UnityEngine;

// 헬리콥터: 거인 주위를 일정 반경으로 선회하며 로켓 공격.
// 목표 지점이 항상 거인 기준 반경 위에 있으므로 거인이 다가오면 자연스럽게 물러나 거리를 유지.
public class HeliAI : MonoBehaviour
{
    public Transform[] mainRotors;
    public Transform[] tailRotors;
    public float rotorSpeed = 1400f;

    [Header("선회")]
    public float orbitRadius = 140f;
    public float altitude = 55f;
    public float bobAmount = 5f;
    [Tooltip("거인 어깨 높이보다 높이 날지 않음")] public bool capAtShoulder = true;
    public float minAltitude = 25f;
    public float angularSpeed = 12f;     // deg/s
    public float direction = 1f;         // 1 = 반시계, -1 = 시계
    public float startAngle = 0f;
    public float smoothTime = 2f;
    public float maxSpeed = 70f;         // 최대 이동 속도 (m/s)
    [Tooltip("거인에게 이보다 가까워지면 바깥으로 급히 벗어남")] public float panicRadius = 90f;

    [Header("사격")]
    public float fireInterval = 3.2f;
    public float rocketSpeed = 120f;
    public float damage = 40f;
    public float homing = 1.3f;

    [Header("충돌")]
    [Tooltip("거인 몸에 이 반경 안으로 닿으면 폭발")] public float contactRadius = 10f;

    public bool IsDead { get; private set; }
    float angle, nextFire;
    Vector3 vel, fallSpin;

    void Start()
    {
        angle = startAngle;
        nextFire = Time.time + Random.Range(1.5f, fireInterval);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var r in mainRotors) if (r) r.Rotate(transform.up, rotorSpeed * dt, Space.World);
        foreach (var r in tailRotors) if (r) r.Rotate(transform.right, rotorSpeed * 1.5f * dt, Space.World);

        if (IsDead) { Fall(dt); return; }

        var g = GiantHealth.Instance;
        if (!g) return;
        Vector3 c = g.transform.position;

        // 거인 몸에 닿으면 폭발
        if (g.IsTouching(transform.position, contactRadius)) { Explode(); return; }

        angle += angularSpeed * direction * dt;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 desired = c + new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * orbitRadius
                          + Vector3.up * (altitude + Mathf.Sin(Time.time * 0.6f + startAngle) * bobAmount);

        // 고도: 거인 어깨 높이를 넘지 않게
        if (capAtShoulder) desired.y = Mathf.Min(desired.y, g.ShoulderY);
        desired.y = Mathf.Max(desired.y, minAltitude);

        // 너무 가까우면 거인 반대 방향으로 밀어냄
        Vector3 flat = transform.position - c; flat.y = 0;
        if (flat.magnitude < panicRadius)
            desired += flat.normalized * (panicRadius - flat.magnitude) * 2f;

        Vector3 prev = transform.position;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref vel, smoothTime, maxSpeed);

        // 기수는 거인을 향하고, 이동 방향에 따라 기울기
        Vector3 look = c + Vector3.up * altitude * 0.7f - transform.position; look.y = 0;
        if (look.sqrMagnitude > 1f)
        {
            Quaternion yaw = Quaternion.LookRotation(look);
            Vector3 localVel = Quaternion.Inverse(yaw) * vel;
            float roll = Mathf.Clamp(-localVel.x * 0.25f, -28f, 28f);
            float pitch = Mathf.Clamp(localVel.z * 0.15f, -15f, 15f);
            transform.rotation = Quaternion.Slerp(transform.rotation, yaw * Quaternion.Euler(pitch, 0, roll), dt * 3f);
        }

        if (!g.IsDead && Time.time >= nextFire) Fire(g);
    }

    public void Explode()
    {
        if (IsDead) return;
        IsDead = true;
        ScoreManager.Add(ScoreManager.Instance ? ScoreManager.Instance.heliPoints : 800, "HELICOPTER");
        CombatFX.Explosion(transform.position, 24f);
        CombatFX.Explosion(transform.position + Random.insideUnitSphere * 6f, 14f);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.25f;
        vel = vel * 0.3f + Random.insideUnitSphere * 15f;
        fallSpin = new Vector3(Random.Range(-60f, 60f), Random.Range(200f, 400f), Random.Range(-60f, 60f));
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.5f);
    }

    // 폭발 후 추락 → 땅에 닿으면 한 번 더 폭발하고 사라짐
    void Fall(float dt)
    {
        vel += Physics.gravity * dt;
        transform.position += vel * dt;
        transform.Rotate(fallSpin * dt, Space.World);
        if (Random.value < 0.3f) CombatFX.Explosion(transform.position, 5f);
        if (transform.position.y <= 1f)
        {
            CombatFX.Explosion(new Vector3(transform.position.x, 2f, transform.position.z), 20f);
            Destroy(gameObject);
        }
    }

    void Fire(GiantHealth g)
    {
        nextFire = Time.time + fireInterval * Random.Range(0.8f, 1.2f);
        float side = Random.value < 0.5f ? -1f : 1f;
        Vector3 origin = transform.position + transform.right * side * 4f - transform.up * 2f + transform.forward * 3f;
        Vector3 aim = g.GetAimPoint();
        Vector3 dir = (aim - origin).normalized;
        CombatFX.Muzzle(origin, dir, 2f);
        CombatFX.Spawn(CombatFX.Kind.Rocket, origin, dir, rocketSpeed, damage, homing);
    }
}
