using System.Collections.Generic;
using UnityEngine;

// 작살 줄 (루프건): 작살병이 쏜 작살이 거인 몸의 맞은 부위(뼈)에 꽂히고, 줄이 땅에 박힌 말뚝까지 이어짐.
// 꽂힌 줄이 많을수록 거인이 느려짐(SpeedFactor → GiantController). 거인이 말뚝에서 멀리 떨어지거나 말뚝을 밟으면 줄이 끊겨 사라짐.
// 빗나간 작살은 땅에 떨어졌다가 줄과 함께 사라짐. 거인이 죽거나 집에 들어가면 줄이 모두 끊어짐.
public class GiantRopes : MonoBehaviour
{
    // 거인 이동 속도 배율 = 줄에 묶인 만큼 × 얼음 주문(Chill, 마법사)에 걸린 동안
    public static float SpeedFactor => ropeFactor * (Chilled ? chillFactor : 1f);
    public static bool Chilled => Time.time < chillUntil && chillUntil - Time.time < 60f; // 이전 플레이의 값이 남아 있으면 무시
    static float ropeFactor = 1f, chillUntil, chillFactor = 1f;

    // 얼음 주문: seconds초 동안 속도 × factor (겹치면 더 느린 쪽, 더 긴 쪽)
    public static void Chill(float factor, float seconds)
    {
        chillFactor = Chilled ? Mathf.Min(chillFactor, factor) : factor;
        chillUntil = Mathf.Max(Chilled ? chillUntil : 0f, Time.time + seconds);
    }
    public static int Attached { get; private set; }

    [Tooltip("줄 하나당 느려지는 정도: 속도 = 1 / (1 + 이 값 × 줄 수)")] public float slowPerRope = 0.2f;
    [Tooltip("아무리 많이 묶여도 이 배율보다 느려지지 않음")] public float minSpeedFactor = 0.25f;
    [Tooltip("꽂혔을 때 길이에서 거인 키 × 이 값만큼 더 멀어지면 줄이 끊어짐")] public float breakSlackPerHeight = 2.5f;
    [Tooltip("동시에 꽂혀 있을 수 있는 줄 수 (넘으면 가장 오래된 줄이 끊어짐)")] public int maxRopes = 12;
    public float hitDamage = 3f;

    static GiantRopes inst;
    public enum State { Flying, Attached, Missed, Snapped }
    // 줄 하나. 쏜 병사(holder)가 줄 끝을 잡고 있다가 PlantStake로 땅에 말뚝을 세워 묶고 DriveStake로 박음
    public class Rope
    {
        public State state;
        public Transform harpoon, stake, bone, holder;
        public LineRenderer line;
        public Vector3 anchor, pos, vel, missPoint, dir, holdPoint;
        public float speed, restLen, t0, len, stakeK;
        public bool held;
        public bool Flying => state == State.Flying;
        public bool Hooked => state == State.Attached;
        public bool Done => state == State.Missed || state == State.Snapped;
    }
    readonly List<Rope> ropes = new List<Rope>();
    static readonly HumanBodyBones[] HitBones =
    {
        HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips, HumanBodyBones.UpperChest,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
    };
    Material shaftMat, tipMat;

    void OnDestroy() { if (inst == this) { inst = null; ropeFactor = 1f; Attached = 0; } }

    static float Ground(Vector3 p) => WildTerrain.Active ? WildTerrain.Height(p.x, p.z) : 0f;
    static float GiantHeight(GiantHealth g) => Mathf.Max(2f, (g.ShoulderY - g.transform.position.y) / 0.837f);

    // 작살 발사: muzzle에서 거인 쪽으로. 줄 끝은 쏜 병사(holder)의 holdPoint(병사 기준 좌표)에 붙어 따라다님. hit이면 맞은 부위에 꽂힘
    public static Rope Fire(Vector3 muzzle, Transform holder, Vector3 holdPoint, bool hit, float speed)
    {
        var g = GiantHealth.Instance;
        var ctrl = GiantController.Instance;
        if (!g || g.IsDead || !ctrl) return null;
        if (!inst) inst = new GameObject("GiantRopes").AddComponent<GiantRopes>();
        return inst.Launch(muzzle, holder, holdPoint, hit, speed, g, ctrl);
    }

