using System.Collections.Generic;
using UnityEngine;

// UFO 레이저에 닿으면 거인이 일반 사람 크기로 작아짐 (duration초 뒤 원래 크기로).
// 작아진 동안:
//  - HP가 따로 tinyMaxHP(20)로 바뀜 (다 깎이면 게임 오버, 버텨내면 원래 HP 그대로). 군대 공격은 원래 HP 비율만큼 깎임
//  - 건물을 부수거나 무엇을 밟지도 못함 (GiantStomp 정지). 건물에는 막힘
//  - 주변 사람들이 몰려와 주먹질하고 (ShrinkAttacker / Civilian), 가까운 차들이 도로를 벗어나 들이받음 (TrafficCar.StartChase)
//  - 일반 시민과 같은 속도로 이동, 카메라는 사람 크기에 맞게 가까이 붙음
public class GiantShrink : MonoBehaviour
{
    public static GiantShrink Instance { get; private set; }
    public static bool Active => Instance && Instance.active;
    public static float SavedScale => Instance ? Instance.savedScale : 1f;
    public static float CameraBlend => Instance ? Instance.camK : 0f;   // 0 = 보통 카메라, 1 = 작아진 거인용 카메라
    public static float TinyCameraDistance => Instance ? Instance.tinyCameraDistance : 8f;
    public static float TinyCameraPitch => Instance ? Instance.tinyCameraPitch : 70f;
    public static float TinyCameraFov => Instance ? Instance.tinyCameraFov : 18f;
    public static float TimeLeft => Active ? Mathf.Max(0f, Instance.duration - (Time.time - Instance.startAt)) : 0f;

    [Header("축소")]
    [Tooltip("작아져 있는 시간(초)")] public float duration = 15f;
    [Tooltip("켜면 시민 키에 맞춰 작아짐 (일반 사람 크기)")] public bool matchCivilianHeight = true;
    [Tooltip("작아졌을 때 키(m). 시민 키를 잴 수 없을 때 씀")] public float tinyHeight = 4.7f;
    [Tooltip("작아지거나 커지는 데 걸리는 시간(초)")] public float transitionTime = 0.4f;
    [Tooltip("켜면 작아졌을 때 이동 속도가 일반 시민과 같음 (달리기 = 시민 도망 속도, Shift 걷기 = 시민 걷기 속도)")] public bool matchCivilianSpeed = true;
    [Tooltip("작아졌을 때 달리기 속도(m/s, 크기와 무관. Shift 걷기는 절반). matchCivilianSpeed를 끄거나 시민이 없을 때 씀")] public float tinyRunSpeed = 10f;
    [Tooltip("작아졌을 때 카메라 거리(m)")] public float tinyCameraDistance = 22f;
    [Tooltip("작아졌을 때 내려다보는 각도 (90 = 수직)")] public float tinyCameraPitch = 38f;
    [Tooltip("작아졌을 때 카메라 시야각")] public float tinyCameraFov = 50f;
    [Tooltip("다 작아진 뒤 이 시간(초) 동안은 공격받지 않음 (정신 차릴 시간)")] public float graceTime = 1.5f;
    [Tooltip("주먹에 맞은 뒤·쓰러졌다 일어난 뒤 무적 시간(초). 그동안 몸이 깜빡임")] public float hitInvulnTime = 1f;
    [Tooltip("사람 크기일 때 HP (작아질 때마다 가득 참)")] public float tinyMaxHP = 20f;

    [Header("사람들의 공격")]
    [Tooltip("주변에 몰려드는 공격자 수 (근처 시민과 별도)")] public int walkerCount = 6;
    public Vector2 walkerSpawnDistance = new Vector2(18f, 45f);
    [Tooltip("이 거리(m) 안에 있던 시민도 달려들어 공격")] public float civilianJoinRadius = 60f;
    [Tooltip("공격자 달리기 속도 = 시민 도망 속도 × 이 비율")] public float attackerSpeedRatio = 0.6f;
    [Tooltip("주먹 한 대 피해 (사람 크기 HP 기준)")] public float punchDamage = 2f;
    [Tooltip("한 사람이 때리는 간격(초)")] public float punchInterval = 1.8f;
    [Tooltip("때릴 수 있는 거리(m). 작아질 때 키에 맞춰 정해짐")] public float punchReach = 2f;
    [Tooltip("주먹을 휘두르기 시작해서 맞는 순간까지(초). Cross Punch 동작(1.5배속)에서 주먹이 뻗는 때")] public float punchHitDelay = 0.7f;
    [Tooltip("주먹에 맞아 밀려나는 속도(m/s). 금방 줄어들어 4m 정도 밀림")] public float punchKnockback = 24f;
    [Tooltip("맞았을 때 움츠리는 동작을 그대로 유지하는 시간(초)")] public float flinchHold = 0.4f;
    [Tooltip("움츠린 뒤 원래 동작으로 돌아오는 시간(초)")] public float flinchFade = 0.3f;

