using UnityEngine;

// 마법 구슬 (마법사): 빛나는 구슬이 거인 쪽으로 천천히 휘어 날아감. 거인 몸에 닿으면 피해 + onHit (얼음 주문이면 느려짐),
// 건물·땅에 닿거나 수명이 끝나면 빛을 흩뿌리며 사라짐. 모양은 MagicFX 빛 입자로만 그림.
public class MagicBolt : MonoBehaviour
{
    Vector3 vel, offset;
    float damage, homing, dieAt, size;
    Color col;
    System.Action onHit;
    static int buildingMask = -1;
    static int BuildingMask => buildingMask >= 0 ? buildingMask : (buildingMask = LayerMask.GetMask("Building"));

    public static MagicBolt Cast(Vector3 pos, Vector3 dir, float speed, float damage, float homing, Color col, float size, System.Action onHit = null)
    {
        var b = new GameObject("MagicBolt").AddComponent<MagicBolt>();
        b.transform.position = pos;
        b.vel = dir.normalized * speed; b.damage = damage; b.homing = homing; b.col = col; b.size = size; b.onHit = onHit;
        b.dieAt = Time.time + 8f;
        b.offset = Random.insideUnitSphere * size * 2f; // 조금씩 어긋나게 → 가끔 빗나감
        ThreatGlow.Add(b.transform, size * 4f);
        return b;
    }

    void Update()
    {
        var g = GiantHealth.Instance;
        Vector3 p = transform.position;
        Draw(p);
        if (EnemyFreeze.Active) { dieAt += Time.deltaTime; return; }
        float dt = Time.deltaTime;
        if (homing > 0f && g && !g.IsDead)
        {
            Vector3 target = g.Center + Vector3.up * g.Scale * 0.25f + offset;
            vel = Vector3.RotateTowards(vel, (target - p).normalized * vel.magnitude, homing * dt, 0f);
        }
        Vector3 next = p + vel * dt;
        if (g && !g.IsDead && (g.IsInsideBody(next, size * 0.5f) || g.IsInsideBody((p + next) * 0.5f, size * 0.5f)))
        {
            g.TakeDamage(damage, false);
            onHit?.Invoke();
            Pop(next);
            return;
        }
        if (Physics.Linecast(p, next, out RaycastHit hit, BuildingMask, QueryTriggerInteraction.Ignore)) { Pop(hit.point); return; }
        float ground = WildTerrain.Active ? WildTerrain.Height(next.x, next.z) : 0f;
        if (next.y <= ground + 0.3f || Time.time > dieAt) { Pop(next); return; }
        transform.position = next;
    }

    // 밝은 속 + 퍼지는 겉빛 + 뒤로 흩어지는 꼬리
    void Draw(Vector3 p)
    {
        float flick = 0.85f + 0.15f * Mathf.Sin(Time.time * 40f);
        MagicFX.Glow(p, Vector3.zero, Color.Lerp(col, Color.white, 0.6f), size * flick, 0.05f);
        MagicFX.Glow(p, Vector3.zero, col, size * 2.2f * flick, 0.05f);
        MagicFX.Glow(p + Random.insideUnitSphere * size * 0.4f, -vel * 0.05f + Random.insideUnitSphere * size, col, size * Random.Range(0.5f, 0.9f), 0.45f);
    }

    void Pop(Vector3 p)
    {
        MagicFX.Burst(p, col, size * 1.4f, 22);
        Destroy(gameObject);
    }
}