    // 병사가 잡고 있던 줄 끝을 p(땅)에 세운 말뚝에 묶음. 말뚝은 처음엔 덜 박힌 채로 솟아 있음 (DriveStake로 박음)
    public static void PlantStake(Rope r, Vector3 p)
    {
        if (!inst || r == null || r.stake || r.state == State.Snapped) return;
        float k = r.stakeK;
        p.y = Ground(p);
        r.stake = inst.Part(null, PrimitiveType.Cube, new Vector3(0.35f, 1.4f, 0.35f) * k, inst.shaftMat ? inst.shaftMat : (inst.shaftMat = ShaftMaterial()));
        r.stake.rotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
        r.held = false; r.holder = null;
        r.anchor = p; // DriveStake가 말뚝 위치·줄 매듭을 정함
        DriveStake(r, 0f);
        if (r.state == State.Attached) r.restLen = Vector3.Distance(inst.Tail(r), r.anchor);
    }

    // 말뚝 박기 진행 (0 = 막 세움, 1 = 다 박힘)
    public static void DriveStake(Rope r, float u)
    {
        if (r == null || !r.stake) return;
        float k = r.stakeK;
        Vector3 b = r.stake.position; b.y = 0f;
        if (u <= 0f) b = new Vector3(r.anchor.x, 0f, r.anchor.z);
        float gy = Ground(b);
        r.stake.position = new Vector3(b.x, gy + k * Mathf.Lerp(1.3f, 0.5f, Mathf.Clamp01(u)), b.z);
        r.anchor = r.stake.position + Vector3.up * 0.6f * k;
    }

    // 쏜 병사가 줄을 묶기 전에 죽음: 줄이 풀려 끊어짐
    public static void Release(Rope r)
    {
        if (inst && r != null && r.held) inst.Snap(r);
    }

    Rope Launch(Vector3 muzzle, Transform holder, Vector3 holdPoint, bool hit, float speed, GiantHealth g, GiantController ctrl)
    {
        float h = GiantHeight(g);
        var anim = ctrl.GetComponentInChildren<Animator>();
        Transform bone = null;
        if (anim && anim.isHuman)
            for (int k = 0; k < 6 && !bone; k++) bone = anim.GetBoneTransform(HitBones[Random.Range(0, HitBones.Length)]);
        Vector3 aim = bone ? bone.position : g.GetAimPoint();
        var r = new Rope { state = State.Flying, bone = hit ? bone : null, pos = muzzle, speed = speed, t0 = Time.time, len = Mathf.Max(1.2f, h * 0.14f) };
        r.missPoint = aim + (Random.onUnitSphere + Vector3.up * 0.3f).normalized * h * Random.Range(0.25f, 0.45f);
        if (hit && !bone) { r.bone = g.transform; } // 사람 모양 뼈가 없으면 몸 가운데
        r.dir = ((hit ? aim : r.missPoint) - muzzle).normalized;

        // 줄 끝: 말뚝에 묶기 전까지는 쏜 병사의 총에
        r.stakeK = Mathf.Max(1f, h / 18f);
        r.holder = holder; r.holdPoint = holdPoint; r.held = holder;
        r.anchor = holder ? holder.TransformPoint(holdPoint) : muzzle;

        // 작살: 자루 + 촉 (뿌리 = 자루 가운데, 촉은 앞쪽 +Z)
        var root = new GameObject("Harpoon").transform;
        Part(root, PrimitiveType.Cube, new Vector3(r.len * 0.05f, r.len * 0.05f, r.len), shaftMat ? shaftMat : (shaftMat = ShaftMaterial()));
        var tip = Part(root, PrimitiveType.Cube, new Vector3(r.len * 0.12f, r.len * 0.12f, r.len * 0.18f), tipMat ? tipMat : (tipMat = TipMaterial()));
        tip.localPosition = Vector3.forward * r.len * 0.55f; tip.localRotation = Quaternion.Euler(0f, 0f, 45f);
        root.position = muzzle; root.rotation = Quaternion.LookRotation(r.dir);
        r.harpoon = root;

        // 줄
        var lgo = new GameObject("HarpoonRope");
        r.line = lgo.AddComponent<LineRenderer>();
        r.line.sharedMaterial = WarningZone.material;
        r.line.positionCount = 12;
        r.line.startWidth = r.line.endWidth = Mathf.Max(0.15f, h * 0.011f);
        Color c = GameEra.Medieval ? new Color(0.55f, 0.42f, 0.26f) : new Color(0.35f, 0.36f, 0.38f);
        r.line.startColor = r.line.endColor = c;
        r.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.line.receiveShadows = false;
        ropes.Add(r);
        return r;
    }

