using UnityEngine;

// UFO: 거인이 appearLevel(25)이 되면 나타나, 거인 머리 위 높은 곳을 맴돌며 땅에 레이저를 쏨 (fireDuration초 쏘고 cooldown초 쉼을 반복).
//  - 레이저가 닿은 지점(보라색 원)은 거인을 향해 따라옴 (거인 달리기 속도보다 조금 느림)
//  - 거인이 그 원 안에 들어가면 GiantShrink로 일반 사람 크기가 됨 → 15초 동안 적·사람·차가 공격해 옴
//  - 맞히면 위로 날아가 사라졌다가 returnDelay(30초~2분) 중 무작위 시간 뒤 다시 나타남. 쏠 땐 거인 바로 근처부터 시작
// UFO는 공격받지 않음 (충돌 박스 없음).
public class UfoEnemy : MonoBehaviour
{
    [Tooltip("UFO 모델 프리팹 (FlexUnit UFO)")] public GameObject ufoPrefab;

    [Header("비행")]
    [Tooltip("UFO 폭 = 거인 키 × 이 비율")] public float sizeRatio = 1.1f;
    [Tooltip("나는 높이 = 거인 키 × 이 비율")] public float altitudeRatio = 1.4f;
    [Tooltip("거인 주변을 도는 반경 = 거인 키 × 이 비율")] public float orbitRadiusRatio = 2.2f;
    [Tooltip("화면 위쪽에서 좌우로 오가는 속도")] public float orbitSpeed = 14f;
    [Tooltip("UFO 자체 회전 속도 (도/초)")] public float spinSpeed = 25f;

    [Header("등장")]
    [Tooltip("거인이 이 레벨이 되면 처음 등장")] public int appearLevel = 25;
    [Tooltip("거인을 맞혀 작게 만든 뒤 사라졌다가 다시 나타나기까지 걸리는 시간(초) 범위 (최소, 최대)")] public Vector2 returnDelay = new Vector2(30f, 120f);
    [Tooltip("사라질 때 위로 날아가 버리는 시간(초)")] public float departTime = 2f;

    [Header("레이저")]
    [Tooltip("UFO가 나타난 뒤 레이저를 쏘기 시작할 때까지(초)")] public float firstShotDelay = 8f;
    [Tooltip("한 번에 레이저를 쏘는 시간(초). 그 안에 못 맞히면 쉼")] public float fireDuration = 10f;
    [Tooltip("다 쏜 뒤 다시 쏘기까지 쉬는 시간(초)")] public float cooldown = 10f;
    [Tooltip("레이저 지점이 거인을 따라오는 속도 = 거인 기본 이동(달리기) 속도 × 이 비율 (1보다 작으면 달려서 피할 수 있음)")] public float chaseSpeedRatio = 0.8f;
    [Tooltip("레이저를 다시 쏠 때 거인에게서 떨어진 거리 = 거인 키 × 이 비율")] public float startDistanceRatio = 0.8f;
    [Tooltip("레이저 원 반경 = 거인 키 × 이 비율")] public float beamRadiusRatio = 0.22f;
    public Color beamColor = new Color(0.75f, 0.35f, 1f, 1f);

    enum State { Idle, Firing }
    State state;
    float nextShotAt, angle, swing, baseWidth = 12f, normalHeight = 25f, firedFor;
    bool present, departing;      // 하늘에 나와 있는지 / 맞힌 뒤 날아가 버리는 중
    float returnAt = -1f, departAt; // returnAt < 0: 아직 등장 레벨이 안 됨
    Transform ufo;
    Vector3 target; float beamRadius;
    GameObject ring, glow, core; Material mat;
    Mesh ringMesh, glowMesh, coreMesh;

    public static bool Firing { get; private set; }

