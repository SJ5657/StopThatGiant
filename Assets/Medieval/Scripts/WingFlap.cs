using UnityEngine;

// 날개 퍼덕임 (그리폰·와이번·드래곤): 날개 회전축을 몸 앞뒤 축(로컬 Z)으로 위아래로 흔듦.
// 왼쪽 날개는 +, 오른쪽 날개는 − 방향. 격추되면(HeliAI·JetAI의 IsDead) 날개를 축 늘어뜨림.
public class WingFlap : MonoBehaviour
{
    public Transform[] leftWings;
    public Transform[] rightWings;
    [Tooltip("1초에 퍼덕이는 횟수")] public float rate = 1.2f;
    [Tooltip("퍼덕이는 각도 (위아래 최대)")] public float amplitude = 35f;
    [Tooltip("날개를 펼친 기본 각도 (위로 +)")] public float baseAngle = 5f;
    float t;
    HeliAI heli; JetAI jet;

    void Awake() { heli = GetComponent<HeliAI>(); jet = GetComponent<JetAI>(); t = Random.value * 10f; }

    void Update()
    {
        bool dead = (heli && heli.IsDead) || (jet && jet.IsDead);
        float a;
        if (dead) a = -40f; // 축 늘어진 날개
        else
        {
            t += Time.deltaTime * rate;
            // 내려칠 때 빠르고 올릴 때 느리게 (실제 날갯짓처럼)
            float s = Mathf.Sin(t * Mathf.PI * 2f);
            a = baseAngle + amplitude * (s > 0f ? s : s * 0.7f);
        }
        foreach (var w in leftWings) if (w) w.localRotation = Quaternion.Euler(0f, 0f, a);
        foreach (var w in rightWings) if (w) w.localRotation = Quaternion.Euler(0f, 0f, -a);
    }
}