    Transform Part(Transform parent, PrimitiveType type, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        if (parent) go.transform.SetParent(parent, false);
        go.transform.localScale = size;
        var mr = go.GetComponent<MeshRenderer>();
        if (mat) mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    static Material ShaftMaterial()
    {
        var fx = CombatFX.Instance;
        if (GameEra.Medieval && fx && fx.javelinMaterial) return fx.javelinMaterial;
        if (fx && fx.shellMaterial) return fx.shellMaterial;
        return null;
    }
    static Material TipMaterial()
    {
        var fx = CombatFX.Instance;
        if (GameEra.Medieval && fx && fx.boulderMaterial) return fx.boulderMaterial;
        if (fx && fx.rocketMaterial) return fx.rocketMaterial;
        return null;
    }

    Vector3 Tail(Rope r) => r.harpoon ? r.harpoon.position - r.harpoon.forward * r.len * 0.5f : r.pos;

    void Update()
    {
        var g = GiantHealth.Instance;
        bool release = !g || g.IsDead || GiantHome.GiantSafe || GameStartMenu.InMenu;
        float dt = Time.deltaTime, h = g ? GiantHeight(g) : 18f;
        float squash = (CivilianManager.Instance ? CivilianManager.Instance.squashRadius : 0.3f) * (GiantController.Instance ? GiantController.Instance.GameScale : 27f);
        int attached = 0;
        for (int i = ropes.Count - 1; i >= 0; i--)
        {
            var r = ropes[i];
            // 병사가 잡고 있는 줄: 끝이 총을 따라감 (병사가 사라지면 풀림)
            if (r.held)
            {
                if (r.holder) r.anchor = r.holder.TransformPoint(r.holdPoint);
                else if (r.state != State.Snapped) Snap(r);
            }
            switch (r.state)
            {
                case State.Flying:
                {
                    if (release) { Snap(r); break; }
                    Vector3 tgt = r.bone ? r.bone.position : r.missPoint;
                    Vector3 to = tgt - r.pos;
                    float step = r.speed * dt;
                    if (to.magnitude <= step + 0.01f)
                    {
                        if (r.bone)
                        {
                            // 꽂힘: 촉이 맞은 부위 한가운데에 박히고 자루는 쏜 쪽으로 튀어나옴
                            r.dir = r.dir.sqrMagnitude > 0.01f ? r.dir : to.normalized;
                            r.harpoon.rotation = Quaternion.LookRotation(r.dir);
                            r.harpoon.position = tgt - r.dir * r.len * 0.5f;
                            r.harpoon.SetParent(r.bone, true);
                            r.state = State.Attached;
                            r.restLen = Vector3.Distance(Tail(r), r.anchor);
                            g.TakeDamage(hitDamage, false);
                            CombatFX.Muzzle(tgt, -r.dir, 1.6f);
                        }
                        else { r.state = State.Missed; r.vel = r.dir * r.speed * 0.25f; r.t0 = Time.time; }
                    }
                    else
                    {
                        r.dir = to.normalized;
                        r.pos += r.dir * step;
                        r.harpoon.position = r.pos;
                        r.harpoon.rotation = Quaternion.LookRotation(r.dir);
                    }
                    break;
                }
                case State.Attached:
                {
                    if (!r.harpoon || !r.bone) { Snap(r); break; }
                    float d = Vector3.Distance(Tail(r), r.anchor);
                    Vector3 sp = r.stake ? r.stake.position : r.anchor; sp.y = 0f;
                    Vector3 gp = g ? g.transform.position : sp; gp.y = 0f;
                    // 너무 멀어지거나, 거인이 말뚝을 밟으면 끊어짐
                    if (release || d > r.restLen + h * breakSlackPerHeight || Vector3.Distance(sp, gp) < squash) { Snap(r); break; }
                    attached++;
                    break;
                }
                case State.Missed:
                {
                    if (!r.harpoon) { Remove(i); continue; }
                    float gy = Ground(r.pos);
                    if (r.pos.y > gy + 0.2f)
                    {
                        r.vel += Vector3.down * 30f * dt;
                        r.pos += r.vel * dt;
                        r.harpoon.position = r.pos;
                        if (r.vel.sqrMagnitude > 1f) r.harpoon.rotation = Quaternion.LookRotation(r.vel);
                    }
                    if (Time.time - r.t0 > 1.6f) { Snap(r); }
                    break;
                }
                case State.Snapped:
                {
                    float u = (Time.time - r.t0) / 0.4f;
                    if (r.harpoon && r.harpoon.parent == null)
                    {
                        r.vel += Vector3.down * 30f * dt;
                        r.pos += r.vel * dt;
                        if (r.pos.y < Ground(r.pos)) r.pos.y = Ground(r.pos);
                        r.harpoon.position = r.pos;
                    }
                    if (u >= 1f && Time.time - r.t0 > 1.2f) { Remove(i); continue; }
                    break;
                }
            }
            if (i < ropes.Count && ropes[i] == r) DrawRope(r, h);
        }

        // 너무 많으면 가장 오래 꽂혀 있던 줄부터 끊음
        if (attached > maxRopes)
            foreach (var r in ropes) if (r.state == State.Attached) { Snap(r); attached--; if (attached <= maxRopes) break; }

        Attached = attached;
        ropeFactor = Mathf.Max(minSpeedFactor, 1f / (1f + slowPerRope * attached));
    }

    void Snap(Rope r)
    {
        if (r.state == State.Snapped) return;
        r.state = State.Snapped;
        r.t0 = Time.time;
        if (r.harpoon)
        {
            // 작살이 빠져서 떨어짐
            r.harpoon.SetParent(null, true);
            r.pos = r.harpoon.position;
            r.vel = (Random.onUnitSphere + Vector3.up) * 4f;
        }
    }

    void DrawRope(Rope r, float h)
    {
        if (!r.line) return;
        Vector3 a = r.state == State.Flying || r.state == State.Missed ? Tail(r) : Tail(r);
        Vector3 b = r.anchor;
        int n = r.line.positionCount;
        float d = Vector3.Distance(a, b);
        float sag, fade = 1f;
        if (r.state == State.Attached)
        {
            float tension = Mathf.Clamp01((d - r.restLen * 0.6f) / Mathf.Max(0.1f, r.restLen * 0.4f + h * breakSlackPerHeight));
            sag = d * 0.12f * (1f - tension); // 팽팽해질수록 곧게
        }
        else if (r.state == State.Snapped)
        {
            // 끊긴 줄: 말뚝 쪽으로 오그라들며 흐려짐
            float u = Mathf.Clamp01((Time.time - r.t0) / 0.4f);
            a = Vector3.Lerp(a, b, u);
            sag = d * 0.25f * u;
            fade = 1f - u;
        }
        else sag = d * 0.05f;
        for (int k = 0; k < n; k++)
        {
            float t = k / (float)(n - 1);
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1f - t);
            float gy = Ground(p) + 0.1f;
            if (p.y < gy) p.y = gy;
            r.line.SetPosition(k, p);
        }
        Color c = r.line.startColor; c.a = fade;
        r.line.startColor = r.line.endColor = c;
    }

    void Remove(int i)
    {
        var r = ropes[i];
        if (r.harpoon) Destroy(r.harpoon.gameObject);
        if (r.stake) Destroy(r.stake.gameObject);
        if (r.line) Destroy(r.line.gameObject);
        ropes.RemoveAt(i);
    }
}
