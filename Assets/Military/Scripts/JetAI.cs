using UnityEngine;

// 전투기: 멀리서 거인을 향해 날아와, 폭격 경로가 정해지면 '폭탄 낙하 지점(폭발 범위)'을 바닥에 먼저 표시해 경고한 뒤
// 일직선으로 날며 표시된 지점에 정확히 떨어지도록 폭탄을 투하. 통과 후 직진하다 선회해서 재공격. 거인 몸에 닿으면 폭발.
public class JetAI : MonoBehaviour
{
    public GameObject[] bombPrefabs;
    public float bombScale = 0.45f;

    [Header("비행")]
    public float speed = 140f;
    public float turnRate = 35f;
    [Tooltip("거인 머리 위로 얼마나 높이 지나갈지 (m)")] public float passHeightAboveHead = 25f;
    public float egressTime = 6f;

    [Header("폭격")]
    public int bombsPerPass = 2;
    [Tooltip("폭탄 낙하 지점 사이 간격 (m)")] public float bombSpacing = 170f;
    [Tooltip("이 거리 범위에서 거인을 정면으로 보면 폭격 경로 확정 + 경고 표시")] public float lockMinDistance = 650f;
    public float lockMaxDistance = 1150f;
    public float bombGravity = 30f;
    public float releaseDownSpeed = 5f;
    [Tooltip("폭발 몇 초 전에 경고 원을 보여줄지")] public float warningLeadTime = 3f;

    [Header("충돌")]
    public float contactRadius = 9f;

    public bool IsDead { get; private set; }

    enum State { Attack, BombRun, Egress }
    State state = State.Attack;
    float stateUntil, bank, runAlt;
    Vector3 vel, fallSpin, runDir;
    Vector3[] targets;
    WarningZone[] markers;
    int nextTarget;

    void Start() { vel = transform.forward * speed; }

    float PassAltitude(GiantHealth g) => g.ShoulderY + g.Scale * 0.35f + passHeightAboveHead;

    void Update()
    {
        float dt = Time.deltaTime;
        if (IsDead) { Fall(dt); return; }
        var g = GiantHealth.Instance;
        if (!g) { transform.position += vel * dt; return; }
        if (g.IsTouching(transform.position, contactRadius)) { Explode(); return; }

        Vector3 gp = g.transform.position;
        Vector3 flatVel = new Vector3(vel.x, 0, vel.z);
        Vector3 flatFwd = flatVel.normalized;
        Vector3 steerTarget;

        switch (state)
        {
            case State.Attack:
            {
                Vector3 to = gp - transform.position; to.y = 0;
                steerTarget = gp; steerTarget.y = PassAltitude(g);
                float ang = Vector3.Angle(flatFwd, to);
                if (!g.IsDead && !GiantHome.GiantSafe && ang < 5f && to.magnitude > lockMinDistance && to.magnitude < lockMaxDistance) StartRun(g); // 거인의 집 안이면 폭격 안 함
                else if (to.magnitude < lockMinDistance * 0.8f && Vector3.Dot(to, flatFwd) > 0) { state = State.Egress; stateUntil = Time.time + 3f; } // 너무 가까우면 지나쳐서 다시 진입
                break;
            }
            case State.BombRun:
            {
                steerTarget = transform.position + runDir * 500f; steerTarget.y = runAlt;
                if (nextTarget < targets.Length)
                {
                    float along = Vector3.Dot(targets[nextTarget] - transform.position, runDir);
                    float drop = 4f;
                    float tFall = Bomb.FallTime(transform.position.y - drop - 0.3f, releaseDownSpeed, bombGravity);
                    if (along <= speed * tFall) DropBomb(nextTarget++, drop);
                }
                else
                {
                    float along = Vector3.Dot(targets[targets.Length - 1] - transform.position, runDir);
                    if (along < -100f) { state = State.Egress; stateUntil = Time.time + egressTime; }
                }
                break;
            }
            default:
            {
                steerTarget = transform.position + flatFwd * 500f; steerTarget.y = PassAltitude(g) + 20f;
                if (Time.time >= stateUntil) state = State.Attack;
                break;
            }
        }

        float yawDelta;
        if (state == State.BombRun)
        {
            // 폭격 중엔 완전히 수평 직선 비행 (투하 지점 정확도)
            vel = runDir * speed;
            yawDelta = 0f;
            transform.position = new Vector3(transform.position.x, runAlt, transform.position.z);
        }
        else
        {
            Vector3 desiredDir = (steerTarget - transform.position).normalized;
            Vector3 newDir = Vector3.RotateTowards(vel.normalized, desiredDir, turnRate * Mathf.Deg2Rad * dt, 0f);
            yawDelta = Vector3.SignedAngle(new Vector3(vel.x, 0, vel.z), new Vector3(newDir.x, 0, newDir.z), Vector3.up);
            vel = newDir * speed;
        }
        transform.position += vel * dt;
        if (transform.position.y < 30f) transform.position = new Vector3(transform.position.x, 30f, transform.position.z);

        bank = Mathf.Lerp(bank, Mathf.Clamp(-yawDelta / Mathf.Max(dt, 0.0001f) * 1.2f, -70f, 70f), dt * 3f);
        transform.rotation = Quaternion.LookRotation(vel) * Quaternion.Euler(0, 0, bank);
    }

