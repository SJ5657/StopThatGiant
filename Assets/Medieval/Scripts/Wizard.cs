using UnityEngine;

// 마법사 하나 (WizardSpawner가 내보냄): 걸어서 거인과 일정 거리까지 다가와, 서서 지팡이에 힘을 모았다가(끝이 밝아짐) 주문을 씀.
//  - 비전: 거인 쪽으로 휘어 날아오는 보라 구슬
//  - 얼음: 하늘색 구슬, 맞으면 잠시 느려짐 (GiantRopes.Chill)
//  - 폭풍: 거인이 갈 곳에 노란 원 → 잠시 뒤 번개 (LightningStrike)
// 거인이 너무 가까이 오면 보랏빛 연기와 함께 순간이동으로 달아남 (쿨타임 동안은 뛰어서). 밟히면 납작 (점수·경험치).
public class Wizard : MonoBehaviour
{
    public enum School { Arcane, Frost, Storm }
    [HideInInspector] public School school;
    [HideInInspector] public float scale = 1.2f;
    [HideInInspector] public WizardSpawner owner;
    [HideInInspector] public Transform orb; // 지팡이 끝

    public bool IsDead { get; private set; }
    Animator anim;
    float nextCast, castUntil = -1f, nextBlink, animSpeed;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        nextCast = Time.time + Random.Range(2f, 4f);
    }

    static float Ground(Vector3 p) => WildTerrain.Active ? WildTerrain.Height(p.x, p.z) : 0f;
    static bool Blocked(GiantHealth g) => !g || g.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || GameStartMenu.InMenu;

    void Update()
    {
        if (IsDead || !owner) return;
        var g = GiantHealth.Instance;
        var ctrl = GiantController.Instance;
        if (!g || !ctrl) return;
        Vector3 pos = transform.position;
        Vector3 toG = g.transform.position - pos; toG.y = 0f;
        float d = toG.magnitude;
        float squash = (CivilianManager.Instance ? CivilianManager.Instance.squashRadius : 0.3f) * ctrl.GameScale;
        if (d < squash || g.IsTouching(pos + Vector3.up, 1.5f)) { Die(); return; }
        Color col = owner.SpellColor(school);
        bool casting = castUntil > 0f;
        // 지팡이 끝 빛 (모으는 동안 커지고 반짝임이 빨려 들어감)
        if (orb)
        {
            float charge = casting ? Mathf.Clamp01(1f - (castUntil - Time.time) / Mathf.Max(0.1f, owner.castTime)) : 0f;
            float size = scale * (0.9f + charge * 2.2f) * (0.9f + 0.1f * Mathf.Sin(Time.time * 12f));
            MagicFX.Glow(orb.position, Vector3.zero, Color.Lerp(col, Color.white, 0.5f), size, 0.05f);
            MagicFX.Glow(orb.position, Vector3.zero, col, size * 2f, 0.05f);
            if (casting && Random.value < 0.6f)
            {
                Vector3 off = Random.onUnitSphere * scale * 1.6f;
                MagicFX.Glow(orb.position + off, -off * 2.5f, col, scale * 0.3f, 0.4f);
            }
        }
        if (EnemyFreeze.Active) return;
        if (d > owner.despawnDistance) { Destroy(gameObject); return; }

        float h = Mathf.Max(2f, (g.ShoulderY - g.transform.position.y) / 0.837f);
        float prefer = h * owner.preferRangePerHeight, tooClose = h * owner.minRangePerHeight, range = h * owner.castRangePerHeight;
        float dt = Time.deltaTime, speed = 0f;

        // 너무 가까움: 순간이동 (쿨타임이면 뜀, 주문은 취소)
        if (d < tooClose && Time.time >= nextBlink) { Blink(g, h); return; }
        Vector3 move = Vector3.zero;
        if (!casting)
        {
            if (d < tooClose) { move = -toG.normalized; speed = owner.runSpeed * scale; }
            else if (d > prefer) { move = toG.normalized; speed = (d > range ? owner.runSpeed : owner.runSpeed * 0.5f) * scale; }
        }
        if (speed > 0f)
        {
            pos += move * speed * dt;
            pos.y = Ground(pos);
            transform.position = pos;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(move), 540f * dt);
        }
        else if (toG.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toG), 360f * dt);

        bool canCast = !Blocked(g) && d < range && d > tooClose && !MilitarySpawner.OutOfSight(pos);
        if (!casting && canCast && Time.time >= nextCast) castUntil = Time.time + owner.castTime;
        if (casting && Time.time >= castUntil)
        {
            castUntil = -1f;
            nextCast = Time.time + owner.castInterval * Random.Range(0.85f, 1.2f);
            if (canCast) Cast(g, h, col);
        }

        animSpeed = Mathf.MoveTowards(animSpeed, speed <= 0f ? 0f : speed > owner.runSpeed * scale * 0.6f ? 1f : 0.5f, dt * 4f);
        if (anim) anim.SetFloat(SpeedHash, animSpeed);
    }

    void Cast(GiantHealth g, float h, Color col)
    {
        Vector3 from = orb ? orb.position : transform.position + Vector3.up * 2.5f * scale;
        MagicFX.Burst(from, col, scale * 0.8f, 10);
        var o = owner;
        switch (school)
        {
            case School.Arcane:
                MagicBolt.Cast(from, g.GetAimPoint() - from, o.boltSpeed, o.boltDamage, o.boltHoming, col, Mathf.Max(1.2f, h * 0.06f));
                break;
            case School.Frost:
                MagicBolt.Cast(from, g.GetAimPoint() - from, o.boltSpeed * 1.15f, o.frostDamage, o.boltHoming, col, Mathf.Max(1.2f, h * 0.06f),
                    () => GiantRopes.Chill(o.frostSlow, o.frostTime));
                break;
            case School.Storm:
                // 지금 가는 방향으로 1초쯤 뒤 있을 곳에 떨어뜨림
                Vector3 at = g.transform.position + g.Velocity * Mathf.Min(1f, o.lightningDelay * 0.7f);
                LightningStrike.Cast(at, h * o.lightningRadiusPerHeight, o.lightningDamage, o.lightningDelay, h * 3f);
                MagicFX.Glow(from, Vector3.up * h, col, scale * 1.2f, 0.4f); // 하늘로 솟는 빛
                break;
        }
    }

    // 보랏빛 연기와 함께 사라져, 거인에게서 원하는 거리쯤 다른 곳에 나타남
    void Blink(GiantHealth g, float h)
    {
        Color col = owner.SpellColor(school);
        MagicFX.Burst(transform.position + Vector3.up * scale, col, scale * 1.5f, 24);
        Vector3 to = owner.PickSpot(g.transform.position, h * owner.preferRangePerHeight, false);
        transform.position = to;
        Vector3 look = g.transform.position - to; look.y = 0f;
        if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
        MagicFX.Burst(to + Vector3.up * scale, col, scale * 1.5f, 24);
        castUntil = -1f;
        nextBlink = Time.time + owner.blinkCooldown;
        nextCast = Mathf.Max(nextCast, Time.time + 1.5f);
    }

    void Die()
    {
        IsDead = true;
        if (anim) anim.enabled = false;
        var s = transform.localScale;
        transform.localScale = new Vector3(s.x * 1.4f, s.y * 0.12f, s.z * 1.4f);
        BloodSplat.Spawn(transform.position, scale);
        if (owner) MagicFX.Burst(transform.position + Vector3.up * 0.5f, owner.SpellColor(school), scale, 16);
        ScoreManager.Add(owner ? owner.points : 150, "마법사", transform.position + Vector3.up);
        SinkAway.Begin(gameObject, 2f, 0.8f);
    }
}