    void Start()
    {
        nextShotAt = Time.time + firstShotDelay;
        angle = Random.Range(0f, 360f);
        if (!ufoPrefab) return;
        ufo = Instantiate(ufoPrefab, transform).transform;
        ufo.name = "UFO";
        foreach (var c in ufo.GetComponentsInChildren<Collider>()) Destroy(c);
        var rs = ufo.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); baseWidth = Mathf.Max(1f, Mathf.Max(b.size.x, b.size.z)); }
        var g = GiantHealth.Instance;
        if (g) { normalHeight = Mathf.Max(1f, g.Height); ufo.position = Desired(g, 0f); }

        mat = new Material(Shader.Find("Sprites/Default"));
        ring = MakeObj("UfoLaserRing"); glow = MakeObj("UfoLaserGlow"); core = MakeObj("UfoLaserCore");
        glowMesh = WarningZone.Wall(Color.white, Color.white);
        coreMesh = WarningZone.Wall(Color.white, Color.white);
        glow.GetComponent<MeshFilter>().sharedMesh = glowMesh;
        core.GetComponent<MeshFilter>().sharedMesh = coreMesh;
        Show(false);
        ufo.gameObject.SetActive(false); // 등장 레벨이 될 때까지 숨김
    }

    // 나타남: 거인에게서 먼 하늘에서 날아와 자리를 잡고, 잠시 뒤 쏘기 시작
    void Arrive(GiantHealth g)
    {
        present = true; departing = false; state = State.Idle;
        normalHeight = Mathf.Max(1f, g.Height);
        Vector3 gp = g.transform.position; gp.y = 0f;
        float a = Random.Range(0f, Mathf.PI * 2f);
        ufo.position = gp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * normalHeight * 8f + Vector3.up * normalHeight * 4f;
        ufo.gameObject.SetActive(true);
        nextShotAt = Time.time + firstShotDelay;
    }

    // 거인을 맞힘: 위로 날아가 사라지고, returnDelay 범위의 무작위 시간 뒤 다시 나타남
    void Depart()
    {
        state = State.Idle; Firing = false; Show(false);
        departing = true; departAt = Time.time;
        returnAt = Time.time + Random.Range(returnDelay.x, returnDelay.y);
    }

    void OnDisable() { Firing = false; }

    GameObject MakeObj(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        return go;
    }

    void Show(bool on)
    {
        if (ring) ring.SetActive(on);
        if (glow) glow.SetActive(on);
        if (core) core.SetActive(on);
    }

    // 거인 주변 궤도 위 목표 위치
    Vector3 Desired(GiantHealth g, float a)
    {
        float H = normalHeight, rad = a * Mathf.Deg2Rad;
        Vector3 gp = g.transform.position; gp.y = 0f;
        return gp + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * H * orbitRadiusRatio + Vector3.up * H * altitudeRatio;
    }

    Vector3 BeamOrigin => ufo ? ufo.position - Vector3.up * ufo.lossyScale.y * 2.5f : transform.position;

    // 거인 기본 이동(달리기) 속도 (m/s). Shift를 누르면 걷기라 평소엔 이 속도로 움직임
    float GiantSpeed()
    {
        var c = GiantController.Instance;
        return c ? c.runSpeed * c.GameScale : normalHeight * 1.8f;
    }

    void Update()
    {
        var g = GiantHealth.Instance;
        if (!ufo || !g || GameStartMenu.InMenu) { Firing = false; return; }
        float dt = Time.deltaTime;
        bool shrunk = GiantShrink.Active;

        if (!present)
        {
            // 처음엔 거인이 appearLevel이 되면, 맞힌 뒤엔 정해 둔 시간이 지나면 다시 나타남 (작아져 있는 동안은 기다림)
            Firing = false;
            if (returnAt < 0f && LevelSystem.Instance && LevelSystem.Instance.Level >= appearLevel) returnAt = Time.time;
            if (returnAt >= 0f && Time.time >= returnAt && !shrunk && !g.IsDead) Arrive(g);
            return;
        }
        if (departing)
        {
            // 위로 빠르게 솟구치며 작아져 사라짐
            Firing = false;
            float k = (Time.time - departAt) / Mathf.Max(0.1f, departTime);
            ufo.position += Vector3.up * normalHeight * 3f * dt * (1f + k * 3f);
            ufo.localScale *= Mathf.Max(0f, 1f - dt * 1.2f);
            ufo.Rotate(0f, spinSpeed * 6f * dt, 0f, Space.World);
            if (k >= 1f) { departing = false; present = false; ufo.gameObject.SetActive(false); }
            return;
        }
        if (!shrunk) normalHeight = Mathf.Max(1f, g.Height); // 작아진 동안엔 원래 키 기준 유지

        // 크기·비행 (카메라 반대편 화면 위쪽에서 좌우로 오가며 거인을 따라감)
        float wantScale = normalHeight * sizeRatio / baseWidth;
        ufo.localScale = Vector3.one * Mathf.MoveTowards(ufo.localScale.x, wantScale, wantScale * dt);
        swing += orbitSpeed * (state == State.Idle ? 1f : 0.4f) * dt;
        var cam = Camera.main;
        Vector3 cf = cam ? cam.transform.forward : Vector3.forward; cf.y = 0f;
        float baseAngle = cf.sqrMagnitude > 0.001f ? Mathf.Atan2(cf.z, cf.x) * Mathf.Rad2Deg : 90f;
        angle = Mathf.MoveTowardsAngle(angle, baseAngle + Mathf.Sin(swing * Mathf.Deg2Rad) * 55f, 90f * dt);
        float follow = Mathf.Max(40f, normalHeight * 2f);
        Vector3 want = Desired(g, angle) + Vector3.up * Mathf.Sin(Time.time * 1.3f) * normalHeight * 0.05f;
        ufo.position = Vector3.MoveTowards(ufo.position, want, follow * dt);
        ufo.Rotate(0f, spinSpeed * dt, 0f, Space.World);

        bool canFire = !shrunk && !g.IsDead && !EnemyFreeze.Active && !GiantHome.GiantSafe
                       && !(HostageEvent.Instance && HostageEvent.Instance.Busy);
        Vector3 gp = g.transform.position; gp.y = 0f;

        switch (state)
        {
            case State.Idle:
                Firing = false;
                if (Time.time < nextShotAt || !canFire) break;
                // 거인 바로 근처(UFO 쪽)에 쏘기 시작 → 곧장 피해야 함
                Vector3 fromUfo = ufo.position - gp; fromUfo.y = 0f;
                if (fromUfo.sqrMagnitude < 0.01f) fromUfo = Vector3.forward;
                target = gp + fromUfo.normalized * normalHeight * startDistanceRatio;
                state = State.Firing; firedFor = 0f;
                Show(true);
                break;

            case State.Firing:
                if (!canFire)
                {
                    // 보급상자 정지·인질 이벤트·집 안 등: 잠시 레이저를 끄고 그 자리에서 기다림
                    Show(false); Firing = false;
                    if (shrunk || g.IsDead) state = State.Idle;
                    break;
                }
                // 정해진 시간 동안 쏘면 멈추고 쉼
                firedFor += dt;
                if (firedFor >= fireDuration)
                {
                    state = State.Idle; Firing = false; Show(false);
                    nextShotAt = Time.time + cooldown;
                    break;
                }
                Show(true); Firing = true;
                beamRadius = normalHeight * beamRadiusRatio;
                // 레이저 지점이 거인을 느리게 따라옴
                target = Vector3.MoveTowards(target, gp, GiantSpeed() * chaseSpeedRatio * dt);
                float flicker = 0.85f + 0.15f * Mathf.Sin(Time.time * 37f);
                DrawRing(new Color(beamColor.r, beamColor.g, beamColor.b, 0.55f + 0.3f * Mathf.Sin(Time.time * 9f)));
                DrawBeam(glow, glowMesh, beamRadius * 0.55f * flicker, new Color(beamColor.r, beamColor.g, beamColor.b, 0.28f));
                DrawBeam(core, coreMesh, beamRadius * 0.16f, new Color(1f, 0.85f, 1f, 0.9f));

                // 거인 몸이 레이저 원에 닿으면 축소
                float giantR = g.GetComponent<CharacterController>() ? g.GetComponent<CharacterController>().radius : 0f;
                if ((gp - target).magnitude <= beamRadius + giantR * 0.5f && GiantShrink.TryBegin())
                    Depart();
                break;
        }
    }

    void DrawRing(Color c)
    {
        if (ringMesh) Destroy(ringMesh);
        ringMesh = WarningZone.Annulus(0.82f, 1f, c, c);
        ring.GetComponent<MeshFilter>().sharedMesh = ringMesh;
        ring.transform.position = target + Vector3.up * 0.3f;
        ring.transform.rotation = Quaternion.identity;
        ring.transform.localScale = Vector3.one * beamRadius;
    }

    // UFO 밑에서 목표 지점까지 원기둥 레이저 (Wall 메시: 반지름 1, 높이 0~1)
    void DrawBeam(GameObject go, Mesh mesh, float radius, Color c)
    {
        var cols = mesh.colors;
        for (int i = 0; i < cols.Length; i++) cols[i] = c;
        mesh.colors = cols;
        Vector3 top = BeamOrigin, bottom = target;
        Vector3 d = top - bottom;
        go.transform.position = bottom;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        go.transform.localScale = new Vector3(radius, d.magnitude, radius);
    }
}
