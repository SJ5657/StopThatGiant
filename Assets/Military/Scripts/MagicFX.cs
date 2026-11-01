using UnityEngine;

// 빛나는 마법 입자 (마법사 주문, UFO 광선). 불꽃 재질(가산)을 복사해 동그랗게 번지는 빛 텍스처를 씌운 파티클 하나를 공유.
// 깊이는 지키므로 건물 뒤에 있으면 가려짐 (경험치 구슬과 다름).
public class MagicFX : MonoBehaviour
{
    static MagicFX inst;
    ParticleSystem ps;

    static MagicFX Get()
    {
        if (inst) return inst;
        if (!CombatFX.Instance || !CombatFX.Instance.fireMaterial) return null;
        inst = new GameObject("MagicFX").AddComponent<MagicFX>();
        return inst;
    }

    void Awake()
    {
        var mat = new Material(CombatFX.Instance.fireMaterial) { name = "MagicGlow", mainTexture = XpOrbs.GlowTexture() };
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(0.6f, 0.6f, 0.6f, 0.6f)); // 불꽃의 주황 틴트를 빼서 입자 색 그대로
        ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 3000;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var c = ps.colorOverLifetime; c.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                   new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(0f, 1) });
        c.color = gr;
        var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.3f));
        var r = GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
    }

    void OnDestroy() { if (inst == this) inst = null; }

    // 얼음 주문에 걸린 동안 거인 몸 주위에 하늘색 서리 반짝임
    void Update()
    {
        var g = GiantHealth.Instance;
        if (!g || !GiantRopes.Chilled || EnemyFreeze.Active) return;
        float h = Mathf.Max(2f, g.Height);
        for (int i = 0; i < 3; i++)
        {
            Vector2 c = Random.insideUnitCircle * h * 0.18f;
            Vector3 p = g.transform.position + new Vector3(c.x, Random.Range(0f, h), c.y);
            ps.Emit(new ParticleSystem.EmitParams { position = p, velocity = Vector3.down * h * 0.08f, startColor = new Color(0.6f, 0.9f, 1f, 0.9f), startSize = h * Random.Range(0.02f, 0.05f), startLifetime = 0.8f }, 1);
        }
    }

    // 빛 알갱이 하나
    public static void Glow(Vector3 p, Vector3 vel, Color col, float size, float life)
    {
        var fx = Get(); if (!fx) return;
        if (life < 0.2f) life = Mathf.Max(life, Time.deltaTime * 2f); // 매 프레임 찍는 빛은 프레임이 느려도 끊기지 않게
        fx.ps.Emit(new ParticleSystem.EmitParams { position = p, velocity = vel, startColor = col, startSize = size, startLifetime = life }, 1);
    }

    // 사방으로 흩어지는 빛 (터짐)
    public static void Burst(Vector3 p, Color col, float size, int count = 18)
    {
        var fx = Get(); if (!fx) return;
        Glow(p, Vector3.zero, Color.Lerp(col, Color.white, 0.5f), size * 2.5f, 0.35f);
        for (int i = 0; i < count; i++)
            fx.ps.Emit(new ParticleSystem.EmitParams
            {
                position = p + Random.insideUnitSphere * size * 0.3f,
                velocity = Random.insideUnitSphere * size * 4f,
                startColor = col,
                startSize = size * Random.Range(0.3f, 0.8f),
                startLifetime = Random.Range(0.4f, 0.9f),
            }, 1);
    }
}
