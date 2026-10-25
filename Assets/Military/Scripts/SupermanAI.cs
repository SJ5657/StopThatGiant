using System.Collections.Generic;
using UnityEngine;

// 슈퍼맨: 거인 주변 하늘을 자유롭게 날아다니며, 잠깐 멈춰 거인 쪽을 보고 눈에서 레이저를 한 발씩 쏨(단발).
// 몇 발 쏜 뒤 기를 모아 거인 쪽으로 주먹을 뻗고 곧게 돌진함 (최대 dashCount번 연속, 마지막 돌진이 가장 빠름).
// 돌진 직전에 잠깐 예상 경로(빨간 선)가 보이고, 그 길은 바뀌지 않음 → 거인이 움직여 비켜나면 피함.
// 한 번이라도 맞으면 돌진을 멈추고 다시 레이저 → 몇 발 뒤 다시 돌진 (반복).
// 마지막 돌진까지 모두 피하면 슈퍼맨은 그대로 저 멀리 날아가 버리고, 한참 뒤(SupermanSpawner.respawnDelay) 다시 나타남.
// 모델은 Assets/Models/SuperMan.vrm (휴머노이드). 멈춰 있을 땐 허리에 손을 얹은 자세(애니메이션), 빨리 날 때는 몸을 눕히고 오른팔을 앞으로 뻗음.
public class SupermanAI : MonoBehaviour
{
    public static SupermanAI Active { get; private set; }

    [Header("크기 · 비행 (거인 키 기준)")]
    [Tooltip("슈퍼맨 키 = 거인 키 × 이 비율")] public float heightRatio = 0.3f;
    [Tooltip("날아다니는 범위: 거인과의 수평 거리 (최소, 최대)")] public Vector2 roamDistance = new Vector2(1.3f, 3.2f);
    [Tooltip("날아다니는 범위: 높이 (최소, 최대)")] public Vector2 roamHeight = new Vector2(0.5f, 1.8f);
    [Tooltip("최고 비행 속도 (거인 키/초)")] public float flySpeedRatio = 5.8f;
    [Tooltip("가속도 (거인 키/초²)")] public float accelRatio = 8f;

    [Header("레이저 (단발)")]
    [Tooltip("레이저를 쏘는 간격 (초, 범위)")] public Vector2 shotInterval = new Vector2(1.2f, 2.2f);
    [Tooltip("쏘기 전에 멈춰서 거인을 바라보는 시간(초)")] public float aimTime = 0.35f;
    public float boltDamage = 14f;
    [Tooltip("레이저 탄 속도 (거인 키/초). 느릴수록 움직여서 피하기 쉬움")] public float boltSpeedRatio = 9f;
    [Tooltip("돌진하기 전까지 쏘는 레이저 발 수 (최소, 최대). 돌진 공격이 끝날 때마다 새로 랜덤")] public Vector2Int shotsBeforeDash = new Vector2Int(0, 8);
    public Color laserColor = new Color(1f, 0.12f, 0.08f, 1f);

    [Header("돌진")]
    [Tooltip("연속 돌진 횟수 (모두 피하면 슈퍼맨이 날아가 버림)")] public int dashCount = 4;
    [Tooltip("첫 돌진 전 기를 모으는 시간(초)")] public float windUpTime = 0.9f;
    [Tooltip("돌진을 피한 뒤 다음 돌진 전 기를 모으는 시간(초)")] public float nextWindUpTime = 0.6f;
    [Tooltip("돌진 직전 예상 경로가 보이는 시간(초)")] public float routeShowTime = 0.3f;
    [Tooltip("돌진 속도 (거인 키/초)")] public float dashSpeedRatio = 12f;
    [Tooltip("마지막 돌진 속도 배율")] public float finalDashMultiplier = 1.8f;
    [Tooltip("돌진 경로를 거인이 지금 움직이는 쪽으로 얼마나 앞질러 잡을지 (0 = 지금 위치, 1 = 도착할 때 위치)")] public float dashLead = 0.6f;
    [Tooltip("거인을 지나쳐 더 날아가는 거리 (거인 키 비율)")] public float dashOvershootRatio = 1.5f;
    public float dashDamage = 100f;
    [Tooltip("돌진을 모두 피했을 때 점수")] public int dodgeScore = 2000;

