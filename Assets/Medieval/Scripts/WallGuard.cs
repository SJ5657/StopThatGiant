using UnityEngine;

// 성벽 위 수비병 하나 (WallDefense가 거인 근처에서만 세우고, 멀어지면 치움).
//  - 궁수: 성벽 위 길을 오가다 거인이 사거리 안·화면 안에 오면 멈춰 서서 화살을 쏨
//  - 투석기: 거인 쪽으로 돌아서 돌덩이를 던짐
// 거인이 바로 옆까지 오면(성벽 위를 손으로 쓸어버림) 죽고, 밑의 성벽이 부서지면 떨어져 죽음 (점수·경험치 구슬).
// 탑 옥상 수비병은 높아서 옆에 와도 안 죽음: 몸이 실제로 닿거나 탑이 부서져야 죽음.
public class WallGuard : MonoBehaviour
{
    public enum Kind { Archer, Catapult }
    [HideInInspector] public Kind kind;
    [HideInInspector] public Transform wall;      // 서 있는 성벽 조각
    [HideInInspector] public Vector3 endA, endB;  // 오가는 구간 (궁수)
    [HideInInspector] public WallDefense owner;
    [HideInInspector] public int slot;
    [HideInInspector] public float scale = 1.2f;
    [HideInInspector] public bool onTower;        // 탑 옥상 (성벽이 아니라)

    public bool Dead { get; private set; }
    Animator anim; CatapultArm arm;
    float nextShot, animSpeed;
    bool toB = true, falling;
    Vector3 vel;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        arm = GetComponentInChildren<CatapultArm>();
        nextShot = Time.time + (kind == Kind.Catapult ? Random.Range(3f, 7f) : Random.Range(1f, 3f)); // 한꺼번에 쏘지 않게 흩어 놓음
        toB = Random.value < 0.5f;
    }

    static bool Blocked(GiantHealth g) => !g || g.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || GameStartMenu.InMenu;

    void Update()
    {
        if (Dead) { if (falling) Fall(); return; }
        if (!wall || !wall.gameObject.activeInHierarchy) { Die(true); return; } // 밑의 성벽이 부서짐
        var g = GiantHealth.Instance;
        if (!g || !owner) return;
        Vector3 pos = transform.position;
        Vector3 to = g.transform.position - pos; to.y = 0f;
        float d = to.magnitude;
        // 거인이 바로 옆: 성벽 위를 손으로 쓸어버림
        if ((!onTower && d < owner.SwatReach) || g.IsTouching(pos + Vector3.up, kind == Kind.Archer ? 2.5f : 4f)) { Die(false); return; }
        if (EnemyFreeze.Active) return;
        float dt = Time.deltaTime;
        bool engage = !Blocked(g) && d < (kind == Kind.Archer ? owner.archerRange : owner.catapultRange) && !MilitarySpawner.OutOfSight(pos);

        if (kind == Kind.Archer)
        {
            float speed = 0f;
            if (!engage)
            {
                // 성벽 위 길을 오감
                Vector3 target = toB ? endB : endA, mv = target - pos; mv.y = 0f;
                if (mv.magnitude < 0.5f) toB = !toB;
                else
                {
                    speed = 2.8f * scale;
                    transform.position = pos + mv.normalized * Mathf.Min(speed * dt, mv.magnitude);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(mv), 540f * dt);
                }
            }
            else
            {
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * dt);
                if (Time.time >= nextShot) ShootArrow(g);
            }
            animSpeed = Mathf.MoveTowards(animSpeed, speed > 0f ? 0.5f : 0f, dt * 3f);
            if (anim) anim.SetFloat(SpeedHash, animSpeed);
        }
        else if (engage)
        {
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 60f * dt);
            if (Time.time >= nextShot && Vector3.Angle(transform.forward, to) < 10f) ThrowBoulder(g);
        }
    }

    void ShootArrow(GiantHealth g)
    {
        nextShot = Time.time + owner.archerInterval * Random.Range(0.8f, 1.25f);
        Vector3 from = transform.position + Vector3.up * 2.6f * scale + transform.forward * 0.8f;
        Vector3 aim = g.GetAimPoint();
        bool hit = Random.value < owner.archerAccuracy;
        FlyingArrow.Shoot(from, hit ? aim : aim + Random.onUnitSphere * g.Scale * 0.8f);
        if (hit) g.TakeDamage(owner.archerDamage, false);
    }

    void ThrowBoulder(GiantHealth g)
    {
        nextShot = Time.time + owner.catapultInterval * Random.Range(0.85f, 1.2f);
        Vector3 from = transform.position + Vector3.up * 4f + transform.forward * 2f;
        Vector3 aim = g.GetAimPoint();
        aim += g.Velocity * (Vector3.Distance(from, aim) / owner.boulderSpeed) * 0.7f + Random.insideUnitSphere * 4f;
        CombatFX.Spawn(CombatFX.Kind.Shell, from, (aim - from).normalized, owner.boulderSpeed, owner.boulderDamage, 0f);
        CombatFX.Muzzle(from, transform.forward, 3f);
        if (arm) arm.Swing();
    }

    void Die(bool fall)
    {
        Dead = true;
        if (owner) owner.OnGuardDied(slot);
        Vector3 p = transform.position;
        bool archer = kind == Kind.Archer;
        ScoreManager.Add(archer ? owner.archerPoints : owner.catapultPoints, archer ? "궁수" : "투석기", p + Vector3.up * 2f);
        if (anim) anim.enabled = false;
        if (fall)
        {
            // 무너지는 성벽에서 떨어짐
            falling = true;
            vel = new Vector3(Random.Range(-4f, 4f), 3f, Random.Range(-4f, 4f));
            return;
        }
        Squash(p);
    }

    void Squash(Vector3 p)
    {
        var s = transform.localScale;
        transform.localScale = new Vector3(s.x * 1.3f, s.y * (kind == Kind.Archer ? 0.12f : 0.3f), s.z * 1.2f);
        if (kind == Kind.Archer) BloodSplat.Spawn(p, scale);
        else
        {
            CombatFX.Explosion(p + Vector3.up * 2f, 8f);
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.35f;
        }
        SinkAway.Begin(gameObject, 2.5f, 0.8f);
    }

    void Fall()
    {
        float dt = Time.deltaTime;
        vel += Vector3.down * 30f * dt;
        transform.position += vel * dt;
        Vector3 p = transform.position;
        float ground = WildTerrain.Active ? WildTerrain.Height(p.x, p.z) : 0f;
        if (p.y > ground) return;
        falling = false;
        transform.position = new Vector3(p.x, ground, p.z);
        Squash(transform.position);
    }
}
