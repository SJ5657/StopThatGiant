using UnityEngine;

// 번개 주문 (폭풍 마법사): 땅에 노란 원으로 떨어질 곳을 알려 주고, delay초 뒤 하늘에서 번개가 내리꽂힘.
// 그때 거인이 원 안에 있으면 피해. 원은 시전할 때 거인이 갈 곳(속도로 예측)에 그려지므로 방향을 틀면 피할 수 있음.
public class LightningStrike : MonoBehaviour
{
    Vector3 at; float radius, damage, strikeAt, height;
    bool struck; float struckAt;
    GameObject ring; Mesh ringMesh;
    LineRenderer bolt;
    static readonly Color Warn = new Color(1f, 0.9f, 0.3f, 1f);
    static readonly Color BoltCol = new Color(0.85f, 0.9f, 1f, 1f);

    public static void Cast(Vector3 at, float radius, float damage, float delay, float height)
    {
        var s = new GameObject("LightningStrike").AddComponent<LightningStrike>();
        at.y = WildTerrain.Active ? WildTerrain.Height(at.x, at.z) : 0f;
        s.at = at; s.radius = radius; s.damage = damage; s.strikeAt = Time.time + delay; s.height = height;
    }

    void Start()
    {
        ring = new GameObject("LightningWarn");
        ring.transform.SetParent(transform, false);
        ring.AddComponent<MeshFilter>();
        var mr = ring.AddComponent<MeshRenderer>(); mr.sharedMaterial = WarningZone.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        bolt = new GameObject("LightningBolt").AddComponent<LineRenderer>();
        bolt.transform.SetParent(transform, false);
        bolt.sharedMaterial = WarningZone.material;
        bolt.positionCount = 14;
        bolt.startColor = bolt.endColor = BoltCol;
        bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bolt.enabled = false;
    }

    void Update()
    {
        if (!struck)
        {
            if (EnemyFreeze.Active) { strikeAt += Time.deltaTime; }
            // 경고 원: 점점 빨리 깜빡이며 안쪽이 차오름
            float left = strikeAt - Time.time;
            float a = 0.35f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * (6f + 10f / Mathf.Max(0.2f, left))));
            float fill = Mathf.Clamp01(1f - left / 1.5f);
            if (ringMesh) Destroy(ringMesh);
            ringMesh = WarningZone.Annulus(Mathf.Lerp(0.85f, 0f, fill), 1f, new Color(Warn.r, Warn.g, Warn.b, a * 0.5f), new Color(Warn.r, Warn.g, Warn.b, a));
            ring.GetComponent<MeshFilter>().sharedMesh = ringMesh;
            ring.transform.position = at + Vector3.up * 0.3f;
            ring.transform.localScale = Vector3.one * radius;
            if (left <= 0f) Strike();
            return;
        }
        // 번개: 0.35초 동안 모양을 바꾸며 번쩍이다 사라짐
        float t = Time.time - struckAt;
        if (t > 0.35f) { Destroy(gameObject); return; }
        Jag();
        Color c = BoltCol; c.a = 1f - t / 0.35f;
        bolt.startColor = bolt.endColor = c;
    }

    void Strike()
    {
        struck = true; struckAt = Time.time;
        ring.SetActive(false);
        bolt.enabled = true;
        bolt.startWidth = Mathf.Max(1f, radius * 0.2f); bolt.endWidth = bolt.startWidth * 0.5f;
        Jag();
        MagicFX.Burst(at + Vector3.up, BoltCol, radius * 0.5f, 30);
        CombatFX.Explosion(at + Vector3.up * 2f, radius * 0.4f);
        var g = GiantHealth.Instance;
        if (g && !g.IsDead)
        {
            Vector3 gp = g.transform.position; gp.y = 0f;
            Vector3 p = at; p.y = 0f;
            var cc = g.GetComponent<CharacterController>();
            float body = cc ? cc.radius * g.transform.lossyScale.x : 2f;
            if (Vector3.Distance(gp, p) <= radius + body * 0.5f) g.TakeDamage(damage, true);
            else if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.2f);
        }
    }

    // 하늘에서 땅까지 지그재그
    void Jag()
    {
        int n = bolt.positionCount;
        Vector3 top = at + Vector3.up * height + Random.insideUnitSphere * radius;
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(top, at, k);
            if (i > 0 && i < n - 1) p += new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * radius * 0.35f;
            bolt.SetPosition(i, p);
        }
    }

    void OnDestroy() { if (ringMesh) Destroy(ringMesh); }
}
