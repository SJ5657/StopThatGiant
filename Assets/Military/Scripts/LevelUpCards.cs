using System;
using System.Collections.Generic;
using UnityEngine;

// 메가봉크 스타일 레벨업 카드 (패시브 아이템).
// 레벨업하면 게임을 멈추고 카드 3장을 보여줌 → 마우스 클릭 또는 1/2/3 키로 1장 선택.
// 카드는 '새 패시브 아이템 획득' 또는 '보유 아이템 강화(Lv +1)'. 아이템은 최대 maxItems(기본 2)개까지만 보유하고,
// 슬롯이 다 차면 그 뒤로는 보유한 아이템의 강화 카드만 나옴.
// 카드마다 희귀도(일반~전설)가 따로 굴려지고, 희귀도가 높을수록 강화 폭이 큼. 레벨이 오를수록 높은 희귀도가 잘 나옴.
// 한 번에 여러 레벨이 오르면 그 횟수만큼 연속으로 고름.
public class LevelUpCards : MonoBehaviour
{
    public static LevelUpCards Instance { get; private set; }
    public static bool IsChoosing => Instance != null && Instance.choosing;

    [Header("패시브 아이템")]
    [Tooltip("동시에 보유할 수 있는 패시브 아이템 수. 다 차면 보유 아이템 강화만 나옴")] public int maxItems = 2;

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

    // 패시브 아이템: 보유하면 해당 능력치가 오르고, 강화할 때마다 더 오름
    class Item
    {
        public string name, icon, stat, desc;
        public Color color;
        public float baseAmount;
        public Func<float, string> amountText;   // 카드 가운데 큰 글씨 (예: +12%)
        public Func<string> current;             // 현재 값
        public Func<float, string> preview;      // 고른 뒤 값
        public Action<float> apply;
        public Func<bool> available;             // 상한 도달 시 카드에서 제외
        public bool capAmount;                   // 감소형(방어력/지구력): 한 번에 60%까지만
        public int level;                        // 0 = 미보유
    }

    struct Card { public Item item; public int rarity; public float amount; }

    readonly List<Item> items = new List<Item>();
    readonly List<Item> owned = new List<Item>();
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
    bool sizeBaseSet;

    bool SlotsFull => owned.Count >= maxItems;

    void Awake() { Instance = this; }

    void Start()
    {
        lv = LevelSystem.Instance ? LevelSystem.Instance : FindObjectOfType<LevelSystem>();
        ctrl = FindObjectOfType<GiantController>();
        hp = GiantHealth.Instance ? GiantHealth.Instance : FindObjectOfType<GiantHealth>();
        st = ctrl ? ctrl.GetComponent<GiantStamina>() : null; if (!st) st = FindObjectOfType<GiantStamina>();

        if (ctrl) { baseWalk = ctrl.walkSpeed; baseRun = ctrl.runSpeed; }
        if (st) { baseDrain = st.runDrain; baseWalkRegen = st.walkRegen; baseIdleRegen = st.idleRegen; }

        BuildItems();
        if (lv) lv.OnLevelUp += HandleLevelUp;
    }

    void OnDestroy()
    {
        if (lv) lv.OnLevelUp -= HandleLevelUp;
        if (choosing) Time.timeScale = 1f; // 카드 고르는 중에 씬이 바뀌어도 게임이 멈춘 채로 남지 않게
        if (Instance == this) Instance = null;
    }

    // ───────────── 패시브 아이템 목록 ─────────────
    static string Pct(float v) => $"+{v * 100f:0.#}%";

