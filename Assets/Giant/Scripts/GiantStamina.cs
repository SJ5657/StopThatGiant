using UnityEngine;

// 거인 스테미나: 달리면 소모, 걷거나 멈추면 회복.
// 0이 되면 '지침' 상태 → 일정량(recoverThreshold) 회복될 때까지 달릴 수 없고 걷는 속도도 약간 느려짐.
public class GiantStamina : MonoBehaviour
{
    public float maxStamina = 100f;
    [Tooltip("달릴 때 초당 소모량")] public float runDrain = 32f;
    [Tooltip("걸을 때 초당 회복량")] public float walkRegen = 8f;
    [Tooltip("멈춰 있을 때 초당 회복량")] public float idleRegen = 20f;
    [Tooltip("달리기를 멈춘 뒤 회복이 시작되기까지 대기 시간(초)")] public float regenDelay = 0.8f;
    [Tooltip("지친 뒤 이 값까지 회복해야 다시 달릴 수 있음")] public float recoverThreshold = 35f;
    [Tooltip("지친 상태의 걷기 속도 배율")] public float exhaustedSpeedMultiplier = 0.6f;

    public float Stamina { get; private set; }
    public bool Exhausted { get; private set; }
    public bool CanRun => !Exhausted && Stamina > 0f;
    public float SpeedMultiplier => Exhausted ? exhaustedSpeedMultiplier : 1f;

    float regenAt;

    void Awake() { Stamina = maxStamina; }

    // GiantController가 매 프레임 호출
    public void Tick(bool running, bool moving, float dt)
    {
        if (running)
        {
            Stamina = Mathf.Max(0f, Stamina - runDrain * dt);
            regenAt = Time.time + regenDelay;
            if (Stamina <= 0f) Exhausted = true;
        }
        else if (Time.time >= regenAt)
        {
            Stamina = Mathf.Min(maxStamina, Stamina + (moving ? walkRegen : idleRegen) * dt);
        }
        if (Exhausted && Stamina >= recoverThreshold) Exhausted = false;
    }

    void OnGUI()
    {
        float w = 260, h = 12, x = 20, y = 46;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, h + 6), Texture2D.whiteTexture);
        Color c = Exhausted
            ? Color.Lerp(new Color(0.6f, 0.3f, 0.1f), new Color(1f, 0.5f, 0.1f), Mathf.PingPong(Time.time * 4f, 1f))
            : new Color(0.95f, 0.8f, 0.2f);
        GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w * Stamina / maxStamina, h), Texture2D.whiteTexture);
        // 다시 달릴 수 있는 지점 표시
        if (Exhausted)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x + w * recoverThreshold / maxStamina - 1, y - 2, 2, h + 4), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
        var style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 10, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(x, y - 4, w, h + 8), Exhausted ? "EXHAUSTED" : "STAMINA", style);
    }
}