    [Header("차량의 공격")]
    [Tooltip("동시에 들이받으러 오는 차 수")] public int maxChasingCars = 3;
    [Tooltip("이 거리(m) 안의 차가 들이받으러 옴")] public float carChaseRadius = 160f;
    public float carChaseSpeed = 20f;
    [Tooltip("차에 한 번 받힐 때 피해 (사람 크기 HP 기준)")] public float ramDamage = 6f;
    [Tooltip("차에 받혀 날아가는 수평 속도(m/s)")] public float ramLaunchSpeed = 45f;
    [Tooltip("차에 받혀 날아오르는 속도(m/s)")] public float ramLaunchUp = 20f;
    [Tooltip("날아가는 동안 중력(m/s²)")] public float flyGravity = 22f;
    [Tooltip("땅에 떨어진 뒤 쓰러져 있는 시간(초). 그동안 조작 불가")] public float lieDownTime = 0.8f;
    [Tooltip("일어나는 동작 시간(초). 끝나면 다시 조작 가능")] public float getUpTime = 1.5f;

    bool active, restoring;
    float savedScale = 1f, startAt, scaleFrom, scaleTo, scaleT = 1f, tinyAt, camK = 0f, savedNear = -1f, tinyRatio = 0.1f, targetHeight;
    float nextCarCheck;
    Vector3 knockVel;                       // 주먹에 맞아 살짝 밀려남 (수평, 금방 줄어듦)
    Vector3 flyVel; bool flying, prevInputLocked; float flyStart;   // 차에 받혀 날아가는 중
    float downUntil;                        // 0보다 크면 땅에 떨어져 쓰러져 있는 중 (이 시각에 일어남)
    float standUntil;                       // 0보다 크면 일어나는 중 (이 시각에 다시 조작 가능)

    /// <summary>사람 크기일 때 HP</summary>
    public float TinyHP { get; private set; }
    GiantController ctrl; GiantHealth hp; CharacterController cc;
    readonly List<ShrinkAttacker> walkers = new List<ShrinkAttacker>();
    public IReadOnlyList<ShrinkAttacker> Walkers => walkers;   // 몰려든 공격자 (추격 차량이 피해 감)
    readonly List<TrafficCar> chasers = new List<TrafficCar>();
    int giantLayer, buildingLayer, militaryLayer, buildingMask;
    bool prevIgnoreMilitary;
    string toast; float toastUntil;

    void Awake()
    {
        Instance = this;
        giantLayer = LayerMask.NameToLayer("Giant");
        buildingLayer = LayerMask.NameToLayer("Building");
        militaryLayer = LayerMask.NameToLayer("Military");
        buildingMask = LayerMask.GetMask("Building");
    }

    void OnDestroy() { if (Instance == this) { Restore(); Instance = null; } }

    /// <summary>다 작아졌고 아직 돌아가는 중이 아니라서 공격을 받을 수 있는지</summary>
    public bool Vulnerable => active && !restoring && scaleT >= 1f && !movingToSidewalk && Time.time - tinyAt >= graceTime
                              && !KnockedDown && Time.time >= invulnUntil;

    // UFO 레이저에 닿았을 때 호출. 이미 작아져 있거나 죽었으면 false
    public static bool TryBegin() => Instance && Instance.Begin();

