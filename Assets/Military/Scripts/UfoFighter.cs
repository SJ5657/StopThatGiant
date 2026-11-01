using UnityEngine;

// 공격 UFO (후반, UfoSquadSpawner가 내보냄): 거인 머리 높이쯤에서 거인 주위를 빙빙 돌다가 갑자기 다른 자리로 휙 옮겨 가고,
// 밑에 초록 빛을 모았다가(경고) 광선을 쏨. 거인 손·몸에 닿으면 폭발해 떨어짐 (헬기처럼 잡을 수 있음).
// 레벨 25의 큰 축소 UFO(UfoEnemy)와는 별개.
public class UfoFighter : MonoBehaviour
{
    [HideInInspector] public UfoSquadSpawner owner;
    [HideInInspector] public Transform model;
    [HideInInspector] public float baseWidth = 10f;

    public bool IsDead { get; private set; }
    float angle, dir = 1f, radiusK, altK, nextDart, nextFire, chargeUntil = -1f, holdUntil, hp, spinSpeed;
    Vector3 vel, fallSpin;
    bool leaving; float leaveAt;
    LineRenderer beam; float beamUntil; Vector3 beamEnd;
    static readonly Color Beam = new Color(0.45f, 1f, 0.45f, 1f);

    float H(GiantHealth g) => Mathf.Max(2f, g.Height);

    void Start()
    {
        angle = Random.Range(0f, 360f);
        dir = Random.value < 0.5f ? 1f : -1f;
        radiusK = Random.Range(1.1f, 2f);
        altK = Random.Range(0.6f, 1f);
        nextDart = Time.time + Random.Range(3f, 6f);
        nextFire = Time.time + Random.Range(3f, 5f);
        hp = owner ? owner.hp : 60f;
        spinSpeed = Random.Range(90f, 160f);
        beam = new GameObject("UfoBeam").AddComponent<LineRenderer>();
        beam.transform.SetParent(transform, false);
        beam.sharedMaterial = WarningZone.material;
        beam.positionCount = 2;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.enabled = false;
    }

    // 지속 피해 (방귀 가스 등)
    public void TakeDamage(float dmg)
    {
        if (IsDead) return;
        hp -= dmg;
        if (hp <= 0f) Explode();
    }

    public void Leave() { if (!IsDead && !leaving) { leaving = true; leaveAt = Time.time; beam.enabled = false; chargeUntil = -1f; } }

    Vector3 Belly => transform.position - Vector3.up * transform.lossyScale.y * baseWidth * 0.12f;

