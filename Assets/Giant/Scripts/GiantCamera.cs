using UnityEngine;

// 사선 탑뷰 카메라: 위에서 비스듬히 내려다보며 거인을 따라감.
// 마우스 좌우로 주변을 돌 수 있고, 내려다보는 각도(pitch)는 고정. 휠 줌, 화면 흔들림.
// Esc 커서 해제 / 클릭 잠금 (커서가 잠겨 있을 때만 마우스로 시점 회전).
public class GiantCamera : MonoBehaviour
{
    GiantController gc;
    public Transform target;
    [Tooltip("타깃 스케일 기준 바라보는 높이")] public float lookHeight = 0.6f;
    [Tooltip("타깃 스케일 기준 거리")] public float distance = 5.5f;
    public float minDistance = 3f, maxDistance = 10f;
    [Tooltip("시작 수평 각도 (45 = 오른쪽 뒤 대각선에서 바라봄)")] public float yaw = 45f;
    [Tooltip("고정 내려다보는 각도 (90 = 수직 탑뷰)")] [Range(10f, 89f)] public float pitch = 50f;
    public float mouseSensitivity = 3f;
    public float smooth = 10f;
    public float minHeightAboveGround = 3f;

    public static GiantCamera Instance { get; private set; }
    float shake;

    void Awake() { Instance = this; }
    void Start() { Lock(true); }
    void Lock(bool l) { Cursor.lockState = l ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !l; }

    public void Shake(float amount) { shake = Mathf.Min(shake + amount, 2.5f); }

    void LateUpdate()
    {
        if (!target) return;
        // 카드 고르는 중(게임 일시정지)에는 카메라를 완전히 고정.
        // (멈춘 상태에서 흔들림 오프셋이 매 프레임 누적되어 카메라가 엉뚱한 곳으로 밀려나던 문제 방지)
        if (CardDraft.IsOpen || Time.deltaTime <= 0f) return;
        if (Input.GetKeyDown(KeyCode.Escape)) Lock(false);
        if (Input.GetMouseButtonDown(0) && !CardDraft.IsOpen) Lock(true); // 카드 고르는 클릭은 무시
        if (Cursor.lockState == CursorLockMode.Locked)
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 0.4f, minDistance, maxDistance);

        if (!gc) gc = target.GetComponentInParent<GiantController>();
        float s = gc ? gc.GameScale : target.lossyScale.y; // 성별 무관 동일한 카메라 거리
        // 회전은 마우스로만 바뀌고, 위치는 거인을 따라감 (거인이 돌아도 화면은 돌지 않음)
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pivot = (gc ? gc.transform.position : target.position) + Vector3.up * lookHeight * s;
        Vector3 desired = pivot - rot * Vector3.forward * distance * s;
        desired.y = Mathf.Max(desired.y, minHeightAboveGround);
        float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, k);

        if (shake > 0.001f)
        {
            float t = Time.time * 40f;
            transform.position += (transform.right * (Mathf.PerlinNoise(t, 0) - 0.5f) + transform.up * (Mathf.PerlinNoise(0, t) - 0.5f)) * shake * s * 0.15f;
            shake = Mathf.MoveTowards(shake, 0, Time.deltaTime * 3f);
        }
    }
}
