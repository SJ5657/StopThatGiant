using System.Collections.Generic;
using UnityEngine;

// 마을 밖 야생동물·몬스터 (상자 조각 모델, Wilderness가 만듦).
//  - 사슴·멧돼지·늑대·양·소: 풀을 뜯거나 집(무리 자리) 근처를 거닐다가 거인이 가까이 오면 도망
//  - 고블린: 거인이 가까이 오면 몰려와 창을 던짐 (너무 가까우면 흩어짐)
//  - 트롤: 큰 덩치로 쿵쿵 다가와 바위를 던짐
//  - 슬라임: 통통 튀어 다니다 거인에게 달라붙어 조금씩 피해
// 모두 거인 발에 밟히면 죽음 (GiantStomp → Crush). 거인과 멀면(activeRange 밖) 아무것도 안 함.
public class WildCreature : MonoBehaviour
{
    public enum Kind { Deer, Boar, Wolf, Sheep, Cow, Goblin, Troll, Slime }
    public Kind kind;
    public bool IsDead { get; private set; }

    public static readonly List<WildCreature> All = new List<WildCreature>();
    const float ActiveRange = 900f;

    Vector3 home, target;
    float homeRadius, speed, nextThink, nextAttack, animT, hop;
    bool fleeing;
    Transform model;
    readonly List<Transform> legs = new List<Transform>();
    readonly List<Transform> arms = new List<Transform>();
    static Material baseMat;
    static readonly Dictionary<Color32, Material> mats = new Dictionary<Color32, Material>();

    struct Stats { public float walk, run, flee, aggro, keep, hp; public int score; public string label; }
    Stats st;

    static Stats Of(Kind k)
    {
        switch (k)
        {
            case Kind.Deer: return new Stats { walk = 8f, run = 34f, flee = 160f, score = 40, label = "사슴" };
            case Kind.Boar: return new Stats { walk = 7f, run = 26f, flee = 110f, score = 50, label = "멧돼지" };
            case Kind.Wolf: return new Stats { walk = 10f, run = 38f, flee = 140f, score = 60, label = "늑대" };
            case Kind.Sheep: return new Stats { walk = 4f, run = 16f, flee = 80f, score = 20, label = "양" };
            case Kind.Cow: return new Stats { walk = 3f, run = 12f, flee = 70f, score = 30, label = "소" };
            case Kind.Goblin: return new Stats { walk = 9f, run = 24f, flee = 30f, aggro = 260f, keep = 90f, score = 80, label = "고블린" };
            case Kind.Troll: return new Stats { walk = 9f, run = 15f, aggro = 330f, keep = 110f, score = 300, label = "트롤" };
            default: return new Stats { walk = 6f, run = 18f, aggro = 140f, keep = 0f, score = 40, label = "슬라임" };
        }
    }

    public static void SetBaseMaterial(Material m) { baseMat = m; mats.Clear(); }

    static Material Mat(Color c)
    {
        var key = (Color32)c;
        if (!mats.TryGetValue(key, out var m))
        {
            m = new Material(baseMat) { color = c, enableInstancing = true };
            mats[key] = m;
        }
        return m;
    }