    enum State { Roam, Aim, WindUp, Dash, Leave }
    State state = State.Roam;
    float stateT, nextShotAt;
    int shotsLeft, dashIndex;
    bool routeLocked;
    Vector3 vel, waypoint, dashStart, dashDir, dashEnd;
    LineRenderer routeLine;
    float H, scale;
    Animator anim;
    static Material beamMat;

    class Bolt { public LineRenderer lr; public Vector3 start, dir, target; public float travelled, total, length; public bool deals; }
    readonly List<Bolt> bolts = new List<Bolt>();

    public bool Leaving => state == State.Leave; // 돌진을 모두 피해 날아가 버리는 중

    public void Init(float giantHeight)
    {
        Active = this;
        H = Mathf.Max(5f, giantHeight);
        anim = GetComponentInChildren<Animator>();
        // 모델 키(약 1.8m)를 거인 키 비율에 맞춤
        float modelH = 1.8f;
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); if (b.size.y > 0.1f) modelH = b.size.y / transform.lossyScale.y; }
        scale = H * heightRatio / modelH;
        transform.localScale = Vector3.one * scale;
        foreach (var c in GetComponentsInChildren<Collider>()) Destroy(c);
        if (!beamMat) beamMat = new Material(Shader.Find("Sprites/Default"));
        // 돌진 예상 경로 선 (돌진 직전에만 보임)
        routeLine = new GameObject("SupermanDashRoute").AddComponent<LineRenderer>();
        routeLine.sharedMaterial = beamMat;
        routeLine.positionCount = 2;
        routeLine.useWorldSpace = true;
        routeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        routeLine.receiveShadows = false;
        routeLine.startWidth = routeLine.endWidth = Mathf.Max(0.3f, H * 0.035f);
        routeLine.enabled = false;
        shotsLeft = Random.Range(shotsBeforeDash.x, shotsBeforeDash.y + 1);
        nextShotAt = Time.time + Random.Range(shotInterval.x, shotInterval.y) + 1.5f;
        PickWaypoint();
        SupplyCrates.Notice("슈퍼맨이 나타났다!");
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        foreach (var b in bolts) if (b.lr) Destroy(b.lr.gameObject);
        if (routeLine) Destroy(routeLine.gameObject);
    }

    // ───────────── 거인 관련 ─────────────
    static GiantHealth Giant => GiantHealth.Instance;
    static Vector3 GiantChest()
    {
        var g = Giant;
        if (!g) return Vector3.zero;
        var b = g.Bone(HumanBodyBones.Chest);
        return b ? b.position : g.transform.position + Vector3.up * g.Height * 0.7f;
    }
    // 적이 공격하지 않는 상황 (집 안·연막·인질·정지 효과·메뉴)
    static bool AttacksBlocked => GameStartMenu.InMenu || !Giant || Giant.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || EnemyFreeze.Active;

    // 거인 주변 하늘의 아무 곳 중 화면 안에 보이는 곳 (화면 가장자리에서 조금 떨어진 곳)
    [Tooltip("화면 안에 머무는 범위 (화면 비율: 가로·세로 여백)")] public Vector2 screenMargin = new Vector2(0.12f, 0.15f);
    void PickWaypoint()
    {
        var g = Giant ? Giant.transform.position : transform.position;
        var cam = Camera.main;
        Vector3 best = g + Vector3.up * H; float bestScore = float.MaxValue;
        for (int i = 0; i < 24; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f), d = Random.Range(roamDistance.x, roamDistance.y) * H;
            Vector3 p = g + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d + Vector3.up * Random.Range(roamHeight.x, roamHeight.y) * H;
            if (!cam) { best = p; break; }
            // 화면 안쪽일수록 좋음 (밖으로 벗어난 만큼 점수가 나빠짐)
            Vector3 v = cam.WorldToViewportPoint(p);
            float off = v.z <= 0f ? 10f : Mathf.Max(0f, screenMargin.x - v.x) + Mathf.Max(0f, v.x - (1f - screenMargin.x))
                                            + Mathf.Max(0f, screenMargin.y - v.y) + Mathf.Max(0f, v.y - (1f - screenMargin.y));
            if (off < bestScore) { bestScore = off; best = p; }
            if (off <= 0f) break;
        }
        waypoint = best;
    }

    // 슈퍼맨이 화면 밖으로 나갔는지 (조금의 여유는 허용)
    bool OffScreen()
    {
        var cam = Camera.main;
        if (!cam) return false;
        Vector3 v = cam.WorldToViewportPoint(transform.position + transform.up * H * heightRatio * 0.5f);
        return v.z <= 0f || v.x < 0.03f || v.x > 0.97f || v.y < 0.03f || v.y > 0.97f;
    }
    float nextScreenCheck;
    bool IsOffScreenPoint(Camera cam, Vector3 p)
    {
        Vector3 v = cam.WorldToViewportPoint(p);
        return v.z <= 0f || v.x < screenMargin.x * 0.5f || v.x > 1f - screenMargin.x * 0.5f || v.y < screenMargin.y * 0.5f || v.y > 1f - screenMargin.y * 0.5f;
    }

    void Go(State s) { state = s; stateT = 0f; }

    // ───────────── 진행 ─────────────
    void Update()
    {
        UpdateBolts();
        if (!Giant) return;
        float dt = Time.deltaTime;
        stateT += dt;
        // 거인이 움직이면 함께 따라 이동 (거인 기준으로 날아다님 → 거인이 달려도 화면에서 뒤처지지 않음)
        // 돌진 경로가 정해진 뒤(예상 경로 표시 · 돌진 · 떠나기)엔 따라가지 않음: 보여 준 경로 그대로 날아감
        if (!FixedPath && GiantController.Instance)
            transform.position += GiantController.Instance.Velocity * dt;

        switch (state)
        {
            case State.Roam:
            {
                // 목표 지점까지 가속하며 날아가고, 가까워지면 새 목표 (거인이 움직이면 범위도 따라감)
                Vector3 to = waypoint - transform.position;
                if (to.magnitude < H * 0.4f) PickWaypoint();
                // 화면 밖으로 벗어나면(거인이 돌거나 움직여서) 화면 안의 새 목표로 바로 돌아옴
                else if (Time.time >= nextScreenCheck)
                {
                    nextScreenCheck = Time.time + 0.4f;
                    var cam = Camera.main;
                    if (OffScreen() || (cam && IsOffScreenPoint(cam, waypoint))) PickWaypoint();
                }
                to = waypoint - transform.position;
                bool outside = OffScreen();
                float speed = H * flySpeedRatio * (outside ? 1.8f : 1f); // 화면 밖이면 빨리 돌아옴
                vel = Vector3.MoveTowards(vel, to.normalized * speed, H * accelRatio * (outside ? 2f : 1f) * dt);
                transform.position += vel * dt;
                Pose(vel, Mathf.Clamp01(vel.magnitude / (H * flySpeedRatio * 0.6f)));
                if (!AttacksBlocked && !outside && Time.time >= nextShotAt) // 화면 밖에서는 쏘지 않음
                {
                    if (shotsLeft <= 0) { dashIndex = 0; routeLocked = false; Go(State.WindUp); SupplyCrates.Notice("슈퍼맨이 돌진한다!"); }
                    else Go(State.Aim);
                }
                break;
            }
            case State.Aim:
            {
                // 잠깐 멈춰 서서 거인을 바라보고 한 발 쏨
                vel = Vector3.MoveTowards(vel, Vector3.zero, H * accelRatio * 2f * dt);
                transform.position += vel * dt;
                Pose(GiantChest() - transform.position, 0f);
                if (OffScreen()) { PickWaypoint(); Go(State.Roam); break; } // 거인이 돌아서 화면 밖이 됐으면 쏘지 않고 화면 안으로
                if (stateT >= aimTime)
                {
                    if (!AttacksBlocked) Shoot();
                    shotsLeft--;
                    BackToRoam();
                }
                break;
            }
            case State.WindUp:
            {
                if (AttacksBlocked) { EndDashes(); BackToRoam(); break; }
                float wt = dashIndex == 0 ? windUpTime : nextWindUpTime;
                if (!routeLocked)
                {
                    // 멈춰서 뒤로 살짝 물러나며 기를 모음
                    vel = Vector3.MoveTowards(vel, Vector3.zero, H * accelRatio * 2f * dt);
                    Vector3 away = transform.position - GiantChest(); away.y = 0f;
                    transform.position += vel * dt + away.normalized * H * 0.25f * dt + Vector3.up * H * 0.08f * dt;
                    Pose(GiantChest() - transform.position, Mathf.Clamp01(stateT / wt));
                    if (stateT >= wt - routeShowTime) LockRoute();
                }
                else
                {
                    // 경로가 정해짐: 그 자리에 멈춰 경로 쪽을 겨누고, 예상 경로를 깜빡이며 보여 줌
                    vel = Vector3.zero;
                    Pose(dashDir, 1f);
                    ShowRoute();
                }
                if (stateT >= wt) { if (!routeLocked) LockRoute(); HideRoute(); Go(State.Dash); }
                break;
            }
            case State.Dash:
            {
                // 보여 준 경로 그대로 곧게 날아감 (거인이 비켜나면 지나침)
                if (AttacksBlocked) { EndDashes(); BackToRoam(); break; }
                float speed = DashSpeed;
                Vector3 before = transform.position;
                float remain = Vector3.Distance(before, dashEnd), step = speed * dt;
                transform.position = before + dashDir * Mathf.Min(remain, step);
                vel = dashDir * speed;
                Pose(dashDir, 1f);
                if (HitsGiant(before, transform.position)) { Impact(); break; }
                if (remain <= step) DashMissed();
                break;
            }
            case State.Leave:
            {
                // 마지막 돌진까지 피함: 그대로 하늘 저 멀리 날아가 사라짐 (한참 뒤 다시 나타남)
                vel += Vector3.up * H * 1.2f * dt;
                transform.position += vel * dt;
                Pose(vel, 1f);
                if (stateT > 4f || (transform.position - GiantChest()).magnitude > H * 40f) Destroy(gameObject);
                break;
            }
        }
    }

    // 돌진 경로가 정해져 그대로 날아가는 중 (거인을 따라 움직이거나 화면 안으로 끌어오지 않음)
    bool FixedPath => state == State.Dash || state == State.Leave || (state == State.WindUp && routeLocked);

    // 화면 가장자리 밖으로 나가려 하면 화면 안쪽으로 부드럽게 끌어당김 (같은 깊이에서)
    void LateUpdateKeepOnScreen()
    {
        if (FixedPath) return;
        var cam = Camera.main;
        if (!cam) return;
        Vector3 anchor = transform.position + transform.up * H * heightRatio * 0.5f;
        Vector3 v = cam.WorldToViewportPoint(anchor);
        if (v.z <= 0.1f) return;
        const float lo = 0.05f, hi = 0.95f;
        Vector3 c = new Vector3(Mathf.Clamp(v.x, lo, hi), Mathf.Clamp(v.y, lo, hi), v.z);
        if (c == v) return;
        Vector3 inside = cam.ViewportToWorldPoint(c);
        transform.position += (inside - anchor) * (1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    void BackToRoam()
    {
        nextShotAt = Time.time + Random.Range(shotInterval.x, shotInterval.y);
        PickWaypoint();
        Go(State.Roam);
    }

    // ───────────── 레이저 (단발) ─────────────
    void Shoot()
    {
        Vector3 target = GiantChest();
        Transform head = anim ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        Transform le = anim ? anim.GetBoneTransform(HumanBodyBones.LeftEye) : null, re = anim ? anim.GetBoneTransform(HumanBodyBones.RightEye) : null;
        Vector3 hp = head ? head.position : transform.position + transform.up * H * heightRatio * 0.9f;
        Vector3[] eyes = { le ? le.position : hp, re ? re.position : hp };
        float w = Mathf.Max(0.3f, H * 0.022f);
        for (int i = 0; i < eyes.Length; i++)
        {
            var go = new GameObject("LaserBolt");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = beamMat;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = laserColor; lr.endColor = new Color(1f, 0.9f, 0.8f, 1f); // 앞쪽 끝이 하얗게 빛남
            lr.startWidth = w * 0.8f; lr.endWidth = w * 1.3f;
            lr.SetPosition(0, eyes[i]); lr.SetPosition(1, eyes[i]);
            // 눈 두 개에서 나간 한 쌍은 한 번만 피해
            bolts.Add(new Bolt { lr = lr, start = eyes[i], target = target, dir = (target - eyes[i]).normalized, total = Vector3.Distance(eyes[i], target), length = H * 0.35f, deals = i == 0 });
        }
        CombatFX.Muzzle(eyes[0], (target - eyes[0]).normalized, H * 0.05f);
    }

    // 레이저 탄은 쏜 순간의 거인 위치로 곧게 날아감. 날아가는 동안 거인 몸(발~머리 기둥)에 닿아야 맞고,
    // 거인이 움직여 비켜나면 그대로 지나쳐 사라짐 → 움직여서 피할 수 있음
    void UpdateBolts()
    {
        float step = H * boltSpeedRatio * Time.deltaTime;
        var g = Giant;
        var cc = g ? g.GetComponent<CharacterController>() : null;
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            if (!b.lr) { bolts.RemoveAt(i); continue; }
            Vector3 before = b.start + b.dir * b.travelled;
            b.travelled += step;
            Vector3 head = b.start + b.dir * b.travelled, tail = b.start + b.dir * Mathf.Max(0f, b.travelled - b.length);
            b.lr.SetPosition(0, tail); b.lr.SetPosition(1, head);
            if (g && !g.IsDead && cc)
            {
                // 이번 프레임에 지나간 구간과 거인 몸 기둥 사이 거리
                Vector3 feet = g.transform.position, top = feet + Vector3.up * g.Height;
                if (SegSegDistance(before, head, feet, top) < cc.radius * 1.1f)
                {
                    if (b.deals) { g.TakeDamage(boltDamage, false); CombatFX.Muzzle(head, -b.dir, H * 0.08f); }
                    Destroy(b.lr.gameObject); bolts.RemoveAt(i); continue;
                }
            }
            // 빗나감: 목표 지점을 한참 지나거나 땅에 닿으면 사라짐
            if (b.travelled > b.total * 1.8f || head.y < 0.3f)
            {
                if (head.y < 0.3f) CombatFX.Muzzle(head, Vector3.up, H * 0.05f);
                Destroy(b.lr.gameObject); bolts.RemoveAt(i);
            }
        }
    }

    // 두 선분 사이 가장 가까운 거리 (샘플링으로 충분: 탄 한 프레임 구간은 짧음)
    static float SegSegDistance(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
    {
        float best = float.MaxValue;
        for (int k = 0; k <= 4; k++)
        {
            Vector3 p = Vector3.Lerp(a0, a1, k / 4f);
            Vector3 ab = b1 - b0; float t = Mathf.Clamp01(Vector3.Dot(p - b0, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            best = Mathf.Min(best, Vector3.Distance(p, b0 + ab * t));
        }
        return best;
    }

    // ───────────── 돌진 ─────────────
    bool FinalDash => dashIndex >= dashCount - 1;
    float DashSpeed => H * dashSpeedRatio * (FinalDash ? finalDashMultiplier : 1f);

    // 돌진 경로를 정함: 거인 가슴(움직이는 중이면 조금 앞질러)을 지나 그 너머까지 곧은 길. 이후 바뀌지 않음
    void LockRoute()
    {
        Vector3 chest = GiantChest();
        Vector3 gv = GiantController.Instance ? GiantController.Instance.Velocity : Vector3.zero;
        float eta = routeShowTime + Vector3.Distance(transform.position, chest) / DashSpeed;
        Vector3 aim = chest + gv * eta * dashLead;
        dashStart = transform.position;
        dashDir = (aim - dashStart).sqrMagnitude > 1e-4f ? (aim - dashStart).normalized : transform.forward;
        // 거인을 지나쳐 더 날아가되, 땅 아래로는 가지 않게
        float over = H * dashOvershootRatio;
        float ground = Giant.transform.position.y + H * 0.1f;
        if (dashDir.y < -0.01f) over = Mathf.Min(over, Mathf.Max(0f, (aim.y - ground) / -dashDir.y));
        dashEnd = aim + dashDir * over;
        routeLocked = true;
    }

    // 예상 경로: 빨간 선이 빠르게 깜빡임 (마지막 돌진은 노란빛)
    void ShowRoute()
    {
        if (!routeLine) return;
        routeLine.enabled = true;
        routeLine.SetPosition(0, dashStart);
        routeLine.SetPosition(1, dashEnd);
        float a = Mathf.Repeat(Time.time * 10f, 1f) < 0.6f ? 0.85f : 0.35f;
        Color c = FinalDash ? new Color(1f, 0.85f, 0.1f, a) : new Color(1f, 0.15f, 0.1f, a);
        routeLine.startColor = c; routeLine.endColor = new Color(c.r, c.g, c.b, a * 0.4f);
    }
    void HideRoute() { if (routeLine) routeLine.enabled = false; }
    // 돌진 공격이 끝남(맞혔거나 중간에 멈춤): 다음 돌진 전까지 쏠 레이저 발 수를 새로 랜덤으로 정함
    void EndDashes() { dashIndex = 0; routeLocked = false; HideRoute(); shotsLeft = Random.Range(shotsBeforeDash.x, shotsBeforeDash.y + 1); }

    // 이번 프레임에 지나간 구간이 거인 몸(발~머리 기둥)에 닿았는지
    bool HitsGiant(Vector3 a, Vector3 b)
    {
        var g = Giant;
        var cc = g ? g.GetComponent<CharacterController>() : null;
        if (!g || g.IsDead || !cc) return false;
        Vector3 feet = g.transform.position, top = feet + Vector3.up * g.Height;
        int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / Mathf.Max(0.1f, cc.radius * 0.5f))); // 빨라도 건너뛰지 않게 잘게 나눠 검사
        float best = float.MaxValue;
        for (int i = 0; i < n; i++) best = Mathf.Min(best, SegSegDistance(Vector3.Lerp(a, b, i / (float)n), Vector3.Lerp(a, b, (i + 1) / (float)n), feet, top));
        return best < cc.radius + H * heightRatio * 0.15f;
    }

    // 돌진이 맞음: 큰 피해 → 돌진을 멈추고 튕겨 나가 다시 레이저부터
    void Impact()
    {
        Giant.TakeDamage(dashDamage);
        CombatFX.Explosion(transform.position, H * 0.35f);
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.9f);
        SupplyCrates.Notice($"슈퍼맨의 돌진! -{dashDamage:0}");
        Vector3 away = transform.position - GiantChest(); away = away.sqrMagnitude > 0.01f ? away.normalized : -dashDir;
        vel = (away + Vector3.up * 0.5f).normalized * H * flySpeedRatio;
        EndDashes();
        BackToRoam();
    }

    // 돌진을 피함: 다음 돌진 준비, 마지막 돌진까지 피했으면 그대로 날아가 버림
    void DashMissed()
    {
        if (!FinalDash)
        {
            dashIndex++;
            routeLocked = false;
            vel = dashDir * DashSpeed * 0.3f; // 지나친 기세로 조금 더 미끄러지다 멈춤
            Go(State.WindUp);
            if (FinalDash) SupplyCrates.Notice("슈퍼맨이 마지막 돌진을 준비한다!");
            return;
        }
        HideRoute();
        vel = dashDir * DashSpeed;
        ScoreManager.Add(dodgeScore, "돌진 회피!");
        SupplyCrates.Notice("돌진을 모두 피했다! 슈퍼맨이 날아가 버렸다!");
        SupermanSpawner.OnSupermanDied();
        Go(State.Leave);
    }

    // ───────────── 자세 ─────────────
    // flight 0 = 서서 dir 쪽을 바라봄, 1 = 몸을 눕혀 dir 쪽으로 머리부터 날아감 (오른팔을 앞으로 뻗음)
    float flight;
    Vector3 flightDir = Vector3.forward;
    void Pose(Vector3 dir, float flightAmount)
    {
        float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
        flight = Mathf.Lerp(flight, flightAmount, k);
        if (dir.sqrMagnitude > 1e-4f) flightDir = Vector3.Slerp(flightDir, dir.normalized, k).normalized;
        Vector3 flat = flightDir; flat.y = 0f;
        Quaternion upright = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward, Vector3.up);
        Vector3 belly = Vector3.ProjectOnPlane(Vector3.down, flightDir);
        Quaternion lying = Quaternion.LookRotation(belly.sqrMagnitude > 1e-4f ? belly.normalized : Vector3.down, flightDir); // 머리(위)가 날아가는 쪽, 배(앞)가 땅 쪽
        transform.rotation = Quaternion.Slerp(upright, lying, flight);
    }

    void LateUpdate()
    {
        LateUpdateKeepOnScreen();
        if (!anim || flight < 0.01f) return;
        // 날 때 오른팔을 머리 위(몸 위쪽 = 날아가는 쪽)로 쭉 뻗음
        var up = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var lo = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (!up || !lo || !hand) return;
        Vector3 bodyUp = transform.up;
        Vector3 cur = lo.position - up.position;
        up.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, bodyUp), flight) * up.rotation;
        Vector3 cur2 = hand.position - lo.position;
        lo.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur2, bodyUp), flight) * lo.rotation;
    }
}