    void BuildItems()
    {
        if (hp)
        {
            Add("거인의 심장", "심장", "최대 체력", "최대 HP 증가.\n늘어난 만큼 즉시 회복.", new Color(0.95f, 0.3f, 0.35f), 200f,
                a => $"+{a:0}", () => $"{hp.maxHP:0}", a => $"{hp.maxHP + a:0}",
                a => hp.AddMaxHP(a));
            Add("트롤의 피", "재생", "체력 재생", "매초 HP가 자동으로 회복.", new Color(0.4f, 0.85f, 0.45f), 4f,
                a => $"+{a:0.#}/초", () => $"{hp.hpRegen:0.#}/초", a => $"{hp.hpRegen + a:0.#}/초",
                a => hp.hpRegen += a);
            Add("강철 피부", "피부", "방어력", "군대에게 받는 피해 감소.", new Color(0.65f, 0.72f, 0.8f), 0.05f,
                a => $"-{a * 100f:0.#}%", () => $"피해 {hp.damageTakenMultiplier * 100f:0}%",
                a => $"피해 {hp.damageTakenMultiplier * (1f - a) * 100f:0}%",
                a => hp.damageTakenMultiplier *= (1f - a),
                () => hp.damageTakenMultiplier > 0.25f);
        }
        if (ctrl)
        {
            Add("바람의 샌들", "샌들", "이동 속도", "걷기·달리기 속도 증가.", new Color(0.45f, 0.85f, 1f), 0.06f,
                Pct, () => $"{moveMult * 100f:0}%", a => $"{(moveMult + a) * 100f:0}%",
                a => { moveMult += a; ctrl.walkSpeed = baseWalk * moveMult; ctrl.runSpeed = baseRun * moveMult; });
        }
        if (st)
        {
            Add("무쇠 폐", "폐", "최대 스테미나", "스테미나 최대치 증가.\n늘어난 만큼 즉시 채움.", new Color(0.95f, 0.8f, 0.25f), 15f,
                a => $"+{a:0}", () => $"{st.maxStamina:0}", a => $"{st.maxStamina + a:0}",
                a => st.AddMaxStamina(a));
            Add("숨고르기 부적", "부적", "스테미나 회복", "걷거나 멈췄을 때\n스테미나 회복 속도 증가.", new Color(1f, 0.6f, 0.75f), 0.12f,
                Pct, () => $"{staminaRegenMult * 100f:0}%", a => $"{(staminaRegenMult + a) * 100f:0}%",
                a => { staminaRegenMult += a; st.walkRegen = baseWalkRegen * staminaRegenMult; st.idleRegen = baseIdleRegen * staminaRegenMult; });
            Add("철인의 각반", "각반", "지구력", "달릴 때 스테미나 소모 감소.", new Color(0.85f, 0.55f, 0.3f), 0.08f,
                a => $"-{a * 100f:0.#}%", () => $"소모 {drainMult * 100f:0}%", a => $"소모 {drainMult * (1f - a) * 100f:0}%",
                a => { drainMult *= (1f - a); st.runDrain = baseDrain * drainMult; },
                () => drainMult > 0.25f);
        }
        if (ctrl)
        {
            Add("거대화 버섯", "버섯", "몸 크기", "몸이 커져 더 넓게 부수고\n보폭이 커져 더 빨라짐.", new Color(0.8f, 0.45f, 1f), 0.05f,
                Pct, () => $"{sizeMult * 100f:0}%", a => $"{Mathf.Min(sizeMult + a, maxSizeMultiplier) * 100f:0}%",
                a => { if (!sizeBaseSet) { baseModelScale = ctrl.ModelScale; sizeBaseSet = true; } sizeMult = Mathf.Min(sizeMult + a, maxSizeMultiplier); }, // 성별 선택 후의 모델 크기 기준
                () => sizeMult < maxSizeMultiplier - 0.001f, capAmount: false);
        }
        if (lv)
        {
            Add("현자의 안경", "안경", "경험치 획득", "얻는 경험치 증가.\n레벨업이 빨라짐.", new Color(0.4f, 0.65f, 1f), 0.08f,
                Pct, () => $"{lv.xpMultiplier * 100f:0}%", a => $"{(lv.xpMultiplier + a) * 100f:0}%",
                a => lv.xpMultiplier += a);
        }
    }

