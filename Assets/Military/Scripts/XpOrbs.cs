using System.Collections.Generic;
using UnityEngine;

// 경험치 구슬: 부서진 오브젝트에서 빛나는 구슬이 톡 튀어나와 잠깐 떠 있다가 거인 가슴으로 빨려 들어감.
// 구슬이 거인에게 닿는 순간 그 몫의 경험치가 들어감 (ScoreManager.Add에 위치를 넘기면 이걸 거침).
// 렌더링은 파티클 시스템 두 개: 구슬(위치를 매 프레임 직접 씀) + 꼬리·흡수 반짝임(일반 파티클).
public class XpOrbs : MonoBehaviour
{
    static XpOrbs inst;
    const int MaxOrbs = 300;

    struct Orb { public Vector3 pos, vel; public float born, delay, size, seed; public int xp; }
    readonly List<Orb> orbs = new List<Orb>();
    ParticleSystem.Particle[] buf = new ParticleSystem.Particle[MaxOrbs * 2];
    ParticleSystem orbPS, sparkPS;
    Transform chest; Animator chestAnim;

    static readonly Color Core = new Color(0.85f, 0.97f, 1f, 1f);         // 경험치 바와 같은 하늘색 계열
    static readonly Color Halo = new Color(0.25f, 0.65f, 1f, 0.7f);

    // 구슬로 경험치를 보냄. 효과를 못 만들면(재질·거인 없음) false → 호출한 쪽이 바로 경험치를 줌
    public static bool Emit(Vector3 from, int xp)
    {
        if (xp <= 0) return true;
        var g = GiantHealth.Instance;
        if (!g || g.IsDead || !CombatFX.Instance || !CombatFX.Instance.fireMaterial) return false;
        if (!inst) inst = new GameObject("XpOrbs").AddComponent<XpOrbs>();
        return inst.Spawn(from, xp, g);
    }

    void Awake()
    {
        // Resources/XpOrb 재질(깊이 무시 가산 빛, 나무·건물에 가려도 보임). 없으면 불꽃 재질을 복사. 텍스처는 동그랗게 번지는 빛
        var src = Resources.Load<Material>("XpOrb");
        var mat = new Material(src ? src : CombatFX.Instance.fireMaterial) { name = "XpOrb", mainTexture = GlowTexture() };
        orbPS = MakePS("XpOrbCores", mat, MaxOrbs * 2, false);
        sparkPS = MakePS("XpOrbSparks", mat, 1500, true);
    }

    void OnDestroy() { if (inst == this) inst = null; }