    // ───────────── 만들기 ─────────────
    public static WildCreature Create(Kind k, Vector3 pos, float homeRadius, Transform parent, System.Random r)
    {
        var go = new GameObject(k.ToString());
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f));
        var c = go.AddComponent<WildCreature>();
        c.kind = k; c.st = Of(k);
        c.home = pos; c.homeRadius = homeRadius; c.target = pos;
        c.nextThink = Time.time + (float)r.NextDouble() * 4f;
        c.animT = (float)r.NextDouble() * 10f;
        c.model = new GameObject("Model").transform;
        c.model.SetParent(go.transform, false);
        Vector3 size = c.Build((float)r.NextDouble());
        // 거인 발에 밟히도록 Military 레이어 + 충돌 상자
        int layer = LayerMask.NameToLayer("Military");
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        var box = go.AddComponent<BoxCollider>();
        box.size = size; box.center = new Vector3(0f, size.y * 0.5f, 0f);
        All.Add(c);
        return c;
    }

    void OnDestroy() { All.Remove(this); }

    Transform Cube(Transform parent, Vector3 pos, Vector3 size, Color col, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = size;
        var mr = g.GetComponent<MeshRenderer>();
        mr.sharedMaterial = Mat(col);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return g.transform;
    }

    // 다리: 엉덩이 높이의 회전축 아래로 뻗은 상자 (걸을 때 앞뒤로 흔듦)
    void Leg(Vector3 hip, float len, float thick, Color col)
    {
        var pivot = new GameObject("Leg").transform;
        pivot.SetParent(model, false); pivot.localPosition = hip;
        Cube(pivot, new Vector3(0f, -len / 2, 0f), new Vector3(thick, len, thick), col);
        legs.Add(pivot);
    }

    void Arm(Vector3 shoulder, float len, float thick, Color col)
    {
        var pivot = new GameObject("Arm").transform;
        pivot.SetParent(model, false); pivot.localPosition = shoulder;
        Cube(pivot, new Vector3(0f, -len / 2, 0f), new Vector3(thick, len, thick), col);
        arms.Add(pivot);
    }

    // 네발짐승 공통: 몸통·목·머리·네 다리·꼬리 (앞 = +Z). s = 크기 배율
    void Quadruped(float s, Color body, Color dark, float legLen, Vector3 bodySize, float neckTilt, Vector3 headSize)
    {
        float hipY = legLen * s;
        Cube(model, new Vector3(0f, hipY + bodySize.y * s * 0.5f, 0f), bodySize * s, body);
        float bz = bodySize.z * s * 0.5f, bx = bodySize.x * s * 0.3f;
        foreach (var x in new[] { -bx, bx })
            foreach (var z in new[] { -bz * 0.75f, bz * 0.75f }) Leg(new Vector3(x, hipY, z), legLen * s, 0.24f * s, dark);
        Vector3 neckPos = new Vector3(0f, hipY + bodySize.y * s * 0.9f, bz * 0.95f);
        Cube(model, neckPos, new Vector3(0.4f, 0.9f, 0.45f) * s, body, new Vector3(neckTilt, 0f, 0f));
        Vector3 headPos = neckPos + new Vector3(0f, 0.45f * s, 0.45f * s);
        Cube(model, headPos, headSize * s, body, new Vector3(15f, 0f, 0f));
        Cube(model, new Vector3(0f, hipY + bodySize.y * s * 0.6f, -bz - 0.1f * s), new Vector3(0.12f, 0.5f, 0.12f) * s, dark, new Vector3(-30f, 0f, 0f));
        headTop = headPos + Vector3.up * headSize.y * s * 0.5f;
    }
    Vector3 headTop;

    // 사람형 (고블린·트롤): 다리 둘, 몸통, 머리, 팔 둘
    void Humanoid(float s, Color skin, Color cloth, float stockiness)
    {
        float legLen = 1.0f * s, hipY = legLen;
        foreach (var x in new[] { -0.22f * s, 0.22f * s }) Leg(new Vector3(x, hipY, 0f), legLen, 0.3f * s, cloth);
        float bodyH = 1.1f * s;
        Cube(model, new Vector3(0f, hipY + bodyH / 2, 0f), new Vector3(0.75f * stockiness, 1.1f, 0.5f * stockiness) * s, cloth);
        Cube(model, new Vector3(0f, hipY + bodyH + 0.3f * s, 0.05f * s), new Vector3(0.55f, 0.55f, 0.55f) * s, skin);
        foreach (var x in new[] { -0.5f * stockiness * s, 0.5f * stockiness * s }) Arm(new Vector3(x, hipY + bodyH * 0.9f, 0f), 1.0f * s, 0.24f * s, skin);
        headTop = new Vector3(0f, hipY + bodyH + 0.6f * s, 0f);
    }

    // 종류마다 모델 (v = 0~1 무작위로 색·크기를 조금씩). 충돌 상자 크기를 돌려줌
    Vector3 Build(float v)
    {
        switch (kind)
        {
            case Kind.Deer:
            {
                Color fur = Color.Lerp(new Color(0.62f, 0.40f, 0.22f), new Color(0.70f, 0.50f, 0.30f), v), dark = new Color(0.40f, 0.26f, 0.15f);
                float s = 2.2f;
                Quadruped(s, fur, dark, 1.1f, new Vector3(0.7f, 0.75f, 1.7f), 30f, new Vector3(0.35f, 0.4f, 0.75f));
                if (v > 0.4f) foreach (var x in new[] { -0.25f, 0.25f }) // 수컷 뿔
                    Cube(model, headTop + new Vector3(x * s, 0.35f * s, 0f), new Vector3(0.08f, 0.7f, 0.08f) * s, new Color(0.85f, 0.8f, 0.7f), new Vector3(0f, 0f, x > 0 ? -25f : 25f));
                return new Vector3(1.6f, 5f, 4.5f);
            }
            case Kind.Boar:
            {
                Color fur = Color.Lerp(new Color(0.30f, 0.22f, 0.16f), new Color(0.40f, 0.30f, 0.22f), v);
                float s = 2.2f;
                Quadruped(s, fur, fur * 0.8f, 0.55f, new Vector3(0.9f, 0.85f, 1.6f), 70f, new Vector3(0.6f, 0.55f, 0.7f));
                foreach (var x in new[] { -0.18f, 0.18f }) Cube(model, headTop + new Vector3(x * s, -0.35f * s, 0.4f * s), new Vector3(0.08f, 0.08f, 0.35f) * s, new Color(0.95f, 0.92f, 0.85f));
                return new Vector3(2.2f, 3.5f, 4.5f);
            }
            case Kind.Wolf:
            {
                Color fur = Color.Lerp(new Color(0.48f, 0.48f, 0.50f), new Color(0.62f, 0.60f, 0.58f), v);
                float s = 2.1f;
                Quadruped(s, fur, fur * 0.75f, 0.8f, new Vector3(0.55f, 0.6f, 1.5f), 40f, new Vector3(0.4f, 0.4f, 0.8f));
                foreach (var x in new[] { -0.14f, 0.14f }) Cube(model, headTop + new Vector3(x * s, 0.12f * s, -0.15f * s), new Vector3(0.1f, 0.25f, 0.1f) * s, fur * 0.75f);
                return new Vector3(1.4f, 4f, 4f);
            }
            case Kind.Sheep:
            {
                float s = 2.2f;
                Quadruped(s, new Color(0.95f, 0.94f, 0.90f), new Color(0.18f, 0.17f, 0.17f), 0.55f, new Vector3(1.0f, 0.85f, 1.3f), 50f, new Vector3(0.35f, 0.4f, 0.55f));
                return new Vector3(2.3f, 3.6f, 3.6f);
            }
            case Kind.Cow:
            {
                bool spotted = v > 0.5f;
                Color hide = spotted ? new Color(0.94f, 0.93f, 0.90f) : new Color(0.55f, 0.36f, 0.24f);
                float s = 2.5f;
                Quadruped(s, hide, spotted ? new Color(0.15f, 0.14f, 0.14f) : hide * 0.75f, 0.8f, new Vector3(1.0f, 1.0f, 2.0f), 60f, new Vector3(0.5f, 0.5f, 0.7f));
                if (spotted) Cube(model, new Vector3(0.2f * s, 2.0f * s, 0.1f * s), new Vector3(0.62f, 0.6f, 0.8f) * s, new Color(0.15f, 0.14f, 0.14f));
                return new Vector3(2.6f, 5f, 6f);
            }
            case Kind.Goblin:
            {
                float s = 2.3f;
                Color skin = Color.Lerp(new Color(0.35f, 0.62f, 0.25f), new Color(0.48f, 0.66f, 0.30f), v);
                Humanoid(s, skin, new Color(0.42f, 0.30f, 0.20f), 0.9f);
                foreach (var x in new[] { -0.36f, 0.36f }) Cube(model, headTop + new Vector3(x * s, -0.25f * s, 0f), new Vector3(0.3f, 0.12f, 0.12f) * s, skin, new Vector3(0f, 0f, x > 0 ? 20f : -20f)); // 뾰족한 귀
                if (arms.Count > 0) Cube(arms[1], new Vector3(0f, -1.0f * s, 0.4f * s), new Vector3(0.08f, 0.08f, 2.2f) * s, new Color(0.45f, 0.32f, 0.2f)); // 창
                return new Vector3(2f, 7f, 2f);
            }
            case Kind.Troll:
            {
                float s = 5.5f;
                Color skin = Color.Lerp(new Color(0.45f, 0.52f, 0.42f), new Color(0.55f, 0.55f, 0.48f), v);
                Humanoid(s, skin, new Color(0.35f, 0.28f, 0.22f), 1.3f);
                if (arms.Count > 0) Cube(arms[1], new Vector3(0f, -1.1f * s, 0.2f * s), new Vector3(0.3f, 1.2f, 0.3f) * s, new Color(0.40f, 0.29f, 0.19f)); // 몽둥이
                return new Vector3(6f, 16f, 5f);
            }
            default:
            {
                Color[] cols = { new Color(0.35f, 0.75f, 0.35f), new Color(0.35f, 0.55f, 0.95f), new Color(0.9f, 0.4f, 0.6f), new Color(0.95f, 0.75f, 0.25f) };
                Color c = cols[Mathf.Min(cols.Length - 1, (int)(v * cols.Length))];
                float s = 3.5f + v * 2f;
                Cube(model, new Vector3(0f, s * 0.4f, 0f), new Vector3(s, s * 0.8f, s), c);
                Cube(model, new Vector3(0f, s * 0.85f, 0f), new Vector3(s * 0.7f, s * 0.2f, s * 0.7f), c);
                foreach (var x in new[] { -0.2f, 0.2f }) Cube(model, new Vector3(x * s, s * 0.55f, s * 0.5f), new Vector3(0.12f, 0.2f, 0.05f) * s, new Color(0.1f, 0.1f, 0.12f)); // 눈
                return new Vector3(s, s, s);
            }
        }
    }

    // ───────────── 움직임 ─────────────
    static bool AttacksBlocked(GiantHealth g) => !g || g.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || EnemyFreeze.Active || GiantShrink.Active;

    void Update()
    {
        if (IsDead || GameStartMenu.InMenu) return;
        var g = GiantHealth.Instance;
        if (!g) return;
        Vector3 pos = transform.position, gp = g.transform.position;
        Vector3 toG = gp - pos; toG.y = 0f;
        float d = toG.magnitude;
        if (d > ActiveRange) return; // 멀면 쉼 (CPU 절약)
        if (EnemyFreeze.Active) return;
        float dt = Time.deltaTime;
        bool monster = kind == Kind.Goblin || kind == Kind.Troll || kind == Kind.Slime;
        float moveSpeed = 0f;

        if (!monster || kind == Kind.Goblin)
        {
            // 도망: 거인 반대쪽으로 (동물은 멀리서부터, 고블린은 바짝 붙었을 때만 흩어짐)
            if (d < st.flee)
            {
                fleeing = true;
                Vector3 away = -toG.normalized;
                target = pos + Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * away * 60f;
                nextThink = Time.time + 1.5f;
            }
            else if (fleeing && d > st.flee * 1.6f) { fleeing = false; home = pos; }
        }
        if (!fleeing)
        {
            if (monster && d < st.aggro && !AttacksBlocked(g))
            {
                // 다가가서 일정 거리 유지 (슬라임은 바짝 붙음)
                target = d > st.keep ? gp - toG.normalized * st.keep : pos;
                Attack(g, d, toG);
            }
            else if (Time.time >= nextThink)
            {
                // 무리 자리 근처를 거닐거나 잠깐 멈춰 풀을 뜯음
                nextThink = Time.time + Random.Range(3f, 8f);
                target = Random.value < 0.35f ? pos : home + Random.insideUnitSphere.Flat() * homeRadius;
            }
        }

        Vector3 to = target - pos; to.y = 0f;
        float dist = to.magnitude;
        if (dist > 1.5f)
        {
            moveSpeed = fleeing || (monster && d < st.aggro) ? st.run : st.walk;
            var want = Quaternion.LookRotation(to);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 240f * dt);
            pos += transform.forward * Mathf.Min(dist, moveSpeed * dt);
            pos.y = WildTerrain.Active ? WildTerrain.Height(pos.x, pos.z) : 0f; // 언덕을 따라 오르내림
            transform.position = pos;
        }
        else if (monster && d < st.aggro && toG.sqrMagnitude > 1f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toG), 240f * dt); // 거인 쪽을 봄

        Animate(moveSpeed, dt);
    }

    void Attack(GiantHealth g, float d, Vector3 toG)
    {
        if (Time.time < nextAttack) return;
        switch (kind)
        {
            case Kind.Goblin:
                if (d > 200f) return;
                nextAttack = Time.time + Random.Range(2.2f, 3.6f);
                Vector3 from = transform.position + Vector3.up * 5f, aim = g.GetAimPoint();
                bool hit = Random.value < 0.6f;
                FlyingArrow.Shoot(from, hit ? aim : aim + Random.onUnitSphere * 12f);
                if (hit) g.TakeDamage(4f, false);
                if (arms.Count > 1) swing = 1f;
                break;
            case Kind.Troll:
                if (d > 260f) return;
                nextAttack = Time.time + Random.Range(4.5f, 6.5f);
                Vector3 hand = transform.position + Vector3.up * 13f + transform.forward * 3f;
                Vector3 tgt = g.GetAimPoint() + g.Velocity * (d / 95f) * 0.6f;
                CombatFX.Spawn(CombatFX.Kind.Shell, hand, (tgt - hand).normalized, 95f, 35f, 0f);
                swing = 1f;
                break;
            default: // 슬라임: 몸에 닿아 있으면 조금씩 피해
                if (!g.IsTouching(transform.position + Vector3.up * 2f, 6f)) return;
                nextAttack = Time.time + 1f;
                g.TakeDamage(4f, false);
                break;
        }
    }

    float swing;

    void Animate(float moveSpeed, float dt)
    {
        if (!model) return;
        float rate = moveSpeed > 0.5f ? Mathf.Clamp(moveSpeed * 0.35f, 2f, 10f) : 0f;
        animT += dt * rate;
        if (kind == Kind.Slime)
        {
            // 통통 튐: 뛸 때 위로 솟고 땅에 닿을 때 납작
            hop += dt * (moveSpeed > 0.5f ? 2.2f : 0.8f);
            float h = Mathf.Abs(Mathf.Sin(hop * Mathf.PI));
            model.localPosition = Vector3.up * h * (moveSpeed > 0.5f ? 4f : 0.6f);
            float squash = 1f - (1f - h) * 0.25f;
            model.localScale = new Vector3(1f / Mathf.Sqrt(squash), squash, 1f / Mathf.Sqrt(squash));
            return;
        }
        float amp = moveSpeed > 0.5f ? 35f : 0f;
        for (int i = 0; i < legs.Count; i++)
        {
            float phase = (i % 2 == 0 ? 0f : Mathf.PI) + (i >= 2 ? Mathf.PI : 0f);
            legs[i].localRotation = Quaternion.Euler(Mathf.Sin(animT + phase) * amp, 0f, 0f);
        }
        swing = Mathf.MoveTowards(swing, 0f, dt * 2.5f);
        for (int i = 0; i < arms.Count; i++)
        {
            float a = Mathf.Sin(animT + (i == 0 ? Mathf.PI : 0f)) * amp * 0.8f;
            if (i == 1 && swing > 0f) a = -150f * Mathf.Sin(swing * Mathf.PI); // 던지는 팔
            arms[i].localRotation = Quaternion.Euler(a, 0f, 0f);
        }
        // 풀 뜯기: 멈춰 있는 네발짐승은 가끔 고개를 숙임 → 몸을 살짝 앞으로 기울여 표현
        bool grazing = moveSpeed < 0.5f && kind != Kind.Goblin && kind != Kind.Troll && Mathf.Sin(Time.time * 0.7f + animT) > 0.3f;
        model.localRotation = Quaternion.Slerp(model.localRotation, Quaternion.Euler(grazing ? 8f : 0f, 0f, 0f), dt * 3f);
    }

    // ───────────── 밟힘 ─────────────
    public void Crush()
    {
        if (IsDead) return;
        IsDead = true;
        ScoreManager.Add(st.score, st.label, transform.position + Vector3.up * 2f);
        if (kind == Kind.Slime) CombatFX.Explosion(transform.position + Vector3.up * 2f, 6f);
        else BloodSplat.Spawn(transform.position, kind == Kind.Troll ? 3f : 1.2f);
        if (kind == Kind.Troll && GiantCamera.Instance) GiantCamera.Instance.Shake(0.4f);
        var sc = transform.localScale;
        transform.localScale = new Vector3(sc.x * 1.3f, sc.y * 0.12f, sc.z * 1.2f);
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        SinkAway.Begin(gameObject, 3f, kind == Kind.Troll ? 1.6f : 1f); // 잠시 뒤 땅속으로 가라앉아 사라짐
    }
}

static class FlatExt
{
    public static Vector3 Flat(this Vector3 v) { v.y = 0f; return v; }
}
