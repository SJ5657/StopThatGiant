using System;
using System.Collections.Generic;
using UnityEngine;

// 메가봉크 스타일 레벨업 카드.
// 레벨업하면 게임을 멈추고 무작위 스탯 강화 카드 3장을 보여줌 → 마우스 클릭 또는 1/2/3 키로 1장 선택.
// 카드마다 희귀도(일반~전설)가 따로 굴려지고, 희귀도가 높을수록 강화 폭이 큼. 레벨이 오를수록 높은 희귀도가 잘 나옴.
// 한 번에 여러 레벨이 오르면 그 횟수만큼 연속으로 고름.
public class LevelUpCards : MonoBehaviour
{
    public static LevelUpCards Instance { get; private set; }
    public static bool IsChoosing => Instance != null && Instance.choosing;

    [Header("희귀도 가중치 (일반 / 고급 / 희귀 / 영웅 / 전설)")]
    public float[] rarityWeights = { 60f, 25f, 10f, 4f, 1f };
    [Tooltip("레벨이 1 오를 때마다 높은 희귀도 가중치가 늘어나는 비율")] public float luckPerLevel = 0.04f;
    [Tooltip("카드가 뜬 직후 입력을 무시하는 시간(초) — 이동 중 실수로 고르는 것 방지")] public float inputDelay = 0.4f;

    static readonly string[] RarityNames = { "일반", "고급", "희귀", "영웅", "전설" };
    static readonly Color[] RarityColors =
    {
        new Color(0.72f, 0.74f, 0.78f),
        new Color(0.35f, 0.85f, 0.40f),
        new Color(0.30f, 0.62f, 1.00f),
        new Color(0.74f, 0.42f, 1.00f),
        new Color(1.00f, 0.76f, 0.20f)
    };
    static readonly float[] RarityPower = { 1f, 1.5f, 2.2f, 3.2f, 5f };

    class Upgrade
    {
        public string name, desc;
        public float baseAmount;
        public Func<float, string> amountText;   // 카드 가운데 큰 글씨 (예: +12%)
        public Func<string> current;             // 현재 값
        public Func<float, string> preview;      // 고른 뒤 값
        public Action<float> apply;
        public Func<bool> available;             // 상한 도달 시 카드에서 제외
        public bool capAmount;                   // 감소형(방어력/지구력): 한 번에 60%까지만
        public int taken;
    }

    struct Card { public Upgrade up; public int rarity; public float amount; }

    readonly List<Upgrade> upgrades = new List<Upgrade>();
    readonly Card[] cards = new Card[3];
    int cardCount, pending;
    bool choosing;
    float openedAt, prevTimeScale = 1f;
    CursorLockMode prevLock; bool prevVisible;

    [Header("몸 크기 강화")]
    [Tooltip("몸 크기 최대 배율 (처음 크기 기준)")] public float maxSizeMultiplier = 2.5f;
    [Tooltip("카드 선택 후 커지는 데 걸리는 시간(초)")] public float growTime = 0.8f;

    GiantController ctrl; GiantHealth hp; GiantStamina st; LevelSystem lv;
    float baseWalk, baseRun, baseDrain, baseWalkRegen, baseIdleRegen, baseModelScale = 1f;
    float moveMult = 1f, staminaRegenMult = 1f, drainMult = 1f, sizeMult = 1f, shownSizeMult = 1f;

    void Awake() { Instance = this; }

    void Start()
    {
        lv = LevelSystem.Instance ? LevelSystem.Instance : FindObjectOfType<LevelSystem>();
        ctrl = FindObjectOfType<GiantController>();
        hp = GiantHealth.Instance ? GiantHealth.Instance : FindObjectOfType<GiantHealth>();
        st = ctrl ? ctrl.GetComponent<GiantStamina>() : null; if (!st) st = FindObjectOfType<GiantStamina>();

        if (ctrl) { baseWalk = ctrl.walkSpeed; baseRun = ctrl.runSpeed; baseModelScale = ctrl.ModelScale; }
        if (st) { baseDrain = st.runDrain; baseWalkRegen = st.walkRegen; baseIdleRegen = st.idleRegen; }

        BuildUpgrades();
        if (lv) lv.OnLevelUp += HandleLevelUp;
    }

