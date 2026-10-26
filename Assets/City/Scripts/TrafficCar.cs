using UnityEngine;

// 도시 도로를 달리는 자동차: 교차로(그리드)를 따라 직진하다가 교차로마다 무작위로 방향을 골라 계속 달림 (오른쪽 차선).
// 거인 발에 밟히면 찌그러지며 폭발 (Military 레이어 + 충돌 박스, GiantStomp가 Crush 호출). 평소엔 공격하지 않지만,
// UFO 레이저로 거인이 사람 크기가 된 동안엔 가까운 차들이 도로를 벗어나 거인을 들이받음 (StartChase / StopChase).
// 들이받으러 갈 땐 앞길의 사람을 피해 꺾고, 다른 차와는 겹치지 않음.
public class TrafficCar : MonoBehaviour
{
    public CityGenerator city;
    public float speed = 25f;
    public float turnSpeed = 220f;
    [Tooltip("도로 가운데에서 오른쪽으로 떨어진 거리 (m)")] public float laneOffset = 3.5f;

    [Tooltip("켜면 교차로마다 거인에게서 가장 멀어지는 길을 골라 도망감 (인질 이벤트 차량)")] public bool flee;

    float emergeT = 1f, emergeTime; Vector3 emergeFrom;
    // Init 다음에 부름: 이 자리(부서진 건물)에서 출발해 emergeTime초 동안 도로로 빠져나옴
    public void EmergeFrom(Vector3 from, float time) { emergeFrom = from; emergeTime = time; emergeT = 0f; transform.position = from; }

    public bool IsDead { get; private set; }

    // ───── 서로 겹치지 않기 ─────
    public static readonly System.Collections.Generic.List<TrafficCar> All = new System.Collections.Generic.List<TrafficCar>();
    [Tooltip("차 길이(m). 생성 때 모델 크기로 채워짐")] public float length = 10f;
    [Tooltip("앞차를 살피는 거리(m)")] public float lookAhead = 45f;
    [Tooltip("앞차와 이 거리(m, 차체 사이) 이내면 완전히 멈춤")] public float stopGap = 3f;
    [Tooltip("앞차와 이 거리(m) 이내면 점점 감속")] public float followGap = 22f;
    [Tooltip("옆으로 이 거리(m) 이내에 있으면 같은 길을 가는 것으로 봄")] public float laneWidth = 6f;
    float curSpeed, limit = 1f, nextCheck;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // 앞에 있는 차에 맞춰 속도 배율(0~1)을 정함. 같은 방향 앞차는 항상 피하고,
    // 교차로에서 엇갈리는 차는 인스턴스 ID가 작은 쪽이 먼저 지나감 (서로 양보하다 멈추는 교착 방지)
    float CheckAhead()
    {
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Right(fwd), pos = transform.position;
        int myId = GetInstanceID();
        float best = 1f;
        foreach (var o in All)
        {
            if (!o || o == this || o.IsDead) continue;
            Vector3 d = o.transform.position - pos; d.y = 0f;
            float f = Vector3.Dot(d, fwd);
            if (f <= 0f || f > lookAhead) continue;
            if (Mathf.Abs(Vector3.Dot(d, right)) > laneWidth) continue;
            Vector3 of = o.transform.forward; of.y = 0f;
            bool same = Vector3.Dot(of, fwd) > 0.5f;
            if (!same && o.GetInstanceID() > myId) continue; // 엇갈리는 차는 ID가 작은 쪽에게만 양보
            float gap = f - (length + o.length) * 0.5f;
            best = Mathf.Min(best, Mathf.Clamp01((gap - stopGap) / Mathf.Max(0.1f, followGap - stopGap)));
        }
        return best;
    }

    Vector3 gridOrigin; float pitch; int maxI, maxJ;
    int ci, cj, ti, tj, prevI = -1, prevJ = -1;
    float t;                 // 현재 구간(ci,cj → ti,tj)에서 진행 비율
    Vector3 offset;          // 현재 차선 오프셋 (차선이 바뀔 때 부드럽게 이동)