    bool Begin()
    {
        if (active) return false;
        hp = GiantHealth.Instance;
        if (!hp || hp.IsDead) return false;
        ctrl = hp.GetComponent<GiantController>(); cc = hp.GetComponent<CharacterController>();
        if (!ctrl || !cc) return false;
        active = true; restoring = false; startAt = Time.time; tinyAt = float.MaxValue;
        savedScale = ctrl.ModelScale;
        scaleFrom = savedScale;
        targetHeight = matchCivilianHeight ? CivilianHeight() : tinyHeight;
        scaleTo = savedScale * targetHeight / Mathf.Max(0.01f, hp.Height);
        tinyRatio = Mathf.Clamp(scaleTo / savedScale, 1e-5f, 0.999f);
        punchReach = targetHeight * 0.45f;
        scaleT = 0f; knockVel = Vector3.zero; flyVel = Vector3.zero; flying = false; downUntil = 0f; standUntil = 0f; invulnUntil = 0f;
        TinyHP = tinyMaxHP;
        // 작아지면서 가장 가까운 인도로 달려가 피함. 건물 안에 있었을 수도 있으니 도착할 때까지는 건물을 통과하게 두고, 도착하면 건물에 막히게 함
        Vector3 walk = SidewalkTarget(hp.transform.position);
        float far = Vector3.Distance(new Vector3(walk.x, 0f, walk.z), new Vector3(hp.transform.position.x, 0f, hp.transform.position.z));
        movingToSidewalk = far > 0.3f;
        if (movingToSidewalk)
        {
            ctrl.autoMoveTarget = walk;
            ctrl.speedOverride = Mathf.Clamp(far / Mathf.Max(0.3f, sidewalkMoveTime), tinyRunSpeed, 60f); // 너무 오래 걸리지 않게 거리에 맞춰 빨리
        }
        else { SetTinySpeed(); BlockBuildings(); }
        if (giantLayer >= 0 && militaryLayer >= 0)
        {
            prevIgnoreMilitary = Physics.GetIgnoreLayerCollision(giantLayer, militaryLayer);
            Physics.IgnoreLayerCollision(giantLayer, militaryLayer, true); // 차·탱크 몸체에 막혀 끼지 않게 (들이받기는 거리로 판정)
        }
        var cam = Camera.main;
        if (cam) { savedNear = cam.nearClipPlane; cam.nearClipPlane = 0.1f; }
        Show($"UFO 레이저에 맞았다!  {duration:0}초 동안 버텨라 — 모두가 공격해 온다!");
        return true;
    }

