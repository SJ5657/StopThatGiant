using System.Collections.Generic;
using UnityEngine;

// 병력 마차 하나 (TroopWagonSpawner가 내보냄): 궁수들이 짐칸에 탄 채로 거인 주위를 돌며 달리고, 멈추지 않고 화살을 쏨.
//  - 마을 안에서 나온 마차는 도로 교차로를 따라, 마을 밖에서 나온 마차는 언덕 위를 자유롭게 달림 (성벽 안으로는 안 들어감)
//  - 거인이 너무 가까우면 달아남. 거인 몸·발에 닿으면 마차째 부서지고 궁수도 함께 죽음 (점수·경험치)
//  - 거인이 집에 들어가거나 인질 구출로 적이 물러날 때는 멀리 달아나 사라짐
public class TroopWagon : MonoBehaviour
{
    [HideInInspector] public TroopWagonSpawner owner;
    [HideInInspector] public Transform model;
    [HideInInspector] public float riderScale = 1.2f;

    public bool IsDead { get; private set; }

    class Rider { public Transform t; public float nextShot; }
    readonly List<Rider> riders = new List<Rider>();
    CityGenerator city;
    Vector3 gridOrigin; float pitch; int maxI, maxJ;
    bool onRoads, moving, leaving;
    int ci, cj, ti, tj, lastI = -1, lastJ = -1;
    float circleDir = 1f, nextFlip, leaveAt;
    int buildingMask;

    public void AddRider(Transform t) => riders.Add(new Rider { t = t, nextShot = Time.time + Random.Range(1f, owner.fireInterval) });

    void Start()
    {
        city = FindObjectOfType<CityGenerator>();
        buildingMask = LayerMask.GetMask("Building");
        circleDir = Random.value < 0.5f ? 1f : -1f;
        nextFlip = Time.time + Random.Range(8f, 16f);
        if (!city) return;
        pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        gridOrigin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        maxI = city.blocksX; maxJ = city.blocksZ;
        onRoads = owner.InTown(transform.position, 0f);
        if (onRoads)
        {
            ci = ti = Mathf.Clamp(Mathf.RoundToInt((transform.position.x - gridOrigin.x) / pitch), 0, maxI);
            cj = tj = Mathf.Clamp(Mathf.RoundToInt((transform.position.z - gridOrigin.z) / pitch), 0, maxJ);
        }
    }

    static float Ground(Vector3 p) => WildTerrain.Active ? WildTerrain.Height(p.x, p.z) : 0f;
    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    static bool Blocked(GiantHealth g) => !g || g.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || GameStartMenu.InMenu;

    void Update()
    {
        if (IsDead || !owner) return;
        var g = GiantHealth.Instance;
        var ctrl = GiantController.Instance;
        if (!g || !ctrl) return;
        Vector3 pos = transform.position;
        float d = Flat(pos, g.transform.position);
        // 거인에게 밟히거나 부딪힘 (마차 앞뒤 어디든)
        float squash = (CivilianManager.Instance ? CivilianManager.Instance.squashRadius : 0.3f) * ctrl.GameScale;
        Vector3 rear = transform.TransformPoint(0f, 0f, -owner.wagonScale * 2f);
        if (d < squash + owner.wagonScale || g.IsTouching(pos + Vector3.up * 3f, owner.wagonScale) || g.IsTouching(rear + Vector3.up * 3f, owner.wagonScale)) { Crush(); return; }
        if (EnemyFreeze.Active) return;

        float h = Mathf.Max(2f, (g.ShoulderY - g.transform.position.y) / 0.837f);
        if (!leaving && (GiantHome.GiantSafe || MilitarySpawner.RetreatLeft > 0f)) { leaving = true; leaveAt = Time.time; }
        if (leaving ? MilitarySpawner.OutOfSight(pos) || Time.time - leaveAt > 20f : d > owner.despawnDistance && MilitarySpawner.OutOfSight(pos)) { Destroy(gameObject); return; }
        if (Time.time >= nextFlip) { nextFlip = Time.time + Random.Range(8f, 16f); circleDir = -circleDir; }

        float speed = Mathf.Max(owner.minSpeed, h * owner.speedPerHeight) * ChaseBoost.Factor(pos);
        if (onRoads) DriveRoads(g, h, speed); else DriveOpen(g, h, d, speed);
        UpdateRiders(g, h, d);
    }

