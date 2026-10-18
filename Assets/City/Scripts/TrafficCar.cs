using UnityEngine;

// 도시 도로를 달리는 자동차: 교차로(그리드)를 따라 직진하다가 교차로마다 무작위로 방향을 골라 계속 달림 (오른쪽 차선).
// 거인 발에 밟히면 찌그러지며 폭발 (Military 레이어 + 충돌 박스, GiantStomp가 Crush 호출). 적이 아니라서 공격은 하지 않음.
public class TrafficCar : MonoBehaviour
{
    public CityGenerator city;
    public float speed = 25f;
    public float turnSpeed = 220f;
    [Tooltip("도로 가운데에서 오른쪽으로 떨어진 거리 (m)")] public float laneOffset = 3.5f;

    public bool IsDead { get; private set; }

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
        ti = n.x; tj = n.y; t = 0f;
    }

    void Update()
    {
        if (IsDead || !city) return;
        if (ti == ci && tj == cj) { PickNext(); return; }
        Vector3 a = Node(ci, cj), b = Node(ti, tj);
        float len = Vector3.Distance(a, b);
        t += speed * Time.deltaTime / Mathf.Max(1f, len);
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
