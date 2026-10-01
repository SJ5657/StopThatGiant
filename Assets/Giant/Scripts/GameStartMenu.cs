using UnityEngine;

// 게임 시작 화면 → 성별(거인 모델) 선택 → 게임 시작.
// 메뉴 동안에는 게임을 멈추고(Time.timeScale = 0) 게임 HUD를 숨기며, 배경에 거인을 보여주면서 카메라가 천천히 흔들린다.
// 성별 카드에 마우스를 올리면 배경의 거인이 그 모델로 바뀌어 미리보기됨. 성별을 고른 뒤에만 '게임 시작'이 활성화.
// 사망 후 R로 재시작하면 시작 화면 없이 같은 성별로 바로 시작.
public class GameStartMenu : MonoBehaviour
{
    public static GameStartMenu Instance { get; private set; }
    public static bool InMenu => Instance != null && Instance.state != State.Playing;

    [System.Serializable]
    public class GiantOption
    {
        public string label = "여자 거인";
        [TextArea(2, 4)] public string description = "";
        [Tooltip("Giant 오브젝트 아래에 있는 이 성별의 모델")] public GameObject model;
        [Tooltip("충돌 캡슐 높이 배율 (모델 키에 맞춤)")] public float collisionHeightFactor = 1f;
        public Color color = Color.white;
    }

    [Header("타이틀")]
    public string gameTitle = "Stop That Giant";
    public string subtitle = "도시를 짓밟고, 몰려드는 군대를 물리쳐라!";

    [Header("연결")]
    public GiantController giant;
    public GiantCamera giantCamera;
    public GiantOption[] options;

    enum State { Title, Select, Playing }
    State state = State.Title;
    int selected = -1, previewing = -1, hover = -1;
    float stateAt;
    bool started, camInit;
    Vector3 menuFocus;

    static int lastChoice = -1; // 재시작 시 같은 성별로 바로 시작
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { lastChoice = -1; }

    float referenceScale = 1f; // 첫 번째(여자) 모델 크기 = 게임 기준 크기

    void Awake()
    {
        Instance = this;
        if (options != null && options.Length > 0 && options[0].model) referenceScale = options[0].model.transform.localScale.y;
        if (!giant) giant = FindObjectOfType<GiantController>();
        if (!giantCamera) giantCamera = FindObjectOfType<GiantCamera>();
        if (options == null || options.Length == 0) { state = State.Playing; return; }

        if (lastChoice >= 0 && lastChoice < options.Length)
        {
            selected = lastChoice;
            state = State.Playing;
            Time.timeScale = 1f;
            SetActiveModel(selected);
        }
        else
        {
            state = State.Title;
            Time.timeScale = 0f;
            SetActiveModel(0);
        }
        stateAt = Time.unscaledTime;
    }

    void Start()
    {
        started = true;
        if (options == null || options.Length == 0) return;
        if (state == State.Playing) FinalizeChoice();
        else { Rebind(previewing); SetAnimUnscaled(true); }
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        if (InMenu) Time.timeScale = 1f;
        Instance = null;
    }

    // ───────────── 모델 교체 ─────────────
    void SetActiveModel(int i)
    {
        for (int j = 0; j < options.Length; j++)
            if (options[j].model) options[j].model.SetActive(j == i);
        previewing = i;
        if (started) { Rebind(i); SetAnimUnscaled(state != State.Playing); }
    }

    // 메뉴 중(시간 정지)에도 대기 애니메이션이 움직이도록
    void SetAnimUnscaled(bool unscaled)
    {
        var m = options[previewing].model;
        var an = m ? m.GetComponent<Animator>() : null;
        if (an) an.updateMode = unscaled ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
    }