    // ───────────── 마을 안: 도로 교차로를 따라 ─────────────
    Vector3 Node(int i, int j) { var p = new Vector3(gridOrigin.x + i * pitch, 0f, gridOrigin.z + j * pitch); p.y = Ground(p); return p; }

    void DriveRoads(GiantHealth g, float h, float speed)
    {
        if (!moving) PickNode(g.transform.position, h);
        if (!moving) return;
        Vector3 target = Node(ti, tj), to = target - transform.position; to.y = 0f;
        if (to.magnitude < 2f) { lastI = ci; lastJ = cj; ci = ti; cj = tj; moving = false; return; }
        Steer(to, 200f, speed, true);
    }

    // 인접 교차로 중 거인과의 거리가 원하는 거리에 가까운 곳 (되돌아가는 길은 웬만하면 피함). 물러날 땐 거인에게서 가장 먼 곳
    void PickNode(Vector3 gp, float h)
    {
        float prefer = h * owner.preferRangePerHeight, tooClose = h * owner.minRangePerHeight, best = float.MinValue;
        int bi = ci, bj = cj;
        void Consider(int i, int j)
        {
            if (!city.RoadOpen(ci, cj, i, j)) return;
            float dn = Flat(Node(i, j), gp);
            float score = leaving ? dn : -Mathf.Abs(dn - prefer) - (dn < tooClose ? 1000f : 0f);
            if (i == lastI && j == lastJ) score -= pitch;
            score += Random.Range(0f, pitch * 0.3f);
            if (score > best) { best = score; bi = i; bj = j; }
        }
        if (ci > 0) Consider(ci - 1, cj);
        if (ci < maxI) Consider(ci + 1, cj);
        if (cj > 0) Consider(ci, cj - 1);
        if (cj < maxJ) Consider(ci, cj + 1);
        if (bi != ci || bj != cj) { ti = bi; tj = bj; moving = true; }
    }

    // ───────────── 마을 밖: 거인 주위를 원을 그리며 ─────────────
    void DriveOpen(GiantHealth g, float h, float d, float speed)
    {
        Vector3 pos = transform.position;
        Vector3 radial = g.transform.position - pos; radial.y = 0f;
        radial = radial.sqrMagnitude > 0.01f ? radial.normalized : transform.forward;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * circleDir;
        float prefer = h * owner.preferRangePerHeight;
        Vector3 want;
        if (leaving || d < h * owner.minRangePerHeight) want = -radial;
        else want = (tangent + radial * Mathf.Clamp((d - prefer) / prefer * 2.5f, -1.5f, 3f)).normalized;

        // 성벽 안으로 들어가지 않음: 벽을 향하는 성분을 빼서 벽을 따라 달림
        const float margin = 50f;
        Vector3 ahead = pos + want * margin;
        if (owner.InTown(ahead, margin))
        {
            Vector3 n = owner.OutwardFromTown(pos);
            want = want - n * Mathf.Min(0f, Vector3.Dot(want, n));
            if (owner.InTown(pos, margin * 0.5f)) want += n;
            want = want.sqrMagnitude > 0.01f ? want.normalized : n;
        }
        // 앞의 집은 옆으로 비켜 감
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (Physics.SphereCast(pos + Vector3.up * 3f, owner.wagonScale, fwd.normalized, out RaycastHit hit, speed * 0.9f, buildingMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 off = pos - hit.point; off.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, fwd.normalized);
            want = (want + side * (Vector3.Dot(off, side) >= 0f ? 2f : -2f)).normalized;
        }
        Steer(want, 150f, speed, false);
    }

