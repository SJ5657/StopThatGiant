using UnityEngine;

// 초록 건물이 무너진 자리에 생기는 회복 영역: 바닥 원 + 테두리 + 반투명 벽.
// 거인이 원 안에 있으면 초당 healPerSecond만큼 HP 회복. duration초 뒤 서서히 사라짐.
public class HealZone : MonoBehaviour
{
    static Mesh ringMesh, discMesh, wallMesh;

    float radius, duration, healPerSecond, born;
    MeshRenderer[] renderers;
    MaterialPropertyBlock mpb;

    public static HealZone Create(Vector3 center, float radius, float duration, float healPerSecond)
    {
        if (!ringMesh)
        {
            Color g = new Color(0.2f, 1f, 0.4f);
            ringMesh = WarningZone.Annulus(0.95f, 1f, new Color(g.r, g.g, g.b, 0.95f), new Color(g.r, g.g, g.b, 0.95f));
            discMesh = WarningZone.Annulus(0f, 1f, new Color(g.r, g.g, g.b, 0.12f), new Color(g.r, g.g, g.b, 0.3f));
            wallMesh = WarningZone.Wall(new Color(g.r, g.g, g.b, 0.4f), new Color(g.r, g.g, g.b, 0f));
        }
        var go = new GameObject("HealZone");
        go.transform.position = new Vector3(center.x, 0.8f, center.z);
        var z = go.AddComponent<HealZone>();
        z.radius = radius; z.duration = duration; z.healPerSecond = healPerSecond; z.born = Time.time;
        z.mpb = new MaterialPropertyBlock();
        Part(go.transform, "Disc", discMesh, new Vector3(radius, 1, radius));
        Part(go.transform, "Ring", ringMesh, new Vector3(radius, 1, radius));
        Part(go.transform, "Wall", wallMesh, new Vector3(radius, 30f, radius));
        z.renderers = go.GetComponentsInChildren<MeshRenderer>();
        return z;
    }

    static void Part(Transform parent, string name, Mesh mesh, Vector3 scale)
    {
        var p = new GameObject(name);
        p.transform.SetParent(parent, false);
        p.transform.localScale = scale;
        p.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = p.AddComponent<MeshRenderer>();
        mr.sharedMaterial = WarningZone.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void Update()
    {
        float age = Time.time - born;
        if (age >= duration) { Destroy(gameObject); return; }

        var g = GiantHealth.Instance;
        if (g && !g.IsDead)
        {
            Vector3 d = g.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude <= radius * radius) g.Heal(healPerSecond * Time.deltaTime);
        }

        // 부드럽게 맥동, 마지막 1.5초 동안 서서히 사라짐 + 처음 0.3초 동안 퍼지며 나타남
        float fade = Mathf.Clamp01((duration - age) / 1.5f);
        float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.3f));
        transform.localScale = new Vector3(grow, 1f, grow);
        float pulse = 0.75f + 0.25f * Mathf.Sin(age * 5f);
        mpb.SetColor("_Color", new Color(1, 1, 1, pulse * fade));
        foreach (var r in renderers) r.SetPropertyBlock(mpb);
    }
}
