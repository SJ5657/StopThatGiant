using UnityEngine;

// 거인 조작: WASD/방향키 이동, Shift 달리기. 모델 스케일에 맞춰 속도/중력 자동 조절.
[RequireComponent(typeof(CharacterController))]
public class GiantController : MonoBehaviour
{
    [Tooltip("원래 크기(1배) 기준 걷기 속도 m/s")] public float walkSpeed = 1.4f;
    [Tooltip("원래 크기(1배) 기준 달리기 속도 m/s")] public float runSpeed = 3.8f;
    [Tooltip("노란 건물 무한 달리기 중 달리기 속도 배율 (Shift 없이 자동으로 달림)")] public float boostRunMultiplier = 1.5f;
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
    // 모델 크기 1당 CharacterController 치수 (크기를 바꿀 때 비례해서 맞춤)
    float ccHeightPer, ccRadiusPer, ccCenterYPer, ccStepPer, ccHeightBase, ccCenterBase;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        stamina = GetComponent<GiantStamina>();
        if (anim) anim.applyRootMotion = false;
        float s0 = anim ? anim.transform.localScale.y : 1f;
        ccHeightPer = cc.height / s0; ccRadiusPer = cc.radius / s0; ccCenterYPer = cc.center.y / s0; ccStepPer = cc.stepOffset / s0;
        ccHeightBase = ccHeightPer; ccCenterBase = ccCenterYPer;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        // 거인은 건물/잔해에 막히지 않고 통과 (대신 GiantStomp가 부숨)
        int g = LayerMask.NameToLayer("Giant"), b = LayerMask.NameToLayer("Building"), d = LayerMask.NameToLayer("Debris");
        if (g >= 0 && b >= 0) Physics.IgnoreLayerCollision(g, b, true);
        if (g >= 0 && d >= 0) Physics.IgnoreLayerCollision(g, d, true);
    }

    // 거인 모델 크기 (기본 32). 발 반경·속도·카메라 거리 등은 모두 이 크기에 비례해서 자동으로 바뀜
    public float ModelScale => anim ? anim.transform.localScale.y : 1f;

    // 모델마다 원본 키가 달라서(남자 모델이 더 큼) 화면상 키를 맞추려고 모델 크기를 다르게 줌.
    // 게임 수치는 이 보정값을 곱한 GameScale 기준이라 성별에 관계없이 완전히 동일.
    [HideInInspector] public float scaleCompensation = 1f;
    public float GameScale => (anim ? anim.transform.lossyScale.y : 1f) * scaleCompensation;

    public void SetModelScale(float s)
    {
        if (!anim || s <= 0f) return;
        anim.transform.localScale = Vector3.one * s;
        float gs = s * scaleCompensation; // 충돌 캡슐도 게임 기준 크기로 (성별 무관 동일)
        cc.height = ccHeightPer * gs;
        cc.radius = ccRadiusPer * gs;
        cc.center = new Vector3(cc.center.x, ccCenterYPer * gs, cc.center.z);
        cc.stepOffset = Mathf.Min(ccStepPer * gs, cc.height * 0.5f);
    }

    // 모델이 바뀌면(성별 선택) 애니메이터 다시 찾고, 모델 키에 맞춰 충돌 캡슐 높이 조정
    public void Rebind(float heightFactor = 1f)
    {
        anim = GetComponentInChildren<Animator>();
        if (anim) anim.applyRootMotion = false;
        ccHeightPer = ccHeightBase * heightFactor;
        ccCenterYPer = ccCenterBase * heightFactor;
        SetModelScale(ModelScale);
    }

    void Update()
    {
        float scale = GameScale;
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) + externalInput;
        input = Vector2.ClampMagnitude(input, 1f);
        bool wantRun = externalRun || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool boosted = stamina && stamina.Infinite; // 노란 건물 효과: 자동 달리기 + 더 빠르게
        if (boosted) wantRun = true;

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
        float speed = moving ? (IsRunning ? runSpeed * (boosted ? boostRunMultiplier : 1f) : walkSpeed * mult) * input.magnitude : 0f;

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
            anim.speed = (moving && !IsRunning) ? mult : (IsRunning && boosted ? boostRunMultiplier : 1f);
        }
    }
}