    void Add(string name, string icon, string stat, string desc, Color color, float baseAmount, Func<float, string> amountText,
             Func<string> current, Func<float, string> preview, Action<float> apply, Func<bool> available = null, bool capAmount = true)
    {
        items.Add(new Item
        {
            name = name, icon = icon, stat = stat, desc = desc, color = color, baseAmount = baseAmount, amountText = amountText,
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
        // 슬롯이 남아 있으면 새 아이템 + 보유 아이템 강화, 다 찼으면 보유 아이템 강화만
        var pool = (SlotsFull ? owned : items).FindAll(it => it.available == null || it.available());
        if (pool.Count == 0) { pending = 0; if (choosing) Close(); return; }

        for (int i = pool.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        // 후보가 3개보다 적으면(보유 아이템 강화만 남은 경우) 같은 아이템이 다른 희귀도로 여러 장 나올 수 있음
        cardCount = 3;
        for (int i = 0; i < cardCount; i++)
        {
            var it = pool[i % pool.Count];
            int r = RollRarity();
            float amount = it.baseAmount * RarityPower[r];
            if (it.capAmount) amount = Mathf.Min(amount, 0.6f); // 감소형(방어력/지구력)은 한 번에 60%까지만
            cards[i] = new Card { item = it, rarity = r, amount = amount };
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
        if (c.item.level == 0) owned.Add(c.item); // 새 아이템 획득
        c.item.apply(c.amount);
        c.item.level++;
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
        if (GameStartMenu.InMenu) return; // 시작 메뉴 중에는 HUD 숨김
        if (!choosing) { DrawItemSlots(); return; }
        GUI.depth = -100;
        float W = Screen.width, H = Screen.height;
        float now = Time.unscaledTime - openedAt;
        var e = Event.current;

        // 배경 어둡게
        Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f * Mathf.Clamp01(now / 0.2f)));
        DrawItemSlots();

        float cw = Mathf.Clamp(W * 0.2f, 190f, 270f), ch = cw * 1.45f, gap = cw * 0.14f;
        float total = cardCount * cw + (cardCount - 1) * gap;
        float x0 = (W - total) * 0.5f, y0 = H * 0.5f - ch * 0.5f + 24f;

        // 제목
        var title = Style(Mathf.RoundToInt(cw * 0.17f), FontStyle.Bold, TextAnchor.MiddleCenter);
        ShadowLabel(new Rect(0, y0 - cw * 0.42f, W, cw * 0.2f), "LEVEL UP!" + (lv ? $"  LV {lv.Level}" : ""), title, new Color(0.55f, 0.9f, 1f));
        var sub = Style(15, FontStyle.Bold, TextAnchor.MiddleCenter);
        string head = SlotsFull ? $"아이템 슬롯 가득 참 ({owned.Count}/{maxItems}) · 보유 아이템을 강화하세요"
                                : $"패시브 아이템을 고르세요 (슬롯 {owned.Count}/{maxItems})";
        string subText = head + "   ( 클릭 또는 1 / 2 / 3 )" + (pending > 1 ? $"   ·   남은 선택 {pending}" : "");
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

    // 보유 패시브 아이템 슬롯 (HP/스테미나 바 아래)
    void DrawItemSlots()
    {
        const float size = 46f, gap = 8f, x0 = 20f, y = 70f;
        for (int i = 0; i < maxItems; i++)
        {
            var r = new Rect(x0 + i * (size + gap), y, size, size);
            Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.6f));
            if (i >= owned.Count)
            {
                Box(r, new Color(0.15f, 0.16f, 0.2f, 0.7f));
                GUI.color = new Color(1, 1, 1, 0.3f);
                GUI.Label(r, "빈 슬롯", Style(10, FontStyle.Normal, TextAnchor.MiddleCenter));
                GUI.color = Color.white;
                continue;
            }
            var it = owned[i];
            Box(r, new Color(it.color.r * 0.45f, it.color.g * 0.45f, it.color.b * 0.45f, 0.95f));
            ShadowLabel(new Rect(r.x, r.y + 2, r.width, r.height - 14), it.icon, Style(14, FontStyle.Bold, TextAnchor.MiddleCenter), Color.white);
            ShadowLabel(new Rect(r.x, r.yMax - 16, r.width - 3, 15), $"Lv{it.level}", Style(11, FontStyle.Bold, TextAnchor.MiddleRight), it.color);
        }
    }

    void DrawCard(Rect r, Card c, int index, float a, bool hover)
    {
        Color rc = RarityColors[c.rarity];
        var it = c.item;
        bool isNew = it.level == 0;
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

        // 새 아이템 / 강화 배지
        float y = r.y + bandH + cw * 0.05f;
        string badge = isNew ? "NEW  새 아이템" : $"강화  Lv {it.level} → {it.level + 1}";
        Color badgeCol = isNew ? new Color(1f, 0.85f, 0.3f, a) : new Color(0.55f, 0.9f, 1f, a);
        ShadowLabel(new Rect(r.x, y, r.width, cw * 0.08f), badge,
            Style(Mathf.RoundToInt(cw * 0.055f), FontStyle.Bold, TextAnchor.MiddleCenter), badgeCol);

        // 아이템 이름 + 능력치
        y += cw * 0.09f;
        ShadowLabel(new Rect(r.x + 8, y, r.width - 16, cw * 0.13f), it.name,
            Style(Mathf.RoundToInt(cw * 0.09f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(it.color.r, it.color.g, it.color.b, a));
        y += cw * 0.13f;
        GUI.color = new Color(0.75f, 0.77f, 0.82f, a);
        GUI.Label(new Rect(r.x, y, r.width, cw * 0.08f), it.stat, Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.055f, 12, 15)), FontStyle.Bold, TextAnchor.MiddleCenter));

        // 강화량 (크게)
        y += cw * 0.09f;
        ShadowLabel(new Rect(r.x, y, r.width, cw * 0.22f), it.amountText(c.amount),
            Style(Mathf.RoundToInt(cw * 0.15f), FontStyle.Bold, TextAnchor.MiddleCenter), new Color(rc.r, rc.g, rc.b, a));

        // 설명
        y += cw * 0.25f;
        var desc = Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.052f, 12, 14)), FontStyle.Normal, TextAnchor.UpperCenter);
        desc.wordWrap = true;
        GUI.color = new Color(0.82f, 0.84f, 0.88f, a);
        GUI.Label(new Rect(r.x + 12, y, r.width - 24, cw * 0.26f), it.desc, desc);

        // 현재 → 강화 후
        float by = r.yMax - cw * 0.36f;
        Box(new Rect(r.x + 10, by, r.width - 20, 1), new Color(1, 1, 1, 0.12f * a));
        var cmp = Style(Mathf.RoundToInt(Mathf.Clamp(cw * 0.058f, 12, 16)), FontStyle.Bold, TextAnchor.MiddleCenter);
        GUI.color = new Color(1, 1, 1, a);
        GUI.Label(new Rect(r.x, by + 6, r.width, 24),
            $"{it.current()}  →  <color=#{ColorUtility.ToHtmlStringRGB(rc)}>{it.preview(c.amount)}</color>", cmp);

        // 슬롯 안내 + 단축키
        var small = Style(12, FontStyle.Normal, TextAnchor.MiddleCenter);
        GUI.color = new Color(0.7f, 0.72f, 0.78f, a);
        GUI.Label(new Rect(r.x, by + 30, r.width, 18), isNew ? $"슬롯 사용 ({owned.Count + 1}/{maxItems})" : "보유 중", small);
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
