using UnityEngine;

// OnAnimatorIK는 Animator가 붙은 오브젝트(거인 모델)에서만 호출되므로, 그 호출을 GiantEat으로 넘겨줌.
// GiantEat이 현재 모델에 자동으로 붙임 (성별 선택으로 모델이 바뀌어도 대응).
public class GiantIKRelay : MonoBehaviour
{
    [HideInInspector] public GiantEat owner;
    Animator anim;

    void Awake() { anim = GetComponent<Animator>(); }

    void OnAnimatorIK(int layerIndex)
    {
        if (owner && anim) owner.OnIK(anim);
    }
}
