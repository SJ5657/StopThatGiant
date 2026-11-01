using UnityEngine;

// 부서진 잔해 치우기: 잠시 그대로 있다가 작아지며 땅속으로 가라앉아 사라짐 (갑자기 뿅 사라지지 않게).
// 밟힌 탱크·차량·경찰차·동물·몬스터 등에 붙임: SinkAway.Begin(gameObject, 남아 있는 시간, 가라앉는 시간)
public class SinkAway : MonoBehaviour
{
    float startAt, duration, depth;
    Vector3 pos0, scale0;
    bool started;

    public static void Begin(GameObject go, float hold, float duration = 1.2f)
    {
        if (!go) return;
        var s = go.GetComponent<SinkAway>();
        if (!s) s = go.AddComponent<SinkAway>();
        s.startAt = Time.time + hold;
        s.duration = Mathf.Max(0.05f, duration);
        s.started = false;
    }

    void Update()
    {
        if (Time.time < startAt) return;
        if (!started)
        {
            // 가라앉기 시작할 때의 자리·크기 기준 (그 전까지는 원래 스크립트가 찌그러뜨리는 등 모양을 바꿀 수 있음)
            started = true;
            pos0 = transform.position; scale0 = transform.localScale;
            var rs = GetComponentsInChildren<Renderer>();
            float h = 2f;
            if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); h = Mathf.Max(1f, b.size.y); }
            depth = h + 1f;
        }
        float f = (Time.time - startAt) / duration;
        if (f >= 1f) { Destroy(gameObject); return; }
        float e = f * f;
        transform.position = pos0 - Vector3.up * depth * e;
        transform.localScale = scale0 * (1f - 0.6f * e);
    }
}