    // 거인 관련 스크립트가 새 모델(애니메이터/뼈)을 쓰도록 다시 연결
    void Rebind(int i)
    {
        var o = options[i];
        if (giant)
        {
            // 화면상 키를 맞추려고 모델 크기가 달라도, 게임 수치는 기준 크기로 동일하게
            giant.scaleCompensation = o.model ? referenceScale / o.model.transform.localScale.y : 1f;
            giant.Rebind(o.collisionHeightFactor);
            var stomp = giant.GetComponent<GiantStomp>(); if (stomp) stomp.Rebind();
            var hp = giant.GetComponent<GiantHealth>(); if (hp) hp.Rebind();
        }
        if (giantCamera && o.model) giantCamera.target = o.model.transform;
    }

    void FinalizeChoice()
    {
        SetActiveModel(selected);
        for (int j = 0; j < options.Length; j++)
            if (j != selected && options[j].model) Destroy(options[j].model); // 고르지 않은 모델 제거
    }

    // ───────────── 흐름 ─────────────
    void GoSelect() { state = State.Select; stateAt = Time.unscaledTime; }
    void GoTitle() { state = State.Title; selected = -1; stateAt = Time.unscaledTime; }
    void Choose(int i) { if (i >= 0 && i < options.Length) selected = i; }