    void OnDestroy()
    {
        if (lv) lv.OnLevelUp -= HandleLevelUp;
        if (choosing) Time.timeScale = 1f; // 카드 고르는 중에 씬이 바뀌어도 게임이 멈춘 채로 남지 않게
        if (Instance == this) Instance = null;
    }

    // ───────────── 강화 목록 ─────────────
    static string Pct(float v) => $"+{v * 100f:0.#}%";

    void BuildUpgrades()
    {
        if (hp)
        {
            Add("최대 체력", "최대 HP 증가.\n늘어난 만큼 즉시 회복.", 200f,
                a => $"+{a:0}", () => $"{hp.maxHP:0}", a => $"{hp.maxHP + a:0}",
                a => hp.AddMaxHP(a));
            Add("체력 재생", "매초 HP가 자동으로 회복.", 4f,
                a => $"+{a:0.#}/초", () => $"{hp.hpRegen:0.#}/초", a => $"{hp.hpRegen + a:0.#}/초",
                a => hp.hpRegen += a);
            Add("방어력", "군대에게 받는 피해 감소.", 0.05f,
                a => $"-{a * 100f:0.#}%", () => $"피해 {hp.damageTakenMultiplier * 100f:0}%",
                a => $"피해 {hp.damageTakenMultiplier * (1f - a) * 100f:0}%",
                a => hp.damageTakenMultiplier *= (1f - a),
                () => hp.damageTakenMultiplier > 0.25f);
        }
        if (ctrl)
        {
            Add("이동 속도", "걷기·달리기 속도 증가.", 0.06f,
                Pct, () => $"{moveMult * 100f:0}%", a => $"{(moveMult + a) * 100f:0}%",
                a => { moveMult += a; ctrl.walkSpeed = baseWalk * moveMult; ctrl.runSpeed = baseRun * moveMult; });
        }
        if (st)
        {
            Add("최대 스테미나", "스테미나 최대치 증가.\n늘어난 만큼 즉시 채움.", 15f,
                a => $"+{a:0}", () => $"{st.maxStamina:0}", a => $"{st.maxStamina + a:0}",
                a => st.AddMaxStamina(a));
            Add("스테미나 회복", "걷거나 멈췄을 때\n스테미나 회복 속도 증가.", 0.12f,
                Pct, () => $"{staminaRegenMult * 100f:0}%", a => $"{(staminaRegenMult + a) * 100f:0}%",
                a => { staminaRegenMult += a; st.walkRegen = baseWalkRegen * staminaRegenMult; st.idleRegen = baseIdleRegen * staminaRegenMult; });
            Add("지구력", "달릴 때 스테미나 소모 감소.", 0.08f,
                a => $"-{a * 100f:0.#}%", () => $"소모 {drainMult * 100f:0}%", a => $"소모 {drainMult * (1f - a) * 100f:0}%",
                a => { drainMult *= (1f - a); st.runDrain = baseDrain * drainMult; },
                () => drainMult > 0.25f);
        }
        if (ctrl)
        {
            Add("몸 크기", "몸이 커져 더 넓게 부수고\n보폭이 커져 더 빨라짐.", 0.05f,
                Pct, () => $"{sizeMult * 100f:0}%", a => $"{Mathf.Min(sizeMult + a, maxSizeMultiplier) * 100f:0}%",
                a => sizeMult = Mathf.Min(sizeMult + a, maxSizeMultiplier),
                () => sizeMult < maxSizeMultiplier - 0.001f, capAmount: false);
        }
        if (lv)
        {
            Add("경험치 획득", "얻는 경험치 증가.\n레벨업이 빨라짐.", 0.08f,
                Pct, () => $"{lv.xpMultiplier * 100f:0}%", a => $"{(lv.xpMultiplier + a) * 100f:0}%",
                a => lv.xpMultiplier += a);
        }
    }

    void Add(string name, string desc, float baseAmount, Func<float, string> amountText, Func<string> current,
             Func<float, string> preview, Action<float> apply, Func<bool> available = null, bool capAmount = true)
    {
        upgrades.Add(new Upgrade
        {
            name = name, desc = desc, baseAmount = baseAmount, amountText = amountText,
            current = current, preview = preview, apply = apply, available = available,
            capAmount = available != null && capAmount
        });
    }