    void Steer(Vector3 dir, float turnSpeed, float speed, bool stopToTurn)
    {
        float dt = Time.deltaTime;
        Vector3 fwd = transform.forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        Quaternion want = Quaternion.LookRotation(dir);
        Quaternion yaw = Quaternion.RotateTowards(Quaternion.LookRotation(fwd), want, turnSpeed * dt);
        Vector3 pos = transform.position;
        if (!stopToTurn || Quaternion.Angle(yaw, want) < 30f) pos += yaw * Vector3.forward * speed * dt;
        // 언덕 따라 앞뒤로 기울고, 달릴 때 살짝 덜컹거림
        float len = owner.wagonScale * 2.5f;
        Vector3 f = yaw * Vector3.forward;
        float slope = Mathf.Atan2(Ground(pos - f * len) - Ground(pos + f * len), len * 2f) * Mathf.Rad2Deg;
        pos.y = Ground(pos);
        transform.SetPositionAndRotation(pos, yaw * Quaternion.Euler(slope, 0f, 0f));
        if (model) model.localPosition = Vector3.up * Mathf.Abs(Mathf.Sin(Time.time * 9f)) * owner.wagonScale * 0.05f;
    }

    // ───────────── 궁수 ─────────────
    void UpdateRiders(GiantHealth g, float h, float d)
    {
        bool canShoot = !leaving && !Blocked(g) && d < h * owner.rangePerHeight && !MilitarySpawner.OutOfSight(transform.position);
        foreach (var r in riders)
        {
            if (!r.t) continue;
            Vector3 to = g.transform.position - r.t.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) r.t.rotation = Quaternion.RotateTowards(r.t.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime); // 달리면서도 거인 쪽을 봄
            if (!canShoot || Time.time < r.nextShot) continue;
            r.nextShot = Time.time + owner.fireInterval * Random.Range(0.8f, 1.25f);
            Vector3 from = r.t.position + Vector3.up * 2.6f * riderScale + r.t.forward * 0.8f;
            Vector3 aim = g.GetAimPoint();
            bool hit = Random.value < owner.accuracy;
            FlyingArrow.Shoot(from, hit ? aim : aim + Random.onUnitSphere * g.Scale * 0.8f);
            if (hit) g.TakeDamage(owner.arrowDamage, false);
        }
    }

    // 마차째 부서짐: 궁수는 땅에 떨어져 납작해지고, 잔해는 잠시 뒤 땅속으로 사라짐
    public void Crush()
    {
        if (IsDead) return;
        IsDead = true;
        Vector3 pos = transform.position;
        int alive = 0;
        foreach (var r in riders)
        {
            if (!r.t) continue;
            alive++;
            r.t.SetParent(null, true);
            Vector3 p = r.t.position; p.y = Ground(p);
            r.t.position = p;
            var anim = r.t.GetComponentInChildren<Animator>();
            if (anim) anim.enabled = false;
            var s = r.t.localScale;
            r.t.localScale = new Vector3(s.x * 1.4f, s.y * 0.12f, s.z * 1.4f);
            BloodSplat.Spawn(p, riderScale);
            SinkAway.Begin(r.t.gameObject, 2f, 0.8f);
        }
        riders.Clear();
        ScoreManager.Add(owner ? owner.wagonPoints + owner.riderPoints * alive : 200, "병력 마차", pos + Vector3.up * 3f);
        CombatFX.Explosion(pos + Vector3.up * 3f, 10f);
        Vector3 sc = transform.localScale;
        transform.localScale = new Vector3(sc.x * 1.15f, sc.y * 0.3f, sc.z * 1.1f);
        foreach (var rd in GetComponentsInChildren<Renderer>())
            foreach (var m in rd.materials) if (m.HasProperty("_Color")) m.color *= 0.35f;
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.3f);
        SinkAway.Begin(gameObject, 3f);
    }
}
