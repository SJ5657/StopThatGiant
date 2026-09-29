using UnityEngine;

// 3인칭 카메라: 마우스 회전, 휠 줌, 화면 흔들림. Esc 커서 해제 / 클릭 잠금.
public class GiantCamera : MonoBehaviour
{
    public Transform target;
    [Tooltip("타깃 스케일 기준 바라보는 높이")] public float lookHeight = 1.25f;
    [Tooltip("타깃 스케일 기준 거리")] public float distance = 3.4f;
    public float minDistance = 1.5f, maxDistance = 9f;
    public float mouseSensitivity = 3f;
    public float minPitch = -10f, maxPitch = 75f;
    public float smooth = 10f;
    public float minHeightAboveGround = 3f;

    public static GiantCamera Instance { get; private set; }
    float yaw, pitch = 18f, shake;

    void Awake() { Instance = this; }
    void Start() { if (target) yaw = target.eulerAngles.y; Lock(true); }
    void Lock(bool l) { Cursor.lockState = l ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !l; }

    public void Shake(float amount) { shake = Mathf.Min(shake + amount, 2.5f); }

    void LateUpdate()
    {
        if (!target) return;
        // 레벨업 카드 선택 중(게임 일시정지)에는 카메라를 완전히 고정.
        // (멈춘 상태에서 흔들림 오프셋이 매 프레임 누적되어 카메라가 엉뚱한 곳으로 밀려나던 문제 방지)
        if (LevelUpCards.IsChoosing || Time.deltaTime <= 0f) return;
        if (Input.GetKeyDown(KeyCode.Escape)) Lock(false);
        if (Input.GetMouseButtonDown(0) && !LevelUpCards.IsChoosing) Lock(true); // 카드 고르는 클릭은 무시
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, minPitch, maxPitch);
        }
        distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 0.4f, minDistance, maxDistance);

        float s = target.lossyScale.y;
        Vector3 pivot = target.position + Vector3.up * lookHeight * s;
        Vector3 desired = pivot - Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * distance * s;
        desired.y = Mathf.Max(desired.y, minHeightAboveGround);
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-smooth * Time.deltaTime));
        transform.rotation = Quaternion.LookRotation(pivot - transform.position);

        if (shake > 0.001f)
        {
            float t = Time.time * 40f;
            transform.position += new Vector3(Mathf.PerlinNoise(t, 0) - 0.5f, Mathf.PerlinNoise(0, t) - 0.5f, 0) * shake * s * 0.15f;
            shake = Mathf.MoveTowards(shake, 0, Time.deltaTime * 3f);
        }
    }
}
