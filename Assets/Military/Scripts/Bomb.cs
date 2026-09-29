using UnityEngine;

// 전투기가 떨어뜨리는 폭탄: 자유낙하, 기수가 진행방향을 향함.
// 지면(또는 건물 옥상)에 닿으면 폭발 → 반경 안 건물 파괴 + 거인에게 거리 비례 피해. 연결된 경고 표시도 함께 제거.
public class Bomb : MonoBehaviour
{
    [Header("낙하")]
    public float gravity = 30f;

    [Header("폭발")]
    public const float DefaultBlastRadius = 90f;
    public float blastRadius = DefaultBlastRadius;
    public float maxDamage = 480f;
    [Range(0, 1)] public float edgeDamageFactor = 0.35f;
    public float explosionSize = 65f;
    [Range(0, 1)] public float buildingShardMultiplier = 0.25f;

    [HideInInspector] public GameObject warningMarker;

    Vector3 vel;
    float halfLength, t;
    bool exploded;
    static readonly Collider[] hits = new Collider[256];

    // 높이 h에서 아래로 vy0(양수) 속도로 떨어질 때 걸리는 시간
    public static float FallTime(float height, float vy0, float g)
        => (-vy0 + Mathf.Sqrt(vy0 * vy0 + 2f * g * Mathf.Max(0f, height))) / g;

    public static Bomb Create(GameObject prefab, Vector3 pos, Vector3 velocity, float scale)
    {
        var root = new GameObject("Bomb");
        root.transform.position = pos;
        var m = Instantiate(prefab, root.transform);
        foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
        m.transform.localScale = Vector3.one * scale;
        m.transform.localRotation = Quaternion.identity; // 모델 기수 = +Y
        var rr = m.GetComponentsInChildren<Renderer>();
        Bounds b = rr[0].bounds; foreach (var r in rr) b.Encapsulate(r.bounds);
        m.transform.position += pos - b.center; // 중심 정렬

        var bomb = root.AddComponent<Bomb>();
        bomb.vel = velocity;
        bomb.halfLength = b.size.y * 0.5f;
        bomb.Orient();
        return bomb;
    }

    void Orient()
    {
        if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.FromToRotation(Vector3.up, vel.normalized);
    }

    void Update()
    {
        if (exploded) return;
        float dt = Time.deltaTime;
        t += dt;
        vel.y -= gravity * dt;
        Vector3 next = transform.position + vel * dt;
        Vector3 nose = next + vel.normalized * halfLength;

        // 건물 옥상에 닿으면 폭발
        if (Physics.Linecast(transform.position, nose, out RaycastHit hit, LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore))
        {
            Explode(hit.point);
            return;
        }
        // 지면에 닿으면 폭발
        if (nose.y <= 0.3f)
        {
            Explode(new Vector3(next.x, 0.3f, next.z));
            return;
        }
        transform.position = next;
        Orient();
        if (t > 20f) { if (warningMarker) Destroy(warningMarker); Destroy(gameObject); }
    }

    void Explode(Vector3 p)
    {
        exploded = true;
        CombatFX.Explosion(p + Vector3.up * explosionSize * 0.2f, explosionSize);
        for (int i = 1; i <= 3; i++)
            CombatFX.Explosion(p + Vector3.up * explosionSize * (0.4f + i * 0.4f), explosionSize * (0.75f - i * 0.12f));
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            CombatFX.Explosion(p + new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * blastRadius * 0.45f, explosionSize * 0.5f);
        }
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(1.1f);

        int n = Physics.OverlapSphereNonAlloc(p, blastRadius, hits, LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (hits[i]) BuildingDestruction.Break(hits[i].gameObject, p, Vector3.zero, false, buildingShardMultiplier);

        var g = GiantHealth.Instance;
        if (g && !g.IsDead)
        {
            float d = g.DistanceToBody(p);
            if (d < blastRadius) g.TakeDamage(maxDamage * Mathf.Lerp(1f, edgeDamageFactor, d / blastRadius));
        }
        if (warningMarker) Destroy(warningMarker);
        Destroy(gameObject);
    }
}
