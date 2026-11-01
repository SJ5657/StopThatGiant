using UnityEngine;

// 폭발/포구 섬광 파티클과 투사체 생성을 담당.
public class CombatFX : MonoBehaviour
{
    public static CombatFX Instance { get; private set; }
    public Material fireMaterial;      // 가산(Additive) 파티클
    public Material smokeMaterial;     // 알파 블렌드 파티클
    public Material shellMaterial;     // 포탄 몸체
    public Material rocketMaterial;    // 로켓 몸체
    public Material warningMaterial;   // 버텍스 컬러 + 알파 재질 (총알 궤적·핏자국 등에 공유)
    [Tooltip("중세 씬(GameEra.Medieval)에서 포탄 대신 쓰는 돌덩이 재질 (창 촉에도 씀)")] public Material boulderMaterial;
    [Tooltip("그리폰 기사가 던지는 창 자루 재질")] public Material javelinMaterial;

    ParticleSystem fire, smoke;

    void Awake()
    {
        Instance = this;
        WarningZone.material = warningMaterial;
        fire = MakePS("FireFX", fireMaterial, 0.35f, 0.7f, new Color(1f, 0.65f, 0.25f, 1f), 800);
        smoke = MakePS("SmokeFX", smokeMaterial, 1.8f, 3.2f, new Color(0.45f, 0.45f, 0.45f, 0.7f), 800);
    }