    // 시민 한 명의 키 (모델 크기 그대로). 시민이 없으면 tinyHeight
    float CivilianHeight()
    {
        var mgr = CivilianManager.Instance;
        if (mgr)
            foreach (var c in mgr.Civilians)
            {
                if (!c) continue;
                var rs = c.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.y > 0.5f) return b.size.y;
            }
        return tinyHeight;
    }

    [Tooltip("작아질 때 가까운 인도까지 달려가는 데 걸리는 시간(초, 대략)")] public float sidewalkMoveTime = 1.2f;
    bool movingToSidewalk;

    // 작아진 동안 이동 속도: 일반 시민과 같게 (달리기 = 시민 도망 속도, 걷기 = 시민 걷기 속도)
    void SetTinySpeed()
    {
        var mgr = CivilianManager.Instance;
        if (matchCivilianSpeed && mgr)
        {
            ctrl.speedOverride = mgr.runSpeed * mgr.scale;
            ctrl.walkSpeedOverride = mgr.walkSpeed * mgr.scale;
        }
        else { ctrl.speedOverride = tinyRunSpeed; ctrl.walkSpeedOverride = 0f; }
    }

    void BlockBuildings() { if (giantLayer >= 0 && buildingLayer >= 0) Physics.IgnoreLayerCollision(giantLayer, buildingLayer, false); }

    [Tooltip("작아졌을 때 자동으로 올라설 수 있는 턱 높이(m)")] public float maxStepUp = 0.6f;

    // 연석 오르기: 이동하는 쪽 바로 앞 바닥이 조금 높으면 그 위로 올려 줌
    void StepUp()
    {
        if (!cc) return;
        Vector3 v = ctrl.Velocity; v.y = 0f;
        if (v.sqrMagnitude < 0.01f) return;
        Vector3 pos = hp.transform.position, dir = v.normalized;
        Vector3 probe = pos + dir * (cc.radius + 0.08f) + Vector3.up * (maxStepUp + 0.05f);
        int mask = ~((1 << Mathf.Max(0, giantLayer)) | (1 << Mathf.Max(0, militaryLayer)));
        if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, maxStepUp + 0.3f, mask, QueryTriggerInteraction.Ignore)) return;
        float rise = hit.point.y - pos.y;
        if (rise < 0.03f || rise > maxStepUp) return;
        cc.enabled = false;
        hp.transform.position = new Vector3(hit.point.x, hit.point.y + 0.01f, hit.point.z);
        cc.enabled = true;
    }

    // 주변 블록들의 인도 중 가장 가까운 지점
    static Vector3 SidewalkTarget(Vector3 p)
    {
        var mgr = CivilianManager.Instance;
        if (!mgr || !mgr.city) return p;
        var b0 = mgr.BlockAt(p);
        Vector3 best = p; float bd = float.MaxValue;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var b = new Vector2Int(b0.x + dx, b0.y + dz);
                if (b.x < 0 || b.y < 0 || b.x >= mgr.city.blocksX || b.y >= mgr.city.blocksZ) continue;
                Vector3 q = mgr.NearestOnWalkway(b, p, out _);
                float d = new Vector3(q.x - p.x, 0f, q.z - p.z).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
        best.y = p.y;
        return best;
    }

    // 공격자·추격 차량 정리 (크기는 Update에서 되돌림)
    void StopAttackers()
    {
        foreach (var w in walkers) if (w) Destroy(w.gameObject);
        walkers.Clear();
        foreach (var c in chasers) if (c) c.StopChase();
        chasers.Clear();
    }

    // 원래 상태로 되돌림 (크기 제외)
    void Restore()
    {
        if (ctrl) { ctrl.speedOverride = 0f; ctrl.walkSpeedOverride = 0f; ctrl.autoMoveTarget = null; }
        Land();
        movingToSidewalk = false;
        if (giantLayer >= 0 && buildingLayer >= 0) Physics.IgnoreLayerCollision(giantLayer, buildingLayer, true);
        if (giantLayer >= 0 && militaryLayer >= 0) Physics.IgnoreLayerCollision(giantLayer, militaryLayer, prevIgnoreMilitary);
        var cam = Camera.main;
        if (cam && savedNear > 0f) cam.nearClipPlane = savedNear;
        savedNear = -1f;
        StopAttackers();
        camK = 0f;
        invulnUntil = 0f; UpdateBlink();
        flinchAt = -10f; if (ctrl) { var a = GiantAnim; int li = a ? a.GetLayerIndex("Flinch") : -1; if (li >= 0) a.SetLayerWeight(li, 0f); }
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    void Update()
    {
        if (!active) return;
        if (!hp || !ctrl) { active = false; Restore(); return; }
        float dt = Time.deltaTime;
        UpdateBlink();

        // 크기 변화 (로그 공간에서 보간해 작아지는 속도가 일정하게 보이게)
        if (scaleT < 1f)
        {
            scaleT = Mathf.Min(1f, scaleT + dt / Mathf.Max(0.05f, transitionTime));
            float s = Mathf.Exp(Mathf.Lerp(Mathf.Log(scaleFrom), Mathf.Log(scaleTo), Smooth(scaleT)));
            ctrl.SetModelScale(s);
            if (scaleT >= 1f)
            {
                if (restoring) { active = false; Restore(); return; }
                tinyAt = Time.time;
            }
        }
        // 카메라 전환 정도: 작은 정도에 맞춰 0 ~ 1
        float r = ctrl.ModelScale / Mathf.Max(1e-6f, savedScale);
        camK = Mathf.Clamp01(Mathf.Log(Mathf.Max(1e-6f, r)) / Mathf.Log(tinyRatio));

        if (hp.IsDead) { StopAttackers(); return; } // 죽은 뒤엔 그대로 (게임 오버 화면)
        if (scaleT >= 1f && !restoring) StepUp();

        // 주먹에 맞아 살짝 밀려남
        if (knockVel.sqrMagnitude > 0.01f && cc)
        {
            cc.Move(knockVel * dt);
            knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, knockVel.magnitude * 6f * dt + 1f * dt);
        }
        // 차에 받혀 포물선으로 날아감 (그동안 조작 불가). 땅에 닿으면 끝
        if (flying && cc)
        {
            flyVel.y -= flyGravity * dt;
            var flags = cc.Move(flyVel * dt);
            // 벽·턱에 닿으면 CharacterController가 미끄러지며 지나감. 혹시 땅을 못 찾아도 4초 뒤엔 끝냄
            if (((flags & CollisionFlags.Below) != 0 && flyVel.y < 0f) || Time.time - flyStart > 4f) Touchdown();
        }
        if (downUntil > 0f && Time.time >= downUntil) GetUp();
        if (standUntil > 0f && Time.time >= standUntil) FinishStand();
        UpdateFlinchWeight();

        // 인도로 피하는 중: 도착하면(또는 너무 오래 걸리면) 건물에 막히게 하고, 축소 시간·무적 시간은 그때부터 셈
        if (movingToSidewalk)
        {
            if (!ctrl.autoMoveTarget.HasValue || Time.time - startAt > sidewalkMoveTime * 3f + transitionTime)
            {
                ctrl.autoMoveTarget = null; movingToSidewalk = false;
                SetTinySpeed(); BlockBuildings();
                startAt = Time.time; tinyAt = Time.time;
            }
            else return;
        }

        if (!restoring && Time.time - startAt >= duration)
        {
            restoring = true; scaleFrom = ctrl.ModelScale; scaleTo = savedScale; scaleT = 0f;
            Land();   // 쓰러져 있었으면 일어나서 커짐
            StopAttackers();
            Show("버텨냈다! 원래 크기로 돌아왔다!");
            return;
        }
        if (restoring || scaleT < 1f) return;

        UpdateCrowd();
        if (Time.time >= nextCarCheck) { nextCarCheck = Time.time + 0.5f; UpdateCars(); }
    }

    // ───── 사람들 ─────

    // 공격자 수 유지: 모자라면 주변 인도·길에 새로 세움 (건물 안은 피함)
    void UpdateCrowd()
    {
        for (int i = walkers.Count - 1; i >= 0; i--) if (!walkers[i]) walkers.RemoveAt(i);
        var mgr = CivilianManager.Instance;
        if (!mgr || mgr.prefabs == null || mgr.prefabs.Length == 0) return;
        Vector3 gp = hp.transform.position; gp.y = 0f;
        for (int n = 0; n < 2 && walkers.Count < walkerCount; n++) SpawnWalker(mgr, gp);
    }

    void SpawnWalker(CivilianManager mgr, Vector3 gp)
    {
        Vector3 pos = gp;
        bool found = false;
        for (int tries = 0; tries < 8 && !found; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            pos = gp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(walkerSpawnDistance.x, walkerSpawnDistance.y);
            found = mgr.InsideCity(pos) && mgr.Free(pos, 1.2f);
        }
        if (!found) return;
        pos.y = mgr.GroundY(pos);
        var go = Instantiate(mgr.prefabs[Random.Range(0, mgr.prefabs.Length)], pos, Quaternion.LookRotation(gp - pos), transform);
        go.name = "ShrinkAttacker";
        go.transform.localScale = Vector3.one * mgr.scale;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c); // 작아진 거인을 밀어내지 않게 (맞는 건 거리로 판정)
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = mgr.locomotion; anim.applyRootMotion = false; }
        var w = go.AddComponent<ShrinkAttacker>();
        w.Init(anim, mgr.runSpeed * mgr.scale * attackerSpeedRatio * Random.Range(0.85f, 1.1f));
        walkers.Add(w);
    }

    float lastPunchToast;

    /// <summary>사람에게 한 대 맞았을 때 (ShrinkAttacker.Step이 부름). dir = 때린 사람 → 거인 방향</summary>
    public void OnPunched(Vector3 dir)
    {
        if (!Vulnerable || !hp) return;
        TakeTinyDamage(punchDamage);
        invulnUntil = Time.time + hitInvulnTime;   // 맞은 뒤 잠깐 무적 (몰매로 순식간에 쓰러지지 않게)
        dir.y = 0f;
        if (!flying && dir.sqrMagnitude > 0.0001f) knockVel += dir.normalized * punchKnockback;
        // 맞으면 상체를 움찔 움츠림 (다리는 하던 동작 그대로). 쓰러져 있을 땐 안 함
        if (!KnockedDown && !hp.IsDead) { var a = GiantAnim; if (a) { a.SetTrigger(FlinchHash); flinchAt = Time.time; } }
        if (Time.time - lastPunchToast > 4f) { lastPunchToast = Time.time; Show("사람들이 때린다! 도망쳐!"); }
    }

    /// <summary>사람 크기일 때 피해 (tinyMaxHP 기준). 다 깎이면 게임 오버 (무한모드·테스트 옵션이면 0에서 멈춤)</summary>
    public void TakeTinyDamage(float dmg)
    {
        if (!active || !hp || hp.IsDead || dmg <= 0f) return;
        if (Time.time < invulnUntil || KnockedDown) return;   // 무적 시간·쓰러져 있는 동안엔 피해 없음 (차 피해는 쓰러지기 직전에 들어옴)
        TinyHP = Mathf.Max(0f, TinyHP - dmg);
        hp.Flash();
        if (TinyHP <= 0f && !hp.NoDeath)
        {
            Land();
            hp.Kill("사람 크기일 때 쓰러졌다");
        }
    }

    static readonly int HitByCarHash = Animator.StringToHash("HitByCar");
    static readonly int GetUpHash = Animator.StringToHash("GetUp");
    static readonly int FlinchHash = Animator.StringToHash("Flinch");
    float flinchAt = -10f;
    float invulnUntil;                      // 이 시각까지 무적 (맞은 뒤·일어난 뒤)
    bool blinkHidden;

    // 무적인 동안 몸을 깜빡여 보여 줌
    void UpdateBlink()
    {
        bool hide = active && hp && !hp.IsDead && !KnockedDown && Time.time < invulnUntil && Mathf.Repeat(Time.time, 0.16f) < 0.08f;
        if (hide == blinkHidden || !ctrl) return;
        blinkHidden = hide;
        foreach (var r in ctrl.GetComponentsInChildren<SkinnedMeshRenderer>()) r.enabled = !hide;
    }

    // 움츠리기 레이어(상체) 가중치: 맞은 직후 1, 동작이 끝나갈 즈음 0으로 (평소엔 0이라 하던 동작 그대로)
    void UpdateFlinchWeight()
    {
        var a = GiantAnim;
        if (!a) return;
        int li = a.GetLayerIndex("Flinch");
        if (li < 0) return;
        float t = Time.time - flinchAt;
        float w = KnockedDown || !active ? 0f : 1f - Mathf.Clamp01((t - flinchHold) / flinchFade);
        a.SetLayerWeight(li, w);
    }
    Animator GiantAnim => ctrl ? ctrl.GetComponentInChildren<Animator>() : null;

    // 땅에 떨어짐: Hit By Car 동작대로 쓰러진 채 잠깐 누워 있다가 일어남
    void Touchdown()
    {
        flying = false; flyVel = Vector3.zero;
        if (ctrl) ctrl.externalMotion = false;
        downUntil = Time.time + lieDownTime;
    }

    // 일어나기 시작: 무릎 꿇었다 일어서는 동작 (누운 자세에서 이어짐). 다 일어서면 다시 조작 가능
    void GetUp()
    {
        downUntil = 0f;
        var a = GiantAnim;
        if (a) { a.ResetTrigger(HitByCarHash); a.SetTrigger(GetUpHash); }
        standUntil = Time.time + getUpTime;
    }

    // 다 일어섬: 조작 가능
    void FinishStand()
    {
        standUntil = 0f;
        invulnUntil = Time.time + hitInvulnTime;   // 일어난 직후 잠깐 무적
        if (ctrl) ctrl.inputLocked = prevInputLocked;
    }

    // 날아가기·쓰러짐을 바로 끝냄 (원래대로 돌아감 / 죽음)
    void Land()
    {
        if (!flying && downUntil <= 0f && standUntil <= 0f) return;
        flying = false; flyVel = Vector3.zero;
        if (ctrl) ctrl.externalMotion = false;
        if (downUntil > 0f || standUntil <= 0f) GetUp();
        FinishStand();
    }

    // 쓰러졌거나 날아가거나 일어나는 중 (움츠리기·차에 또 치이기 없음)
    bool KnockedDown => flying || downUntil > 0f || standUntil > 0f;

    // ───── 차들 ─────

    // 들이받으러 오는 차 수 유지: 가깝고 건물에 가리지 않은 차부터
    void UpdateCars()
    {
        for (int i = chasers.Count - 1; i >= 0; i--) if (!chasers[i] || chasers[i].IsDead) chasers.RemoveAt(i);
        if (chasers.Count >= maxChasingCars) return;
        Vector3 gp = hp.transform.position;
        TrafficCar best = null; float bd = carChaseRadius * carChaseRadius;
        foreach (var car in TrafficCar.All)
        {
            if (!car || car.IsDead || car.Chasing || car.flee) continue;
            Vector3 d = car.transform.position - gp; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq >= bd) continue;
            if (Physics.Linecast(car.transform.position + Vector3.up * 2f, gp + Vector3.up * 2f, buildingMask, QueryTriggerInteraction.Ignore)) continue;
            bd = sq; best = car;
        }
        if (best) { best.StartChase(carChaseSpeed); chasers.Add(best); }
    }

    /// <summary>차에 받혔을 때 (TrafficCar가 부름)</summary>
    public void OnRammed(TrafficCar car, Vector3 carForward)
    {
        if (!Vulnerable || !hp || KnockedDown) return;
        TakeTinyDamage(ramDamage);
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.12f);
        if (hp.IsDead) return;
        Vector3 push = carForward; push.y = 0f;
        if (push.sqrMagnitude < 0.0001f) push = -hp.transform.forward;
        push.Normalize();
        // 멀리 날아감: 차 진행 방향으로 포물선
        flyVel = push * ramLaunchSpeed + Vector3.up * ramLaunchUp;
        knockVel = Vector3.zero;
        // Hit By Car 동작: 몸이 오른쪽(+x)으로 넘어가는 동작이라, 오른쪽이 날아가는 방향이 되게 돌려 세움
        hp.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, -90f, 0f) * push);
        var a = GiantAnim;
        if (a) { a.ResetTrigger(GetUpHash); a.SetTrigger(HitByCarHash); }
        prevInputLocked = ctrl.inputLocked; ctrl.inputLocked = true;
        ctrl.externalMotion = true;
        flying = true; flyStart = Time.time;
        Show($"차에 치였다!  -{ramDamage:0}");
    }

    void Show(string text) { toast = text; toastUntil = Time.time + 3f; }

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return;
        var big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        if (Time.time < toastUntil && !string.IsNullOrEmpty(toast))
        {
            float a = Mathf.Clamp01((toastUntil - Time.time) / 0.5f);
            var rr = new Rect(0, Screen.height * 0.16f, Screen.width, 40);
            GUI.color = new Color(0, 0, 0, 0.8f * a); GUI.Label(new Rect(rr.x + 2, rr.y + 2, rr.width, rr.height), toast, big);
            GUI.color = new Color(0.85f, 0.6f, 1f, a); GUI.Label(rr, toast, big);
        }
        if (active && hp && Camera.main)
        {
            // 작아진 거인 위치 표시: 머리 위에서 아래를 가리키는 화살표
            Vector3 sp = Camera.main.WorldToScreenPoint(hp.transform.position + Vector3.up * (cc ? cc.height * 1.3f : 2f));
            if (sp.z > 0f)
            {
                var arrow = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
                float bob = Mathf.Sin(Time.time * 6f) * 4f;
                var ar = new Rect(sp.x - 20, Screen.height - sp.y - 30 + bob, 40, 28);
                GUI.color = new Color(0, 0, 0, 0.8f); GUI.Label(new Rect(ar.x + 1, ar.y + 1, ar.width, ar.height), "▼", arrow);
                GUI.color = new Color(0.85f, 0.6f, 1f); GUI.Label(ar, "▼", arrow);
            }
        }
        if (active && !restoring && hp && !hp.IsDead)
        {
            var small = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            string t = $"버텨라!  {TimeLeft:0}초";
            GUI.color = new Color(0, 0, 0, 0.8f); GUI.Label(new Rect(1, Screen.height * 0.16f + 41, Screen.width, 26), t, small);
            GUI.color = new Color(0.85f, 0.6f, 1f); GUI.Label(new Rect(0, Screen.height * 0.16f + 40, Screen.width, 26), t, small);
        }
        GUI.color = Color.white;
    }
}