    void StartGame()
    {
        if (selected < 0) return;
        lastChoice = selected;
        state = State.Playing;
        FinalizeChoice();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Update()
    {
        if (!InMenu) return;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (Time.timeScale != 0f) Time.timeScale = 0f;
        if (Time.unscaledTime - stateAt < 0.2f) return;

        if (state == State.Title)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) GoSelect();
        }
        else if (state == State.Select)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1) || Input.GetKeyDown(KeyCode.LeftArrow)) Choose(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2) || Input.GetKeyDown(KeyCode.RightArrow)) Choose(1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) StartGame();
            else if (Input.GetKeyDown(KeyCode.Escape)) GoTitle();
        }
    }

    // 메뉴 카메라: 거인 정면에서 천천히 좌우로 흔들림. 게임이 시작되면 GiantCamera가 이어받아 뒤쪽으로 부드럽게 이동
    void LateUpdate()
    {
        if (!InMenu) return;

        int want = state == State.Select ? (hover >= 0 ? hover : (selected >= 0 ? selected : previewing)) : previewing;
        if (want != previewing) SetActiveModel(want);

        if (!giantCamera || !giant) return;
        var cam = giantCamera.transform;
        var m = options[previewing].model ? options[previewing].model.transform : giant.transform;
        float h = giant.GameScale * 1.7f;

        Vector3 basePos = giant.transform.position;
        Vector3 fwd = giant.transform.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 dir = Quaternion.Euler(0, Mathf.Sin(Time.unscaledTime * 0.25f) * 18f, 0) * fwd;

        Vector3 focus = basePos + Vector3.up * h * 0.55f;
        if (state == State.Title) focus += right * h * 0.4f; // 거인을 화면 오른쪽에 (왼쪽엔 제목)
        Vector3 desired = basePos + Vector3.up * h * 0.62f + dir * h * 1.45f;

        if (!camInit) { cam.position = desired; menuFocus = focus; camInit = true; }
        float k = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
        cam.position = Vector3.Lerp(cam.position, desired, k);
        menuFocus = Vector3.Lerp(menuFocus, focus, k);
        cam.rotation = Quaternion.LookRotation(menuFocus - cam.position);
    }

    // ───────────── 화면 ─────────────
    void OnGUI()
    {
        if (!InMenu) return;
        GUI.depth = -200;
        float W = Screen.width, H = Screen.height;
        float fade = Mathf.Clamp01((Time.unscaledTime - stateAt) / 0.35f);
        if (state == State.Title) DrawTitle(W, H, fade);
        else DrawSelect(W, H, fade);
        GUI.color = Color.white;
    }

    void DrawTitle(float W, float H, float fade)
    {
        // 왼쪽이 어두운 그라데이션
        const int steps = 32;
        float pw = W * 0.6f / steps;
        for (int i = 0; i < steps; i++)
        {
            float a = 1f - i / (float)steps;
            Box(new Rect(i * pw, 0, pw + 1, H), new Color(0, 0, 0, 0.8f * a * a));
        }

        float x = W * 0.07f;
        ShadowLabel(new Rect(x, H * 0.18f, W * 0.9f, H * 0.13f), gameTitle,
            Style(Mathf.RoundToInt(H * 0.09f), FontStyle.Bold, TextAnchor.MiddleLeft), new Color(1f, 0.82f, 0.28f, fade));
        ShadowLabel(new Rect(x + 4, H * 0.31f, W * 0.7f, H * 0.05f), subtitle,
            Style(Mathf.RoundToInt(H * 0.028f), FontStyle.Bold, TextAnchor.MiddleLeft), new Color(1f, 1f, 1f, 0.9f * fade));

        float bw = Mathf.Clamp(W * 0.22f, 240f, 340f), bh = Mathf.Clamp(H * 0.075f, 48f, 72f);
        if (MenuButton(new Rect(x, H * 0.5f, bw, bh), "게임 시작", new Color(1f, 0.55f, 0.15f), true, fade)) { GoSelect(); return; }
        if (MenuButton(new Rect(x, H * 0.5f + bh * 1.3f, bw, bh), "게임 종료", new Color(0.42f, 0.45f, 0.52f), true, fade)) { Quit(); return; }

        ShadowLabel(new Rect(x, H - 50, W * 0.5f, 30), "Enter : 게임 시작",
            Style(14, FontStyle.Normal, TextAnchor.MiddleLeft), new Color(1, 1, 1, 0.6f * fade));
    }

    void DrawSelect(float W, float H, float fade)
    {
        Box(new Rect(0, 0, W, H * 0.17f), new Color(0, 0, 0, 0.55f * fade));
        Box(new Rect(0, H * 0.8f, W, H * 0.2f), new Color(0, 0, 0, 0.55f * fade));

        ShadowLabel(new Rect(0, H * 0.025f, W, H * 0.08f), "거인을 선택하세요",
            Style(Mathf.RoundToInt(H * 0.055f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, fade));
        ShadowLabel(new Rect(0, H * 0.105f, W, H * 0.04f), "카드를 클릭하거나 1 / 2 키로 선택   ·   마우스를 올리면 미리보기",
            Style(Mathf.RoundToInt(Mathf.Clamp(H * 0.022f, 13, 20)), FontStyle.Normal, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.7f * fade));

        var e = Event.current;
        float cw = Mathf.Clamp(W * 0.2f, 220f, 320f), ch = cw * 0.85f;
        int newHover = -1;
        for (int i = 0; i < options.Length; i++)
        {
            float cx = options.Length == 2 ? (i == 0 ? W * 0.06f : W * 0.94f - cw)
                                           : W * 0.5f - (options.Length * (cw + 20f)) * 0.5f + i * (cw + 20f);
            var r = new Rect(cx, H * 0.47f - ch * 0.5f, cw, ch);
            bool hov = r.Contains(e.mousePosition) && Time.unscaledTime - stateAt > 0.2f;
            if (hov) newHover = i;
            DrawCard(r, options[i], i, selected == i, hov, fade);
            if (hov && e.type == EventType.MouseDown && e.button == 0) { e.Use(); Choose(i); }
        }
        if (e.type == EventType.Repaint || e.type == EventType.MouseMove) hover = newHover;

        // 하단: 선택 표시 + 게임 시작 / 뒤로
        bool can = selected >= 0;
        ShadowLabel(new Rect(0, H * 0.815f, W, 28), can ? $"선택: {options[selected].label}" : "성별을 먼저 선택하세요",
            Style(18, FontStyle.Bold, TextAnchor.MiddleCenter), can ? new Color(options[selected].color.r, options[selected].color.g, options[selected].color.b, fade) : new Color(1, 1, 1, 0.6f * fade));
        float bw = Mathf.Clamp(W * 0.22f, 240f, 340f), bh = Mathf.Clamp(H * 0.075f, 48f, 72f);
        if (MenuButton(new Rect(W * 0.5f - bw * 0.5f, H * 0.87f, bw, bh), "게임 시작", new Color(1f, 0.55f, 0.15f), can, fade)) { StartGame(); return; }
        if (MenuButton(new Rect(24, H * 0.87f + bh * 0.2f, 130, bh * 0.7f), "← 뒤로", new Color(0.42f, 0.45f, 0.52f), true, fade)) { GoTitle(); return; }
    }

    void DrawCard(Rect r, GiantOption o, int index, bool sel, bool hov, float fade)
    {
        Color c = o.color;
        if (hov && !sel) r = new Rect(r.x - 4, r.y - 6, r.width + 8, r.height + 8);
        float bw = sel ? 5f : 3f;
        Color border = sel ? Color.Lerp(c, Color.white, Mathf.PingPong(Time.unscaledTime * 1.5f, 0.35f))
                           : new Color(c.r, c.g, c.b, hov ? 0.85f : 0.4f);
        border.a *= fade;
        Box(new Rect(r.x - bw, r.y - bw, r.width + bw * 2, r.height + bw * 2), border);
        Box(r, new Color(0.08f, 0.09f, 0.12f, 0.92f * fade));

        float bandH = r.height * 0.3f;
        Box(new Rect(r.x, r.y, r.width, bandH), new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, fade));
        ShadowLabel(new Rect(r.x, r.y, r.width, bandH), o.label,
            Style(Mathf.RoundToInt(r.width * 0.11f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, fade));

        var desc = Style(Mathf.RoundToInt(Mathf.Clamp(r.width * 0.055f, 13, 17)), FontStyle.Normal, TextAnchor.MiddleCenter);
        desc.wordWrap = true;
        GUI.color = new Color(0.85f, 0.87f, 0.9f, fade);
        GUI.Label(new Rect(r.x + 14, r.y + bandH + 6, r.width - 28, r.height * 0.42f), o.description, desc);

        string foot = sel ? "선택됨" : $"[ {index + 1} ]";
        ShadowLabel(new Rect(r.x, r.yMax - r.height * 0.2f, r.width, r.height * 0.16f), foot,
            Style(Mathf.RoundToInt(r.width * 0.07f), FontStyle.Bold, TextAnchor.MiddleCenter),
            sel ? new Color(c.r, c.g, c.b, fade) : new Color(1, 1, 1, (hov ? 0.9f : 0.5f) * fade));
    }

    bool MenuButton(Rect rect, string text, Color color, bool enabled, float alpha)
    {
        var e = Event.current;
        bool hov = enabled && rect.Contains(e.mousePosition);
        Rect r = hov ? new Rect(rect.x - 4, rect.y - 3, rect.width + 8, rect.height + 6) : rect;
        Color c = enabled ? color * (hov ? 1f : 0.82f) : new Color(0.25f, 0.26f, 0.3f);
        Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.6f * alpha));
        Box(r, new Color(c.r, c.g, c.b, (enabled ? 0.95f : 0.7f) * alpha));
        ShadowLabel(r, text, Style(Mathf.RoundToInt(r.height * 0.4f), FontStyle.Bold, TextAnchor.MiddleCenter),
            new Color(1, 1, 1, (enabled ? 1f : 0.45f) * alpha));
        if (enabled && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition)
            && Time.unscaledTime - stateAt > 0.2f)
        {
            e.Use();
            return true;
        }
        return false;
    }

    static GUIStyle Style(int size, FontStyle fs, TextAnchor anchor)
        => new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = anchor, richText = true };

    static void Box(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void ShadowLabel(Rect r, string text, GUIStyle s, Color c)
    {
        GUI.color = new Color(0, 0, 0, 0.7f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s);
        GUI.color = c;
        GUI.Label(r, text, s);
        GUI.color = Color.white;
    }
}
