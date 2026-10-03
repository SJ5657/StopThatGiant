using UnityEngine;

// 폭탄 낙하 예정 지점 경고: 바닥 원(반경 = 폭발 반경) + 테두리 + 반투명 벽 + 시간에 따라 차오르는 원.
public class WarningZone : MonoBehaviour
{
    public static Material material; // 버텍스 컬러 + 알파 지원 (Sprites/Default)
    static Mesh ringMesh, discMesh, wallMesh;

    float radius, duration, born;
    bool shown;
    string announce;
    Transform progress;
    MeshRenderer[] renderers;
    MaterialPropertyBlock mpb;

    // showDelay초 뒤에 나타나서 expectedDuration초 뒤(폭발 시점)까지 표시
    public static WarningZone Create(Vector3 center, float radius, float expectedDuration, float showDelay = 0f, string announce = null)
    {
        if (!ringMesh)
        {
            ringMesh = Annulus(0.95f, 1f, new Color(1f, 0.15f, 0.1f, 0.95f), new Color(1f, 0.15f, 0.1f, 0.95f));
            discMesh = Annulus(0f, 1f, new Color(1f, 0.2f, 0.1f, 0.12f), new Color(1f, 0.2f, 0.1f, 0.22f));
            wallMesh = Wall(new Color(1f, 0.2f, 0.1f, 0.35f), new Color(1f, 0.2f, 0.1f, 0f));
        }
        var go = new GameObject("BombWarning");
        go.transform.position = new Vector3(center.x, 0.8f, center.z);
        var z = go.AddComponent<WarningZone>();
        z.radius = radius; z.born = Time.time + Mathf.Max(0f, showDelay); z.duration = Mathf.Max(0.5f, expectedDuration - Mathf.Max(0f, showDelay)); z.announce = announce;
        z.mpb = new MaterialPropertyBlock();
        Part(go.transform, "Disc", discMesh, new Vector3(radius, 1, radius));
        Part(go.transform, "Ring", ringMesh, new Vector3(radius, 1, radius));
        Part(go.transform, "Wall", wallMesh, new Vector3(radius, 40f, radius));
        z.progress = Part(go.transform, "Progress", discMesh, Vector3.zero).transform;
        z.progress.localPosition = Vector3.up * 0.1f;
        z.renderers = go.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in z.renderers) r.enabled = false;
        return z;
    }

    static GameObject Part(Transform parent, string name, Mesh mesh, Vector3 scale)
    {
        var p = new GameObject(name);
        p.transform.SetParent(parent, false);
        p.transform.localScale = scale;
        p.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = p.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return p;
    }

    void Update()
    {
        if (Time.time < born) return; // 아직 경고 표시 전
        if (!shown)
        {
            shown = true;
            foreach (var r in renderers) r.enabled = true;
            if (!string.IsNullOrEmpty(announce) && MilitarySpawner.Instance) MilitarySpawner.Instance.ShowBanner(announce);
        }
        float k = Mathf.Clamp01((Time.time - born) / duration);
        // 폭발 시점에 가까워질수록 안쪽 원이 차오름
        progress.localScale = new Vector3(radius * k, 1, radius * k);
        // 깜빡임 (가까워질수록 빠르게)
        float blink = 0.65f + 0.35f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 22f, k));
        mpb.SetColor("_Color", new Color(1, 1, 1, blink));
        foreach (var r in renderers) r.SetPropertyBlock(mpb);
        if (Time.time - born > duration + 8f) Destroy(gameObject); // 안전장치
    }

    public static Mesh Annulus(float inner, float outer, Color cIn, Color cOut)
    {
        int seg = 72;
        var v = new Vector3[seg * 2]; var c = new Color[seg * 2]; var t = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v[i * 2] = d * inner; v[i * 2 + 1] = d * outer;
            c[i * 2] = cIn; c[i * 2 + 1] = cOut;
            int n = (i + 1) % seg;
            t[i * 6] = i * 2; t[i * 6 + 1] = n * 2; t[i * 6 + 2] = i * 2 + 1;
            t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = n * 2; t[i * 6 + 5] = n * 2 + 1;
        }
        var m = new Mesh { vertices = v, colors = c, triangles = t };
        m.RecalculateBounds();
        return m;
    }

    public static Mesh Wall(Color bottom, Color top)
    {
        int seg = 72;
        var v = new Vector3[seg * 2]; var c = new Color[seg * 2]; var t = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v[i * 2] = d; v[i * 2 + 1] = d + Vector3.up;
            c[i * 2] = bottom; c[i * 2 + 1] = top;
            int n = (i + 1) % seg;
            t[i * 6] = i * 2; t[i * 6 + 1] = i * 2 + 1; t[i * 6 + 2] = n * 2;
            t[i * 6 + 3] = n * 2; t[i * 6 + 4] = i * 2 + 1; t[i * 6 + 5] = n * 2 + 1;
        }
        var m = new Mesh { vertices = v, colors = c, triangles = t };
        m.RecalculateBounds();
        return m;
    }
}
