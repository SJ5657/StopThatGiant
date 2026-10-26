using UnityEngine;

// UFO 레이저로 일반 사람 크기가 된 거인에게 달려들어 주먹질하는 사람.
//  - GiantShrink가 주변에 몰려드는 군중으로 만들기도 하고, 근처 시민(Civilian)도 같은 동작(Step)을 빌려 씀
//  - 닿을 거리면 멈춰서 Cross Punch 동작으로 때림 → 주먹이 뻗는 순간 작아진 거인 HP 피해 + 살짝 밀려남
public class ShrinkAttacker : MonoBehaviour
{
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int PunchHash = Animator.StringToHash("Punch");

    Animator anim;
    float speed, cooldown, hitTimer, animValue;

    public void Init(Animator a, float runSpeed)
    {
        anim = a; speed = runSpeed;
        cooldown = Random.Range(0f, 0.6f);   // 모두 같은 박자로 때리지 않게
        hitTimer = -1f;
    }

    void Update()
    {
        if (!GiantShrink.Active) return;   // 끝나면 GiantShrink가 치움
        Step(transform, anim, speed, ref cooldown, ref hitTimer, ref animValue);
    }

    // 한 프레임 동작: 거인에게 달려가고, 닿으면 때림. 때리는 중이면 true
    // hitTimer: 주먹을 휘두른 뒤 맞는 순간까지 남은 시간 (휘두르는 중이 아니면 음수)
    public static bool Step(Transform self, Animator a, float runSpeed, ref float cooldown, ref float hitTimer, ref float animValue)
    {
        var g = GiantHealth.Instance;
        var s = GiantShrink.Instance;
        if (!g || g.IsDead || !s) return false;
        float dt = Time.deltaTime;
        cooldown -= dt;

        Vector3 to = g.transform.position - self.position; to.y = 0f;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.01f ? to / dist : self.forward;
        float reach = s.punchReach;
        bool swinging = hitTimer >= 0f;
        bool attacking = dist <= reach || swinging;

        float wantAnim;
        if (!attacking)
        {
            // 달려감 (거인 바로 앞에서 멈춤)
            float step = Mathf.Min(runSpeed * dt, dist - reach * 0.8f);
            if (step > 0f) self.position += dir * step;
            wantAnim = 1f;
        }
        else
        {
            wantAnim = 0f;
            if (!swinging && cooldown <= 0f && s.Vulnerable)
            {
                // 휘두르기 시작: 주먹이 뻗는 순간에 맞음
                cooldown = s.punchInterval * Random.Range(0.85f, 1.15f);
                hitTimer = s.punchHitDelay;
                if (a) a.SetTrigger(PunchHash);
            }
        }

        if (hitTimer >= 0f)
        {
            hitTimer -= dt;
            if (hitTimer < 0f && dist <= reach * 1.4f && s.Vulnerable)
                s.OnPunched(dir);   // 피해 + 살짝 밀려남
        }

        var mgr = CivilianManager.Instance;   // 인도 턱 높이 따라가기
        if (mgr) { var p = self.position; p.y = Mathf.Lerp(p.y, mgr.GroundY(p), 0.5f); self.position = p; }

        if (dir.sqrMagnitude > 0.001f)
            self.rotation = Quaternion.RotateTowards(self.rotation, Quaternion.LookRotation(dir), 720f * dt);
        animValue = Mathf.MoveTowards(animValue, wantAnim, dt * 4f);
        if (a) a.SetFloat(SpeedHash, animValue);
        return attacking;
    }
}
