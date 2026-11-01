using UnityEngine;

// 중세 씬의 경비병 화살: 총알 궤적(노란 선) 대신 짧은 화살이 시작점에서 끝점까지 빠르게 날아가고 사라짐 (모양만, 피해는 쏜 쪽이 처리).
public class FlyingArrow : MonoBehaviour
{
    const float Speed = 260f, Length = 3.2f;
    Vector3 from, to;
    float t, total;
    LineRenderer shaft, tip;

    public static void Shoot(Vector3 a, Vector3 b)
    {
        if (!WarningZone.material) return;
        var go = new GameObject("Arrow");
        var arrow = go.AddComponent<FlyingArrow>();
        arrow.from = a; arrow.to = b;
        arrow.total = Mathf.Max(0.01f, Vector3.Distance(a, b) / Speed);
        arrow.shaft = Line(go, "Shaft", 0.22f, new Color(0.38f, 0.26f, 0.16f, 1f));
        arrow.tip = Line(go, "Fletching", 0.55f, new Color(0.95f, 0.95f, 0.92f, 1f));
        arrow.Place(0f);
    }

    static LineRenderer Line(GameObject parent, string name, float width, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = WarningZone.material;
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = width;
        lr.startColor = lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    void Place(float k)
    {
        Vector3 dir = (to - from).normalized;
        Vector3 head = Vector3.Lerp(from, to, k), tail = head - dir * Length;
        shaft.SetPosition(0, tail); shaft.SetPosition(1, head);
        tip.SetPosition(0, tail); tip.SetPosition(1, tail + dir * Length * 0.22f); // 꽁지깃
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= total) { Destroy(gameObject); return; }
        Place(t / total);
    }
}