    // ───────────── 카드 흐름 ─────────────
    void HandleLevelUp(int level)
    {
        pending++;
        if (!choosing) Open();
    }

    void Open()
    {
        var pool = upgrades.FindAll(u => u.available == null || u.available());
        if (pool.Count == 0) { pending = 0; if (choosing) Close(); return; }

        for (int i = pool.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        cardCount = Mathf.Min(3, pool.Count);
        for (int i = 0; i < cardCount; i++)
        {
            int r = RollRarity();
            float amount = pool[i].baseAmount * RarityPower[r];
            if (pool[i].capAmount) amount = Mathf.Min(amount, 0.6f); // 감소형(방어력/지구력)은 한 번에 60%까지만
            cards[i] = new Card { up = pool[i], rarity = r, amount = amount };
        }

        if (!choosing)
        {
            choosing = true;
            prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            prevLock = Cursor.lockState; prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        openedAt = Time.unscaledTime;
    }

    int RollRarity()
    {
        int level = lv ? lv.Level : 1;
        float total = 0f;
        var w = new float[RarityPower.Length];
        for (int i = 0; i < w.Length; i++)
        {
            float baseW = rarityWeights != null && i < rarityWeights.Length ? rarityWeights[i] : 0f;
            w[i] = baseW * (1f + luckPerLevel * (level - 1) * i); // 높은 희귀도일수록 레벨 보정 큼
            total += w[i];
        }
        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < w.Length; i++) { roll -= w[i]; if (roll <= 0f) return i; }
        return 0;
    }

    void Pick(int i)
    {
        if (!choosing || i < 0 || i >= cardCount) return;
        if (Time.unscaledTime - openedAt < inputDelay) return;
        var c = cards[i];
        c.up.apply(c.amount);
        c.up.taken++;
        pending = Mathf.Max(0, pending - 1);
        if (pending > 0) Open(); else Close();
    }

    void Close()
    {
        choosing = false;
        Time.timeScale = prevTimeScale;
        Cursor.lockState = prevLock; Cursor.visible = prevVisible;
    }

    void Update()
    {
        // 몸 크기 강화: 카드 선택 후 게임이 다시 진행되면 부드럽게 커짐
        if (!choosing && ctrl && !Mathf.Approximately(shownSizeMult, sizeMult))
        {
            float step = Mathf.Max(0.01f, sizeMult - 1f) / Mathf.Max(0.05f, growTime) * Time.deltaTime;
            shownSizeMult = Mathf.MoveTowards(shownSizeMult, sizeMult, Mathf.Max(step, 0.3f * Time.deltaTime));
            ctrl.SetModelScale(baseModelScale * shownSizeMult);
        }
        if (!choosing) return;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Pick(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Pick(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) Pick(2);
    }

    // ───────────── 화면 ─────────────
    void OnGUI()
    {
        if (!choosing) return;
        GUI.depth = -100;
        float W = Screen.width, H = Screen.height;
        float now = Time.unscaledTime - openedAt;
        var e = Event.current;

        // 배경 어둡게
        Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f * Mathf.Clamp01(now / 0.2f)));

        float cw = Mathf.Clamp(W * 0.2f, 190f, 270f), ch = cw * 1.45f, gap = cw * 0.14f;
        float total = cardCount * cw + (cardCount - 1) * gap;
        float x0 = (W - total) * 0.5f, y0 = H * 0.5f - ch * 0.5f + 24f;

        // 제목
        var title = Style(Mathf.RoundToInt(cw * 0.17f), FontStyle.Bold, TextAnchor.MiddleCenter);
        ShadowLabel(new Rect(0, y0 - cw * 0.42f, W, cw * 0.2f), "LEVEL UP!" + (lv ? $"  LV {lv.Level}" : ""), title, new Color(0.55f, 0.9f, 1f));
        var sub = Style(15, FontStyle.Bold, TextAnchor.MiddleCenter);
        string subText = "강화할 능력을 하나 고르세요   ( 클릭 또는 1 / 2 / 3 )" + (pending > 1 ? $"   ·   남은 선택 {pending}" : "");
        ShadowLabel(new Rect(0, y0 - cw * 0.2f, W, 24), subText, sub, new Color(1f, 1f, 1f, 0.85f));

        for (int i = 0; i < cardCount; i++)
        {
            // 등장 애니메이션 (순서대로 아래에서 올라옴)
            float t = Mathf.Clamp01((now - i * 0.07f) / 0.28f);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            var r = new Rect(x0 + i * (cw + gap), y0 + (1f - ease) * 70f, cw, ch);

            bool ready = now >= inputDelay;
            bool hover = ready && r.Contains(e.mousePosition);
            if (hover) r = new Rect(r.x - 5, r.y - 12, r.width + 10, r.height + 10);

            DrawCard(r, cards[i], i, ease, hover);

            if (ready && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                Pick(i);
                return;
            }
        }
    }