    internal static Texture2D GlowTexture()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "XpGlow" };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a = a * a * (0.6f + 0.4f * a) + Mathf.Clamp01(1f - r * 3f) * 0.6f; // 부드러운 빛 + 밝은 중심
                byte b = (byte)(Mathf.Clamp01(a) * 255f);
                px[y * N + x] = new Color32(b, b, b, b);
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    ParticleSystem MakePS(string n, Material mat, int max, bool fade)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.startLifetime = 0.5f;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        if (fade)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) });
            c.color = gr;
            var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.2f));
        }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.maxParticleSize = 2f;
        ps.Play();
        return ps;
    }

    bool Spawn(Vector3 from, int xp, GiantHealth g)
    {
        int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(xp) / 3f), 1, 12); // 경험치가 많을수록 구슬이 많음 (2→1개, 100→4개, 1500→12개)
        if (orbs.Count + n > MaxOrbs) return false;
        float h = GiantHeight(g);
        float size = h * 0.12f * Mathf.Lerp(0.7f, 1.4f, Mathf.InverseLerp(1f, 3.2f, Mathf.Log10(xp + 1f)));
        int each = xp / n, rest = xp - each * n;
        for (int i = 0; i < n; i++)
        {
            Vector3 burst = Random.onUnitSphere; burst.y = Mathf.Abs(burst.y) * 1.5f + 0.4f; // 위쪽으로 톡 튀어나옴
            orbs.Add(new Orb
            {
                pos = from + Random.insideUnitSphere * h * 0.05f,
                vel = burst.normalized * h * Random.Range(0.5f, 0.9f),
                born = Time.time,
                delay = Random.Range(0.25f, 0.5f),
                size = size * Random.Range(0.85f, 1.15f),
                seed = Random.value * 10f,
                xp = each + (i == 0 ? rest : 0),
            });
        }
        return true;
    }

    static float GiantHeight(GiantHealth g) => Mathf.Max(2f, (g.ShoulderY - g.transform.position.y) / 0.837f);

    Vector3 Target(GiantHealth g)
    {
        var ctrl = GiantController.Instance;
        var a = ctrl ? ctrl.GetComponentInChildren<Animator>() : null;
        if (a != chestAnim) { chestAnim = a; chest = a && a.isHuman ? (a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest)) : null; }
        return chest ? chest.position : new Vector3(g.transform.position.x, g.ShoulderY * 0.85f + g.transform.position.y * 0.15f, g.transform.position.z);
    }

    void Update()
    {
        var g = GiantHealth.Instance;
        if (orbs.Count == 0) { orbPS.Clear(); return; }
        if (!g || g.IsDead || GameStartMenu.InMenu)
        {
            // 거인이 없으면 남은 경험치는 바로 넣고 구슬은 없앰
            foreach (var o in orbs) Grant(o.xp);
            orbs.Clear(); orbPS.Clear();
            return;
        }
        float dt = Time.deltaTime, now = Time.time, h = GiantHeight(g);
        Vector3 tgt = Target(g);
        int count = 0, arrived = 0;
        for (int i = orbs.Count - 1; i >= 0; i--)
        {
            var o = orbs[i];
            float age = now - o.born;
            if (age < o.delay)
            {
                // 튀어나와서 둥실 떠 있음
                o.vel = Vector3.Lerp(o.vel, Vector3.up * h * 0.15f, dt * 4f);
            }
            else
            {
                // 거인 쪽으로 점점 빨라지며 빨려 들어감 (살짝 나선을 그림)
                float t = age - o.delay;
                Vector3 to = tgt - o.pos;
                float dist = to.magnitude;
                float speed = h * (1.5f + t * t * 9f);
                if (dist < Mathf.Max(h * 0.08f, speed * dt * 1.2f) || t > 3.5f)
                {
                    Grant(o.xp);
                    arrived++;
                    sparkPS.Emit(new ParticleSystem.EmitParams { position = tgt + Random.insideUnitSphere * h * 0.04f, velocity = Random.insideUnitSphere * h * 0.3f, startSize = o.size * 1.6f, startLifetime = 0.35f, startColor = Core }, 1);
                    orbs.RemoveAt(i);
                    continue;
                }
                Vector3 dir = to / dist;
                Vector3 swirl = Vector3.Cross(dir, Vector3.up).normalized * Mathf.Sin(o.seed + t * 7f) * Mathf.Clamp01(dist / h) * 0.6f;
                o.vel = Vector3.Lerp(o.vel, (dir + swirl).normalized * speed, dt * (4f + t * 10f));
            }
            o.pos += o.vel * dt;
            orbs[i] = o;

            // 꼬리: 지나온 자리에 작은 반짝임
            if (Random.value < 0.6f)
                sparkPS.Emit(new ParticleSystem.EmitParams { position = o.pos, velocity = Vector3.zero, startSize = o.size * 0.55f, startLifetime = 0.3f, startColor = Halo }, 1);

            float pulse = 1f + Mathf.Sin(now * 12f + o.seed) * 0.12f;
            float pop = Mathf.Clamp01(age / 0.12f); // 나타날 때 커지며 등장
            buf[count++] = new ParticleSystem.Particle { position = o.pos, startSize = o.size * 2.2f * pulse * pop, startColor = Halo, remainingLifetime = 1f, startLifetime = 1f };
            buf[count++] = new ParticleSystem.Particle { position = o.pos, startSize = o.size * pulse * pop, startColor = Core, remainingLifetime = 1f, startLifetime = 1f };
        }
        orbPS.SetParticles(buf, count);
        if (arrived > 0) absorbFlash = Mathf.Min(1f, absorbFlash + arrived * 0.15f);
        absorbFlash = Mathf.MoveTowards(absorbFlash, 0f, dt * 2.5f);
        if (absorbFlash > 0.05f && Random.value < absorbFlash)
            sparkPS.Emit(new ParticleSystem.EmitParams { position = tgt + Random.insideUnitSphere * h * 0.12f, velocity = Vector3.up * h * 0.2f, startSize = h * 0.08f * absorbFlash, startLifetime = 0.4f, startColor = Halo }, 1);
    }

    float absorbFlash;

    static void Grant(int xp) { if (LevelSystem.Instance) LevelSystem.Instance.AddXp(xp); }
}
