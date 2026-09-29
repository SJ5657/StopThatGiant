using UnityEngine;

// 거인 조작: WASD/방향키 이동, Shift 달리기. 모델 스케일에 맞춰 속도/중력 자동 조절.
[RequireComponent(typeof(CharacterController))]
public class GiantController : MonoBehaviour
{
    [Tooltip("원래 크기(1배) 기준 걷기 속도 m/s")] public float walkSpeed = 1.4f;
    [Tooltip("원래 크기(1배) 기준 달리기 속도 m/s")] public float runSpeed = 3.8f;
    public float turnSpeed = 360f;
    public float gravity = 9.81f;
    public Transform cameraTransform;
    [Tooltip("이동 가능 범위 (0이면 제한 없음)")] public Vector2 areaHalfExtent;

    [HideInInspector] public Vector2 externalInput;
    [HideInInspector] public bool externalRun;

    public bool IsRunning { get; private set; }
    public Vector3 Velocity { get; private set; }

    CharacterController cc;
    Animator anim;
    GiantStamina stamina;
    float verticalVel, animSpeed;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        stamina = GetComponent<GiantStamina>();
        if (anim) anim.applyRootMotion = false;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        // 거인은 건물/잔해에 막히지 않고 통과 (대신 GiantStomp가 부숨)
        int g = LayerMask.NameToLayer("Giant"), b = LayerMask.NameToLayer("Building"), d = LayerMask.NameToLayer("Debris");
        if (g >= 0 && b >= 0) Physics.IgnoreLayerCollision(g, b, true);
        if (g >= 0 && d >= 0) Physics.IgnoreLayerCollision(g, d, true);
    }

    void Update()
    {
        float scale = anim ? anim.transform.lossyScale.y : 1f;
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) + externalInput;
        input = Vector2.ClampMagnitude(input, 1f);
        bool wantRun = externalRun || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        Vector3 fwd = Vector3.forward, right = Vector3.right;
        if (cameraTransform)
        {
            fwd = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        Vector3 moveDir = fwd * input.y + right * input.x;
        bool moving = moveDir.sqrMagnitude > 0.001f;
        // 스테미나: 남아 있고 지치지 않았을 때만 달리기
        IsRunning = wantRun && moving && (stamina == null || stamina.CanRun);
        if (stamina) stamina.Tick(IsRunning, moving, Time.deltaTime);
        float mult = stamina ? stamina.SpeedMultiplier : 1f;
        float speed = moving ? (IsRunning ? runSpeed : walkSpeed * mult) * input.magnitude : 0f;

        if (moving)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(moveDir.normalized), turnSpeed * Time.deltaTime);

        if (cc.isGrounded && verticalVel < 0) verticalVel = -1f * scale;
        verticalVel -= gravity * scale * Time.deltaTime;

        Vector3 vel = (moving ? moveDir.normalized : Vector3.zero) * speed * scale;
        vel.y = verticalVel;
        cc.Move(vel * Time.deltaTime);
        Velocity = new Vector3(vel.x, 0, vel.z);

        if (areaHalfExtent.x > 0 && areaHalfExtent.y > 0)
        {
            var p = transform.position;
            p.x = Mathf.Clamp(p.x, -areaHalfExtent.x, areaHalfExtent.x);
            p.z = Mathf.Clamp(p.z, -areaHalfExtent.y, areaHalfExtent.y);
            if (p != transform.position) { cc.enabled = false; transform.position = p; cc.enabled = true; }
        }

        float target = !moving ? 0f : (IsRunning ? 1f : 0.5f);
        animSpeed = Mathf.MoveTowards(animSpeed, target, Time.deltaTime * 3f);
        if (anim)
        {
            anim.SetFloat(SpeedHash, animSpeed);
            // 지친 상태로 걸을 때는 걷기 애니메이션도 느리게 재생 (발 미끄러짐 방지 + 지친 느낌)
            anim.speed = (moving && !IsRunning) ? mult : 1f;
        }
    }
}
