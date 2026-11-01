using UnityEngine;

// 작살병 (루프건): 걸어서 거인과 일정 거리까지 다가와 작살을 쏨 (GiantRopes). 줄 끝은 병사가 총에 잡고 있다가,
// 작살이 꽂히면 발 앞 땅에 말뚝을 세워 줄을 묶고 세 번 내리쳐 박은 뒤 다시 장전해서 또 쏨. 빗나가면 줄을 버리고 다시 장전.
// 거인이 너무 가까이 오면 도망. 밟히면 납작 (점수·경험치). 거리는 거인 키에 비례.
public class RopeGunner : MonoBehaviour
{
    [HideInInspector] public float scale = 1.2f;
    [HideInInspector] public RopeGunnerSpawner owner;

    public bool IsDead { get; private set; }
    Animator anim;
    float nextShot, aimUntil, animSpeed;
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly Vector3 GunTip = new Vector3(0.25f, 1.25f, 1.0f); // 루프건 총구 (모델 기준 좌표)

    enum Phase { Free, Waiting, Anchoring } // 평소 / 쏘고 작살이 날아가는 중 / 말뚝 박는 중
    Phase phase;
    GiantRopes.Rope rope;
    float phaseT, yaw;
    int blows;

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        nextShot = Time.time + Random.Range(1.5f, 3f);
    }

    static float Ground(Vector3 p) => WildTerrain.Active ? WildTerrain.Height(p.x, p.z) : 0f;
    static bool Blocked(GiantHealth g) => !g || g.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || GameStartMenu.InMenu;

    void Update()
    {
        if (IsDead || !owner) return;
        var g = GiantHealth.Instance;
        var ctrl = GiantController.Instance;
        if (!g || !ctrl) return;
        Vector3 pos = transform.position;
        Vector3 toG = g.transform.position - pos; toG.y = 0f;
        float d = toG.magnitude;
        float gs = ctrl.GameScale;
        float squash = (CivilianManager.Instance ? CivilianManager.Instance.squashRadius : 0.3f) * gs;
        if (d < squash || g.IsTouching(pos + Vector3.up, 1.5f)) { Die(); return; }
        if (EnemyFreeze.Active) return;
        if (d > owner.despawnDistance) { Destroy(gameObject); return; }
        float dt = Time.deltaTime, speed = 0f;

        if (phase != Phase.Free) Tie(toG, dt);
        else
        {
            float h = Mathf.Max(2f, (g.ShoulderY - g.transform.position.y) / 0.837f);
            float prefer = h * owner.preferRangePerHeight, tooClose = h * owner.minRangePerHeight, fire = h * owner.fireRangePerHeight;
            Vector3 move = Vector3.zero;
            bool aiming = Time.time < aimUntil;
            if (!aiming)
            {
                if (d < tooClose) { move = -toG.normalized; speed = owner.runSpeed * scale; }        // 도망
                else if (d > prefer) { move = toG.normalized; speed = (d > fire ? owner.runSpeed : owner.runSpeed * 0.45f) * scale; } // 다가감
            }
            if (speed > 0f)
            {
                pos += move * speed * dt;
                pos.y = Ground(pos);
                transform.position = pos;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(move), 540f * dt);
            }
            else if (toG.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toG), 360f * dt);

            // 사격: 사거리 안·화면 안에서 잠깐 겨눴다가 발사
            bool canFire = !Blocked(g) && d < fire && d > tooClose * 0.8f && !MilitarySpawner.OutOfSight(pos);
            if (canFire && Time.time >= nextShot && !aiming) aimUntil = Time.time + 0.6f;
            if (aiming && Time.time + Time.deltaTime >= aimUntil)
            {
                aimUntil = 0f;
                nextShot = Time.time + owner.fireInterval * Random.Range(0.85f, 1.2f);
                if (canFire) Shoot(g);
            }
        }

        animSpeed = Mathf.MoveTowards(animSpeed, speed <= 0f ? 0f : speed > owner.runSpeed * scale * 0.6f ? 1f : 0.5f, dt * 4f);
        if (anim) anim.SetFloat(SpeedHash, animSpeed);
    }

    // 쏜 뒤: 제자리에서 거인 쪽을 보며 줄을 잡고 있다가, 꽂히면 말뚝을 세워 박음
    void Tie(Vector3 toG, float dt)
    {
        if (toG.sqrMagnitude > 0.01f) yaw = Mathf.MoveTowardsAngle(yaw, Quaternion.LookRotation(toG).eulerAngles.y, 240f * dt);
        float pitch = 0f;
        if (rope == null || (rope.Done && phase == Phase.Waiting) || rope.state == GiantRopes.State.Snapped)
        {
            // 빗나갔거나 줄이 끊김: 다시 장전
            bool missed = phase == Phase.Waiting;
            phase = Phase.Free; rope = null;
            nextShot = Time.time + (missed ? owner.fireInterval * 0.5f : owner.reloadAfterAnchor);
        }
        else if (phase == Phase.Waiting)
        {
            if (rope.Hooked)
            {
                // 꽂힘: 발 앞 땅에 말뚝을 세우고 줄을 묶음
                GiantRopes.PlantStake(rope, transform.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 1.3f * scale);
                phase = Phase.Anchoring; phaseT = 0f; blows = 0;
            }
        }
        else
        {
            // 몸을 숙여 세 번 내리쳐 말뚝을 박음
            phaseT += dt / Mathf.Max(0.2f, owner.anchorTime);
            float u = Mathf.Clamp01(phaseT);
            pitch = 40f * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3f));
            if (blows < 3 && u >= (2 * blows + 1) / 6f)
            {
                blows++;
                GiantRopes.DriveStake(rope, blows / 3f);
                if (rope.stake) CombatFX.Muzzle(rope.stake.position + Vector3.up * 0.5f * rope.stakeK, Vector3.up, 0.5f);
            }
            if (u >= 1f)
            {
                GiantRopes.DriveStake(rope, 1f);
                phase = Phase.Free; rope = null;
                nextShot = Time.time + owner.reloadAfterAnchor; // 다시 장전해서 또 쏨
            }
        }
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void Shoot(GiantHealth g)
    {
        Vector3 muzzle = transform.TransformPoint(GunTip);
        rope = GiantRopes.Fire(muzzle, transform, GunTip, Random.value < owner.accuracy, owner.harpoonSpeed);
        CombatFX.Muzzle(muzzle, transform.forward, 1.6f);
        if (rope != null) { phase = Phase.Waiting; yaw = transform.eulerAngles.y; }
    }

    void Die()
    {
        IsDead = true;
        GiantRopes.Release(rope); // 아직 묶지 못한 줄은 풀려 끊어짐
        rope = null;
        if (anim) anim.enabled = false;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        var s = transform.localScale;
        transform.localScale = new Vector3(s.x * 1.4f, s.y * 0.12f, s.z * 1.4f);
        BloodSplat.Spawn(transform.position, scale);
        ScoreManager.Add(owner ? owner.points : 80, "작살병", transform.position + Vector3.up);
        SinkAway.Begin(gameObject, 2f, 0.8f);
    }
}