    // 교차로 (i, j)에 세워서 시작
    public void Init(CityGenerator c, int i, int j)
    {
        city = c;
        pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        gridOrigin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        maxI = city.blocksX; maxJ = city.blocksZ;
        ci = i; cj = j;
        curSpeed = speed;
        PickNext();
        Vector3 dir = Dir();
        offset = Right(dir) * laneOffset;
        transform.position = Node(ci, cj) + offset;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    Vector3 Node(int i, int j) => new Vector3(gridOrigin.x + i * pitch, 0f, gridOrigin.z + j * pitch);
    Vector3 Dir() { Vector3 d = Node(ti, tj) - Node(ci, cj); d.y = 0; return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward; }
    static Vector3 Right(Vector3 dir) => Vector3.Cross(Vector3.up, dir);

    // 다음 교차로 고르기: 온 길로 되돌아가지 않고 (막다른 길이면 되돌아감), 막힌 도로(합친 블록)는 지나지 않음
    void PickNext()
    {
        var opts = new System.Collections.Generic.List<Vector2Int>(4);
        if (ci > 0 && city.RoadOpen(ci, cj, ci - 1, cj)) opts.Add(new Vector2Int(ci - 1, cj));
        if (ci < maxI && city.RoadOpen(ci, cj, ci + 1, cj)) opts.Add(new Vector2Int(ci + 1, cj));
        if (cj > 0 && city.RoadOpen(ci, cj, ci, cj - 1)) opts.Add(new Vector2Int(ci, cj - 1));
        if (cj < maxJ && city.RoadOpen(ci, cj, ci, cj + 1)) opts.Add(new Vector2Int(ci, cj + 1));
        if (opts.Count > 1) opts.RemoveAll(o => o.x == prevI && o.y == prevJ);
        if (opts.Count == 0) { ti = ci; tj = cj; return; }
        var n = opts[Random.Range(0, opts.Count)];
        var giant = flee ? GiantHealth.Instance : null;
        if (giant)
        {
            float best = -1f; Vector3 gp = giant.transform.position; gp.y = 0f;
            foreach (var o in opts) { float d = (Node(o.x, o.y) - gp).sqrMagnitude; if (d > best) { best = d; n = o; } }
        }
        ti = n.x; tj = n.y; t = 0f;
    }

    // ───── 사람 크기가 된 거인 들이받기 (GiantShrink가 켜고 끔) ─────
    public bool Chasing { get; private set; }
    float chaseSpeed, backoffUntil, steer;
    static int buildingMask = -1;

    public void StartChase(float spd) { if (IsDead) return; Chasing = true; chaseSpeed = spd; backoffUntil = 0f; }

    // 추격을 멈추고 가장 가까운 교차로 길로 부드럽게 돌아감
    public void StopChase()
    {
        if (!Chasing) return;
        Chasing = false;
        if (!city) return;
        Vector3 p = transform.position;
        ci = Mathf.Clamp(Mathf.RoundToInt((p.x - gridOrigin.x) / pitch), 0, maxI);
        cj = Mathf.Clamp(Mathf.RoundToInt((p.z - gridOrigin.z) / pitch), 0, maxJ);
        prevI = prevJ = -1;
        PickNext();
        curSpeed = speed * 0.5f;
        EmergeFrom(p, 2f);
    }

    [Tooltip("차 너비(m). 0이면 길이 × 0.45")] public float width = 0f;
    float HalfWidth => (width > 0f ? width : length * 0.45f) * 0.5f;

    // 바닥에서 본 차 모양 = 앞뒤로 긴 캡슐 (가운데 선분 a~b + 반폭)
    void Segment(out Vector3 a, out Vector3 b)
    {
        Vector3 f = transform.forward; f.y = 0f; f.Normalize();
        float h = Mathf.Max(0f, length * 0.5f - HalfWidth);
        Vector3 c = transform.position; c.y = 0f;
        a = c - f * h; b = c + f * h;
    }

    // 두 선분 사이 가장 가까운 두 점
    static void ClosestPoints(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a < 1e-4f && e < 1e-4f) { s = t = 0f; }
        else if (a < 1e-4f) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e < 1e-4f) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > 1e-4f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        c1 = p1 + d1 * s; c2 = p2 + d2 * t;
    }

    // 다른 차와 겹친 만큼 밀어낼 양. away = 가까이 있는 차에서 멀어지는 방향 (가까울수록 큼, 핸들 꺾기용)
    Vector3 CarSeparation(out Vector3 away)
    {
        Vector3 push = Vector3.zero; away = Vector3.zero;
        Segment(out Vector3 a1, out Vector3 b1);
        Vector3 me = transform.position; me.y = 0f;
        foreach (var o in All)
        {
            if (!o || o == this || o.IsDead) continue;
            Vector3 op = o.transform.position; op.y = 0f;
            float far = (length + o.length) * 0.5f + 6f;
            if ((op - me).sqrMagnitude > far * far) continue;
            o.Segment(out Vector3 a2, out Vector3 b2);
            ClosestPoints(a1, b1, a2, b2, out Vector3 c1, out Vector3 c2);
            Vector3 d = c1 - c2; float dist = d.magnitude;
            if (dist < 0.01f) { d = me - op; dist = 0f; if (d.sqrMagnitude < 0.0001f) d = transform.right; }
            Vector3 n = d.normalized;
            float minD = HalfWidth + o.HalfWidth + 0.4f;
            if (dist < minD) push += n * (minD - dist);
            if (dist < minD + 5f) away += n * (1f - Mathf.Max(0f, dist - minD) / 5f);
        }
        return push;
    }

    // 앞길에 있는 사람(시민·몰려든 공격자)을 피해 꺾을 방향. 거인 바로 옆(때리고 있는 사람)은 무시하고 그대로 들이받음
    Vector3 AvoidPeople(Vector3 pos, Vector3 fwd, Vector3 gp, float ignoreR, out float slow)
    {
        slow = 1f;
        Vector3 steerV = Vector3.zero, right = Right(fwd);
        float look = length * 0.5f + chaseSpeed * 0.8f, side = HalfWidth + 1.2f;
        void Check(Vector3 p, ref Vector3 sv, ref float sl)
        {
            Vector3 d = p - pos; d.y = 0f;
            float fd = Vector3.Dot(d, fwd);
            if (fd < -length * 0.3f || fd > look) return;
            float sd = Vector3.Dot(d, right);
            if (Mathf.Abs(sd) > side) return;
            Vector3 g = p - gp; g.y = 0f;
            if (g.sqrMagnitude < ignoreR * ignoreR) return;
            float urgency = 1f - Mathf.Clamp01((fd - length * 0.5f) / look);
            sv += right * (sd >= 0f ? -1f : 1f) * (0.5f + urgency * 2f);
            if (fd < length * 0.5f + 4f) sl = Mathf.Min(sl, 0.35f); // 바로 앞이면 속도를 줄이며 비켜 감
        }
        var mgr = CivilianManager.Instance;
        if (mgr) foreach (var c in mgr.Civilians) if (c) Check(c.transform.position, ref steerV, ref slow);
        var s = GiantShrink.Instance;
        if (s) foreach (var w in s.Walkers) if (w) Check(w.transform.position, ref steerV, ref slow);
        return steerV;
    }

    void ChaseUpdate()
    {
        var g = GiantHealth.Instance;
        var s = GiantShrink.Instance;
        if (!g || g.IsDead || !s) return;
        if (buildingMask < 0) buildingMask = LayerMask.GetMask("Building");
        float dt = Time.deltaTime;
        Vector3 pos = transform.position, gp = g.transform.position;
        Vector3 to = gp - pos; to.y = 0f;
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        CarSeparation(out Vector3 awayCars);

        if (Time.time < backoffUntil)
        {
            // 들이받은 뒤 잠깐 뒤로 물러났다가 다시 돌진
            pos -= fwd * chaseSpeed * 0.35f * dt;
        }
        else
        {
            // 거인 쪽으로 핸들을 꺾되, 사람·다른 차는 피하고, 앞이 건물로 막혀 있으면 좌우로 비켜 감
            Vector3 want = to.sqrMagnitude > 0.01f ? to.normalized : fwd;
            want += AvoidPeople(pos, fwd, gp, Mathf.Max(4f, s.punchReach * 2.2f), out float slowPeople) + awayCars * 1.5f;
            want.y = 0f;
            if (want.sqrMagnitude < 0.0001f) want = fwd;
            want.Normalize();
            float look = Mathf.Max(6f, length);
            Vector3 eye = pos + Vector3.up * 1.5f;
            if (Physics.Raycast(eye, want, look, buildingMask, QueryTriggerInteraction.Ignore))
            {
                if (steer == 0f || Physics.Raycast(eye, Quaternion.Euler(0f, steer, 0f) * want, look, buildingMask, QueryTriggerInteraction.Ignore))
                    steer = !Physics.Raycast(eye, Quaternion.Euler(0f, 60f, 0f) * want, look, buildingMask, QueryTriggerInteraction.Ignore) ? 60f
                          : !Physics.Raycast(eye, Quaternion.Euler(0f, -60f, 0f) * want, look, buildingMask, QueryTriggerInteraction.Ignore) ? -60f : 0f;
                want = Quaternion.Euler(0f, steer, 0f) * want;
            }
            else steer = 0f;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(want), turnSpeed * 0.6f * dt);
            fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            bool blocked = Physics.Raycast(eye, fwd, length * 0.6f, buildingMask, QueryTriggerInteraction.Ignore);
            pos += fwd * chaseSpeed * (blocked ? 0.15f : slowPeople) * dt;

            // 차 앞부분이 거인에게 닿으면 들이받음
            float giantR = g.GetComponent<CharacterController>() ? g.GetComponent<CharacterController>().radius : 1f;
            if (to.magnitude < length * 0.5f + giantR)
            {
                s.OnRammed(this, fwd);
                backoffUntil = Time.time + 0.9f;
            }
        }
        transform.position = pos;
        // 다른 차와 겹치면 겹친 만큼 밀려남
        Vector3 push = CarSeparation(out _);
        if (push.sqrMagnitude > 0.0001f) transform.position = pos + push;
    }

    void Update()
    {
        if (IsDead || !city) return;
        if (Chasing) { ChaseUpdate(); return; }
        if (ti == ci && tj == cj) { PickNext(); return; }
        Vector3 a = Node(ci, cj), b = Node(ti, tj);
        float len = Vector3.Distance(a, b);
        // 앞차·교차로 차량과 겹치지 않게 속도 조절 (0.1초마다 확인, 감속은 빠르게 · 가속은 천천히)
        if (Time.time >= nextCheck) { nextCheck = Time.time + 0.1f + Random.value * 0.03f; limit = flee ? 1f : CheckAhead(); }
        float want = speed * limit;
        curSpeed = Mathf.MoveTowards(curSpeed, want, (want < curSpeed ? 80f : 25f) * Time.deltaTime);
        t += curSpeed * Time.deltaTime / Mathf.Max(1f, len);
        if (t >= 1f)
        {
            prevI = ci; prevJ = cj; ci = ti; cj = tj;
            PickNext();
            a = Node(ci, cj); b = Node(ti, tj);
            t = 0f;
        }
        Vector3 dir = (b - a).normalized;
        offset = Vector3.MoveTowards(offset, Right(dir) * laneOffset, 14f * Time.deltaTime);
        Vector3 pos = Vector3.Lerp(a, b, t) + offset;
        pos.y = transform.position.y;
        if (emergeT < 1f) // 건물 자리에서 도로로 빠져나오는 중
        {
            emergeT += Time.deltaTime / Mathf.Max(0.1f, emergeTime);
            float k = Mathf.Clamp01(emergeT); k = k * k * (3f - 2f * k);
            Vector3 from = emergeFrom; from.y = pos.y;
            pos = Vector3.Lerp(from, pos, k);
        }
        transform.position = pos;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    // 거인에게 밟힘: 납작하게 찌그러지며 폭발한 뒤 잠시 뒤 사라짐
    public void Crush()
    {
        if (IsDead) return;
        IsDead = true;
        enabled = false;
        ScoreManager.Add(50, "CAR");
        CombatFX.Explosion(transform.position + Vector3.up * 2f, 8f);
        Vector3 sc = transform.localScale;
        transform.localScale = new Vector3(sc.x * 1.15f, sc.y * 0.2f, sc.z * 1.1f);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.35f;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Destroy(gameObject, 6f);
    }
}