    // 폭격 경로 확정: 거인 현재 위치를 중심으로 낙하 지점을 정하고 경고 표시
    void StartRun(GiantHealth g)
    {
        Vector3 gp = g.transform.position;
        Vector3 to = gp - transform.position; to.y = 0;
        runDir = to.normalized;
        runAlt = Mathf.Max(transform.position.y, PassAltitude(g));
        vel = runDir * speed;

        int n = Mathf.Max(1, bombsPerPass);
        targets = new Vector3[n];
        markers = new WarningZone[n];
        float radius = BlastRadius();
        for (int i = 0; i < n; i++)
        {
            targets[i] = gp + runDir * (i - (n - 1) * 0.5f) * bombSpacing;
            targets[i].y = 0.3f;
            // 예상 폭발까지 시간 = 투하 지점까지 비행 + 낙하
            float tFall = Bomb.FallTime(runAlt - 4.3f, releaseDownSpeed, bombGravity);
            float along = Vector3.Dot(targets[i] - transform.position, runDir);
            float eta = Mathf.Max(0.5f, (along - speed * tFall) / speed) + tFall;
            markers[i] = WarningZone.Create(targets[i], radius, eta, eta - warningLeadTime, i == 0 ? "WARNING: BOMBING RUN! GET OUT OF THE RED ZONE!" : null);
        }
        nextTarget = 0;
        state = State.BombRun;
    }

    float BlastRadius() => Bomb.DefaultBlastRadius;

    void DropBomb(int i, float drop)
    {
        if (bombPrefabs == null || bombPrefabs.Length == 0) return;
        var prefab = bombPrefabs[Random.Range(0, bombPrefabs.Length)];
        var b = Bomb.Create(prefab, transform.position - Vector3.up * drop, runDir * speed + Vector3.down * releaseDownSpeed, bombScale);
        b.gravity = bombGravity;
        b.warningMarker = markers[i] ? markers[i].gameObject : null;
    }

    public void Explode()
    {
        if (IsDead) return;
        IsDead = true;
        ScoreManager.Add(ScoreManager.Instance ? ScoreManager.Instance.jetPoints : 1500, "FIGHTER JET");
        // 아직 투하 안 한 폭탄의 경고 표시 제거
        if (markers != null) for (int i = nextTarget; i < markers.Length; i++) if (markers[i]) Destroy(markers[i].gameObject);
        CombatFX.Explosion(transform.position, 26f);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.25f;
        fallSpin = new Vector3(Random.Range(-90f, 90f), Random.Range(-60f, 60f), Random.Range(200f, 400f));
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.5f);
    }

    void OnDestroy()
    {
        if (markers != null) for (int i = nextTarget; i < markers.Length; i++) if (markers[i]) Destroy(markers[i].gameObject);
    }

    void Fall(float dt)
    {
        vel += Vector3.down * 30f * dt;
        transform.position += vel * dt;
        transform.Rotate(fallSpin * dt, Space.Self);
        if (Random.value < 0.3f) CombatFX.Explosion(transform.position, 6f);
        if (transform.position.y <= 1f)
        {
            CombatFX.Explosion(new Vector3(transform.position.x, 2f, transform.position.z), 26f);
            Destroy(gameObject);
        }
    }
}