    void DrawCard(Rect r, Card c, int index, float a, bool hover)
    {
        Color rc = RarityColors[c.rarity];
        // 전설은 테두리가 반짝임
        Color border = c.rarity == 4 ? Color.Lerp(rc, Color.white, Mathf.PingPong(Time.unscaledTime * 1.6f, 0.6f)) : rc;
        border.a = a * (hover ? 1f : 0.85f);

        float bw = hover ? 4f : 3f;
        Box(new Rect(r.x - bw, r.y - bw, r.width + bw * 2, r.height + bw * 2), border);
        Box(r, new Color(0.09f, 0.10f, 0.13f, 0.97f * a));

        float cw = r.width;
        // 희귀도 띠
        float bandH = cw * 0.14f;
        Box(new Rect(r.x, r.y, r.width, bandH), new Color(rc.r * 0.55f, rc.g * 0.55f, rc.b * 0.55f, a));
        ShadowLabel(new Rect(r.x, r.y, r.width, bandH), RarityNames[c.rarity],
            Style(Mathf.RoundToInt(cw * 0.065f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, a));

        // 스탯 이름
        float y = r.y + bandH + cw * 0.08f;
        ShadowLabel(new Rect(r.x + 8, y, r.width - 16, cw * 0.14f), c.up.name,
            Style(Mathf.RoundToInt(cw * 0.095f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(1, 1, 1, a));

        // 강화량 (크게)
        y += cw * 0.2f;
        ShadowLabel(new Rect(r.x, y, r.width, cw * 0.24f), c.up.amountText(c.amount),
            Style(Mathf.RoundToInt(cw * 0.16f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(rc.r, rc.g, rc.b, a));

        // 설명
        y += cw * 0.3f;
        var desc = Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.056f, 12, 15)), FontStyle.Normal, TextAnchor.UpperCenter);
        desc.wordWrap = true;
        GUI.color = new Color(0.82f, 0.84f, 0.88f, a);
        GUI.Label(new Rect(r.x + 12, y, r.width - 24, cw * 0.3f), c.up.desc, desc);

        // 현재 → 강화 후
        float by = r.yMax - cw * 0.36f;
        Box(new Rect(r.x + 10, by, r.width - 20, 1), new Color(1, 1, 1, 0.12f * a));
        var cmp = Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.058f, 12, 16)), FontStyle.Bold, TextAnchor.MiddleCenter);
        GUI.color = new Color(1, 1, 1, a);
        GUI.Label(new Rect(r.x, by + 6, r.width, 24),
            $"{c.up.current()}  →  <color=#{ColorUtility.ToHtmlStringRGB(rc)}>{c.up.preview(c.amount)}</color>", cmp);

        // 보유 횟수 + 단축키
        var small = Style(12, FontStyle.Normal, TextAnchor.MiddleCenter);
        GUI.color = new Color(0.7f, 0.72f, 0.78f, a);
        GUI.Label(new Rect(r.x, by + 30, r.width, 18), c.up.taken > 0 ? $"보유 {c.up.taken}회" : "새 강화", small);
        var key = Style(Mathf.RoundToInt(cw * 0.07f), FontStyle.Bold, TextAnchor.MiddleCenter);
        GUI.color = new Color(1, 1, 1, (hover ? 1f : 0.6f) * a);
        GUI.Label(new Rect(r.x, r.yMax - cw * 0.12f, r.width, cw * 0.1f), $"[ {index + 1} ]", key);
        GUI.color = Color.white;
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