    void Update()
    {
        float dt = Time.deltaTime;
        if (model) model.Rotate(0f, spinSpeed * dt, 0f, Space.Self);
        if (IsDead) { Fall(dt); return; }
        var g = GiantHealth.Instance;
        if (!g || !owner) return;
        float h = H(g);
        Vector3 c = g.transform.position;

        // 크기: 거인 키에 맞춤
        float want = h * owner.sizePerHeight / Mathf.Max(0.1f, baseWidth);
        transform.localScale = Vector3.one * Mathf.MoveTowards(transform.localScale.x, want, want * dt);

        if (g.IsTouching(transform.position, transform.localScale.x * baseWidth * 0.45f)) { Explode(); return; }
        if (beam.enabled && Time.time > beamUntil) beam.enabled = false;
        if (beam.enabled) DrawBeam();
        if (EnemyFreeze.Active) return;

        if (leaving || GiantHome.GiantSafe || g.IsDead)
        {
            // 하늘 위로 솟구쳐 사라짐
            if (!leaving) Leave();
            vel = Vector3.MoveTowards(vel, Vector3.up * h * 4f, h * 4f * dt);
            transform.position += vel * dt;
            if (Time.time - leaveAt > 4f) Destroy(gameObject);
            return;
        }

        // 가끔 다른 자리로 휙 옮겨 감 (UFO다운 급한 움직임)
        if (Time.time >= nextDart)
        {
            nextDart = Time.time + Random.Range(3f, 7f);
            angle += Random.Range(50f, 130f) * (Random.value < 0.5f ? -1f : 1f);
            radiusK = Random.Range(1f, 2f);
            altK = Random.Range(0.6f, 1f);
            if (Random.value < 0.3f) dir = -dir;
        }
        bool charging = chargeUntil > 0f;
        angle += owner.orbitSpeed * dir * (charging ? 0.2f : 1f) * dt;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 desired = c + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * h * radiusK + Vector3.up * (h * altK + Mathf.Sin(Time.time * 2f + angle) * h * 0.04f);
        float ground = WildTerrain.Active ? WildTerrain.Height(desired.x, desired.z) : 0f;
        desired.y = Mathf.Max(desired.y, ground + h * 0.4f);
        // 모으는 동안과 쏜 직후엔 그 자리에 떠 있음 → 거인이 달려가 잡을 수 있음
        if (charging || Time.time < holdUntil) desired = new Vector3(transform.position.x, desired.y, transform.position.z);
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref vel, 0.6f, h * owner.maxSpeedPerHeight);
        // 움직이는 쪽으로 살짝 기울어짐
        Vector3 lv = vel; lv.y = 0f;
        Vector3 tilt = Vector3.Cross(Vector3.up, lv.normalized) * Mathf.Clamp(lv.magnitude / Mathf.Max(1f, h), 0f, 1f) * 18f;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(-tilt.x, 0f, -tilt.z), dt * 4f);

        // 사격: 밑에 초록 빛을 모았다가 광선
        bool blocked = GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || GameStartMenu.InMenu;
        if (!charging && !blocked && Time.time >= nextFire && !MilitarySpawner.OutOfSight(transform.position)) chargeUntil = Time.time + owner.chargeTime;
        if (charging)
        {
            float k = 1f - (chargeUntil - Time.time) / Mathf.Max(0.1f, owner.chargeTime);
            float s = transform.localScale.x * baseWidth;
            MagicFX.Glow(Belly, Vector3.zero, Beam, s * (0.1f + 0.25f * k), 0.05f);
            Vector3 off = Random.onUnitSphere * s * 0.4f;
            MagicFX.Glow(Belly + off, -off * 3f, Beam, s * 0.06f, 0.3f);
            if (Time.time >= chargeUntil)
            {
                chargeUntil = -1f;
                nextFire = Time.time + owner.fireInterval * Random.Range(0.8f, 1.25f);
                holdUntil = Time.time + owner.hoverAfterFire;
                if (!blocked) Fire(g, h);
            }
        }
    }

    void Fire(GiantHealth g, float h)
    {
        Vector3 aim = g.GetAimPoint();
        bool hit = Random.value < owner.accuracy;
        if (!hit) aim += Random.onUnitSphere * h * 0.4f + Vector3.down * h * 0.2f;
        beamEnd = aim;
        beam.enabled = true; beamUntil = Time.time + 0.22f;
        DrawBeam();
        MagicFX.Burst(aim, Beam, h * 0.05f, 14);
        if (hit) g.TakeDamage(owner.beamDamage, false);
    }

    void DrawBeam()
    {
        float left = Mathf.Clamp01((beamUntil - Time.time) / 0.22f);
        float w = transform.localScale.x * baseWidth * 0.08f * left;
        beam.startWidth = w; beam.endWidth = w * 0.6f;
        beam.startColor = beam.endColor = new Color(Beam.r, Beam.g, Beam.b, 0.4f + 0.6f * left);
        beam.SetPosition(0, Belly);
        beam.SetPosition(1, beamEnd);
    }

    public void Explode()
    {
        if (IsDead) return;
        IsDead = true;
        beam.enabled = false;
        ScoreManager.Add(owner ? owner.points : 1000, "UFO", transform.position);
        float s = transform.localScale.x * baseWidth;
        CombatFX.Explosion(transform.position, s * 1.2f);
        MagicFX.Burst(transform.position, Beam, s * 0.3f, 30);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.3f;
        vel = vel * 0.3f + Random.insideUnitSphere * 10f;
        fallSpin = new Vector3(Random.Range(-90f, 90f), Random.Range(300f, 600f), Random.Range(-90f, 90f));
        spinSpeed = 0f;
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.5f);
    }

    void Fall(float dt)
    {
        vel += Physics.gravity * dt;
        transform.position += vel * dt;
        transform.Rotate(fallSpin * dt, Space.World);
        if (Random.value < 0.3f) CombatFX.Explosion(transform.position, 4f);
        float ground = WildTerrain.Active ? WildTerrain.Height(transform.position.x, transform.position.z) : 0f;
        if (transform.position.y <= ground + 1f)
        {
            CombatFX.Explosion(new Vector3(transform.position.x, ground + 2f, transform.position.z), transform.localScale.x * baseWidth * 1.5f);
            Destroy(gameObject);
        }
    }
}
