using UnityEngine;
using UnityEngine.SceneManagement;

// 거인 체력. 군대 공격을 받으면 감소, 0이 되면 패배 (R로 재시작).
public class GiantHealth : MonoBehaviour
{
    public static GiantHealth Instance { get; private set; }
    public float maxHP = 2000f;
    public float HP { get; private set; }
    public bool IsDead => HP <= 0f;

    CharacterController cc;
    Animator anim;
    GiantController ctrl;
    float hitFlash;

    static readonly HumanBodyBones[] AimBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Chest,
        HumanBodyBones.Head, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg
    };

    void Awake()
    {
        Instance = this;
        HP = maxHP;
        cc = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        ctrl = GetComponent<GiantController>();
    }

    public float Scale => anim ? anim.transform.lossyScale.y : 1f;
    public Vector3 Velocity => ctrl ? ctrl.Velocity : Vector3.zero;
    public Vector3 Center => transform.TransformPoint(cc.center);

    // 어깨 높이(월드 Y). 헬기 비행 고도 상한에 사용
    public float ShoulderY
    {
        get
        {
            if (anim && anim.isHuman)
            {
                var l = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var r = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (l && r) return Mathf.Max(l.position.y, r.position.y);
            }
            return transform.position.y + cc.height * 0.82f;
        }
    }

    public Transform Bone(HumanBodyBones b) => anim && anim.isHuman ? anim.GetBoneTransform(b) : null;

    // 공격 목표 지점 (몸의 랜덤한 부위)
    public Vector3 GetAimPoint()
    {
        if (anim && anim.isHuman)
        {
            var t = anim.GetBoneTransform(AimBones[Random.Range(0, AimBones.Length)]);
            if (t) return t.position;
        }
        return Center;
    }

    // 점 p에서 거인 몸(캡슐) 표면까지 거리 (안쪽이면 0)
    public float DistanceToBody(Vector3 p)
    {
        Vector3 c = Center;
        float half = Mathf.Max(0, cc.height * 0.5f - cc.radius);
        Vector3 a = c + Vector3.up * half, b = c - Vector3.up * half;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return Mathf.Max(0f, Vector3.Distance(p, a + ab * t) - cc.radius);
    }

    // 점 p가 거인 몸(캡슐) 안에 있는지
    public bool IsInsideBody(Vector3 p, float extra = 0f)
    {
        Vector3 c = Center;
        float half = Mathf.Max(0, cc.height * 0.5f - cc.radius);
        Vector3 a = c + Vector3.up * half, b = c - Vector3.up * half;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
        return (p - (a + ab * t)).sqrMagnitude < (cc.radius + extra) * (cc.radius + extra);
    }

    static readonly HumanBodyBones[] TouchBones =
    {
        HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg
    };

    // 점 p(반경 r)가 거인 몸 어디든(몸통 캡슐 + 팔/머리/다리 뼈) 닿았는지
    public bool IsTouching(Vector3 p, float r)
    {
        if (IsInsideBody(p, r)) return true;
        if (!anim || !anim.isHuman) return false;
        float boneR = 0.12f * Scale + r;
        foreach (var b in TouchBones)
        {
            var t = anim.GetBoneTransform(b);
            if (t && (t.position - p).sqrMagnitude < boneR * boneR) return true;
        }
        return false;
    }

    public void TakeDamage(float dmg)
    {
        if (IsDead) return;
        HP = Mathf.Max(0, HP - dmg);
        hitFlash = 0.25f;
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.12f);
        if (IsDead)
        {
            if (ctrl) ctrl.enabled = false;
            if (anim) anim.SetFloat("Speed", 0f);
        }
    }

    void Update()
    {
        hitFlash = Mathf.Max(0, hitFlash - Time.deltaTime);
        if (IsDead && Input.GetKeyDown(KeyCode.R))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnGUI()
    {
        float w = 260, h = 20, x = 20, y = 20;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, h + 6), Texture2D.whiteTexture);
        GUI.color = hitFlash > 0 ? new Color(1f, 0.9f, 0.9f) : new Color(0.9f, 0.2f, 0.25f);
        GUI.DrawTexture(new Rect(x, y, w * HP / maxHP, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(x, y, w, h), $"GIANT HP  {Mathf.CeilToInt(HP)} / {maxHP:0}", style);
        if (IsDead)
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60), "DEFEATED", big);
            GUI.Label(new Rect(0, Screen.height * 0.4f + 60, Screen.width, 30), "Press R to restart", style);
        }
    }
}