    ParticleSystem MakePS(string n, Material mat, float lifeMin, float lifeMax, Color col, int max)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startColor = col;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) });
        c.color = g;
        var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 1.5f));
        var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.15f; lv.limit = 0f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    public static void Explosion(Vector3 p, float size)
    {
        var fx = Instance; if (!fx) return;
        for (int i = 0; i < 10; i++)
        {
            var ep = new ParticleSystem.EmitParams { position = p + Random.insideUnitSphere * size * 0.3f, velocity = Random.insideUnitSphere * size * 1.5f, startSize = size * Random.Range(0.6f, 1.3f) };
            fx.fire.Emit(ep, 1);
        }
        for (int i = 0; i < 7; i++)
        {
            var ep = new ParticleSystem.EmitParams { position = p + Random.insideUnitSphere * size * 0.4f, velocity = (Random.insideUnitSphere + Vector3.up) * size * 0.5f, startSize = size * Random.Range(1f, 1.8f) };
            fx.smoke.Emit(ep, 1);
        }
    }

    public static void Muzzle(Vector3 p, Vector3 dir, float size)
    {
        var fx = Instance; if (!fx) return;
        if (GameEra.Medieval)
        {
            // 중세: 화약 불꽃 대신 흙먼지 (활처럼 작은 건 아무것도 안 나옴)
            if (size < 1.5f) return;
            var dust = new ParticleSystem.EmitParams { position = p, velocity = dir * size * 0.6f + Vector3.up * size * 0.3f, startSize = size * 1.4f, startLifetime = 1.4f, startColor = new Color(0.62f, 0.55f, 0.45f, 0.6f) };
            fx.smoke.Emit(dust, 3);
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            var ep = new ParticleSystem.EmitParams { position = p, velocity = (dir + Random.insideUnitSphere * 0.3f) * size * 4f, startSize = size * Random.Range(0.6f, 1.1f), startLifetime = 0.15f };
            fx.fire.Emit(ep, 1);
        }
        var sp = new ParticleSystem.EmitParams { position = p, velocity = dir * size, startSize = size * 1.5f, startLifetime = 1.2f };
        fx.smoke.Emit(sp, 2);
    }

    // Javelin·Fireball은 중세 적용 (그리폰 기사의 창, 와이번의 불덩이)
    public enum Kind { Shell, Rocket, Javelin, Fireball }

    public static Projectile Spawn(Kind kind, Vector3 pos, Vector3 dir, float speed, float damage, float homing)
    {
        if (kind == Kind.Javelin || kind == Kind.Fireball) return SpawnMedieval(kind, pos, dir, speed, damage, homing);
        var fx = Instance;
        var go = GameObject.CreatePrimitive(kind == Kind.Shell ? PrimitiveType.Sphere : PrimitiveType.Capsule);
        Destroy(go.GetComponent<Collider>());
        go.name = kind.ToString();
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(dir);
        if (kind == Kind.Shell) go.transform.localScale = Vector3.one * 1.9f;
        else
        {
            // 캡슐을 진행방향(z)으로 눕힘
            var body = go.transform; body.localScale = new Vector3(1.2f, 2.4f, 1.2f);
            var holder = new GameObject("Rocket").transform;
            holder.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            body.SetParent(holder, true);
            body.localRotation = Quaternion.Euler(90, 0, 0);
            body.localPosition = Vector3.zero;
            go = holder.gameObject;
        }
        var mr = go.GetComponentInChildren<MeshRenderer>();
        if (fx) mr.sharedMaterial = kind == Kind.Shell ? fx.shellMaterial : fx.rocketMaterial;
        bool boulder = kind == Kind.Shell && GameEra.Medieval; // 중세: 포탄 대신 투석기 돌덩이 (크고 회색, 흙먼지 꼬리)
        if (boulder)
        {
            go.transform.localScale = Vector3.one * 3.2f;
            if (fx && fx.boulderMaterial) mr.sharedMaterial = fx.boulderMaterial;
        }

        // 꼬리 궤적
        var trailGo = new GameObject("Trail");
        trailGo.transform.SetParent(go.transform, false);
        var tr = trailGo.AddComponent<TrailRenderer>();
        if (boulder)
        {
            tr.time = 0.35f; tr.startWidth = 2.6f; tr.endWidth = 0.4f;
            tr.startColor = new Color(0.66f, 0.6f, 0.5f, 0.55f); tr.endColor = new Color(0.6f, 0.55f, 0.45f, 0f);
            if (fx) tr.sharedMaterial = fx.smokeMaterial;
        }
        else if (kind == Kind.Shell)
        {
            tr.time = 0.25f; tr.startWidth = 1.9f; tr.endWidth = 0f;
            tr.startColor = new Color(1f, 0.85f, 0.4f, 1f); tr.endColor = new Color(1f, 0.4f, 0.1f, 0f);
            if (fx) tr.sharedMaterial = fx.fireMaterial;
        }
        else
        {
            tr.time = 1.4f; tr.startWidth = 1.5f; tr.endWidth = 6f;
            tr.startColor = new Color(0.9f, 0.9f, 0.9f, 0.8f); tr.endColor = new Color(0.6f, 0.6f, 0.6f, 0f);
            if (fx) tr.sharedMaterial = fx.smokeMaterial;
        }
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.minVertexDistance = 2f;

        var p = go.AddComponent<Projectile>();
        p.Launch(dir * speed, damage, homing, kind == Kind.Shell ? 6f : 9f, tr);
        ThreatGlow.Add(go.transform, kind == Kind.Shell ? 18f : 22f); // 날아오는 것을 알아볼 수 있게 붉은 후광
        return p;
    }

    // 창: 길고 가는 나무 자루 + 쇠 촉, 짧은 흰 꼬리 (빠르고 곧게, 작게 터짐)
    // 불덩이: 이글거리는 불 공 + 긴 불꽃 꼬리 (느리게 따라오고 크게 터짐)
    static Projectile SpawnMedieval(Kind kind, Vector3 pos, Vector3 dir, float speed, float damage, float homing)
    {
        var fx = Instance;
        var holder = new GameObject(kind.ToString()).transform;
        holder.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
        var tr = new GameObject("Trail").AddComponent<TrailRenderer>();
        tr.transform.SetParent(holder, false);
        if (kind == Kind.Javelin)
        {
            Part(holder, PrimitiveType.Cube, Vector3.zero, new Vector3(0.35f, 0.35f, 8f), fx ? fx.javelinMaterial : null);
            Part(holder, PrimitiveType.Cube, new Vector3(0f, 0f, 4.4f), new Vector3(0.6f, 0.6f, 1.2f), fx ? fx.boulderMaterial : null);
            tr.time = 0.25f; tr.startWidth = 0.8f; tr.endWidth = 0f;
            tr.startColor = new Color(1f, 1f, 1f, 0.6f); tr.endColor = new Color(1f, 1f, 1f, 0f);
            if (fx) tr.sharedMaterial = fx.smokeMaterial;
        }
        else
        {
            Part(holder, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 4.5f, fx ? fx.fireMaterial : null);
            Part(holder, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 2.6f, fx ? fx.fireMaterial : null); // 밝은 속불
            tr.time = 0.9f; tr.startWidth = 4.5f; tr.endWidth = 0.5f;
            tr.startColor = new Color(1f, 0.75f, 0.25f, 1f); tr.endColor = new Color(1f, 0.25f, 0.05f, 0f);
            if (fx) tr.sharedMaterial = fx.fireMaterial;
        }
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.minVertexDistance = 2f;
        var p = holder.gameObject.AddComponent<Projectile>();
        p.Launch(dir * speed, damage, homing, kind == Kind.Javelin ? 3.5f : 14f, tr);
        ThreatGlow.Add(holder, kind == Kind.Javelin ? 12f : 24f);
        return p;
    }

    static void Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos; go.transform.localScale = size;
        var mr = go.GetComponent<MeshRenderer>();
        if (mat) mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 드래곤 불길: mouth에서 dir 쪽으로 불꽃 입자를 뿜음 (매 프레임 부름)
    public static void FireBreath(Vector3 mouth, Vector3 dir, float length)
    {
        var fx = Instance; if (!fx) return;
        for (int i = 0; i < 4; i++)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = mouth + Random.insideUnitSphere * 1.5f,
                velocity = (dir + Random.insideUnitSphere * 0.13f).normalized * length * Random.Range(2.2f, 2.8f),
                startSize = Random.Range(6f, 12f),
                startLifetime = Random.Range(0.55f, 0.8f),
            };
            fx.fire.Emit(ep, 1);
        }
        if (Random.value < 0.3f)
        {
            var sp = new ParticleSystem.EmitParams { position = mouth + dir * length * 0.6f, velocity = dir * length * 0.3f + Vector3.up * 8f, startSize = Random.Range(8f, 14f), startLifetime = 1.5f };
            fx.smoke.Emit(sp, 1);
        }
    }
}
