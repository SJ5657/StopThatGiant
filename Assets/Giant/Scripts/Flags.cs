using System.Collections.Generic;
using UnityEngine;

// 깃발 그림: 실제 나라 국기 대신 게임 속 가상의 단체·주(州)를 상징하는 깃발을 코드로 픽셀을 칠해 만듦 (가로 3 : 세로 2).
// 이미지 파일 없음. 한 번 만든 그림은 재사용.
public static class Flags
{
    public struct Flag { public string name; public Texture2D tex; }

    const int W = 120, Hgt = 80;
    delegate Color Painter(float x, float y); // x: 0(깃대 쪽)~1, y: 0(아래)~1

    static readonly Color White = Color.white, Cream = new Color(0.96f, 0.92f, 0.8f), Black = new Color(0.07f, 0.07f, 0.08f),
        Charcoal = new Color(0.2f, 0.21f, 0.24f), Silver = new Color(0.78f, 0.8f, 0.84f), Maroon = new Color(0.5f, 0.06f, 0.12f),
        Red = new Color(0.82f, 0.12f, 0.15f), Orange = new Color(1f, 0.5f, 0.08f), Gold = new Color(0.98f, 0.76f, 0.15f),
        Sand = new Color(0.9f, 0.78f, 0.52f), Terracotta = new Color(0.72f, 0.33f, 0.2f), Brown = new Color(0.4f, 0.24f, 0.12f),
        Forest = new Color(0.08f, 0.38f, 0.2f), Lime = new Color(0.6f, 0.85f, 0.2f), Teal = new Color(0f, 0.5f, 0.52f),
        Turquoise = new Color(0.1f, 0.75f, 0.75f), Sky = new Color(0.45f, 0.7f, 0.92f), Ice = new Color(0.75f, 0.9f, 1f),
        Navy = new Color(0.06f, 0.12f, 0.35f), Space = new Color(0.04f, 0.04f, 0.16f), Purple = new Color(0.4f, 0.15f, 0.55f),
        Pink = new Color(1f, 0.55f, 0.75f), Gray = new Color(0.45f, 0.47f, 0.5f);

    static readonly (string name, Painter paint)[] All =
    {
        ("붉은매 연합", (x, y) => Star(x, y, 0.5f, 0.5f, 0.2f, 0f) ? White : Band(x, y, 0.09f) ? Gold : Maroon),
        ("북부 해안주", (x, y) => Circle(x, y, 0.2f, 0.75f, 0.11f) && !Circle(x, y, 0.2f, 0.75f, 0.07f) ? White
                            : y > 0.5f + Mathf.Sin(x * 14f) * 0.06f ? Navy : Teal),
        ("은빛 늑대단", (x, y) => Chevron(x, y, 0.32f) ? Silver : Ring(x, y, 0.68f, 0.5f, 0.22f, 0.05f) ? Silver : Charcoal),
        ("황금 태양 공화국", Sun),
        ("청룡 기사단", (x, y) => Border(x, y, 0.07f) ? White : Diamond(x, y, 0.5f, 0.5f, 0.28f) ? Gold : Teal),
        ("남부 사막주", (x, y) => Chevron(x, y, 0.38f) ? Brown : Circle(x, y, 0.72f, 0.66f, 0.12f) ? Gold : y > 0.42f ? Sand : Terracotta),
        ("자유 항공 연맹", (x, y) => Star(x, y, 0.18f, 0.5f, 0.15f, 0f) ? Navy : Mathf.Abs(y - 0.5f) < 0.06f && x > 0.3f ? White : Sky),
        ("강철 노동조합", Gear),
        ("서부 산악주", Mountains),
        ("보라별 동맹", (x, y) => Star(x, y, 0.5f, 0.5f, 0.17f, 0f) ? White : Saltire(x, y, 0.08f) ? Orange : Purple),
        ("동방 무역회사", (x, y) => Circle(x, y, 0.5f, 0.5f, 0.18f) ? Red : (x < 0.5f) == (y > 0.5f) ? Teal : Cream),
        ("거인 감시단", Eye),
        ("은하 탐사대", Galaxy),
        ("초록숲 협회", (x, y) => x < 0.25f ? Lime : Leaf(x, y) ? White : Forest),
        ("붉은 등대 항구", Lighthouse),
        ("시민 방위대", (x, y) => Shield(x, y) ? White : Mathf.FloorToInt(y * 5f) % 2 == 0 ? Navy : Orange),
        ("얼음 왕관 왕국", Crown),
        ("분홍 고양이 클럽", Cat),
        ("번개 레이서 팀", (x, y) => Mathf.Abs(y - (0.5f + (Mathf.PingPong(x * 6f, 1f) - 0.5f) * 0.35f)) < 0.09f ? Gold : Black),
        ("바다거북 섬", (x, y) => Turtle(x, y) ? Gold : y < 0.18f ? White : Turquoise),
    };

    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    public static Flag Random()
    {
        var f = All[UnityEngine.Random.Range(0, All.Length)];
        if (!cache.TryGetValue(f.name, out var tex) || !tex) cache[f.name] = tex = Paint(f.paint);
        return new Flag { name = f.name, tex = tex };
    }

    static Texture2D Paint(Painter p)
    {
        var tex = new Texture2D(W, Hgt, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[W * Hgt];
        for (int j = 0; j < Hgt; j++)
            for (int i = 0; i < W; i++)
                px[j * W + i] = p((i + 0.5f) / W, (j + 0.5f) / Hgt);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ───── 도형 도우미 (좌표는 깃발 비율 3:2를 감안해 원이 찌그러지지 않게 x에 1.5를 곱함) ─────
    static bool Circle(float x, float y, float cx, float cy, float r) { float dx = (x - cx) * 1.5f, dy = y - cy; return dx * dx + dy * dy < r * r; }
    static bool Ring(float x, float y, float cx, float cy, float r, float t) => Circle(x, y, cx, cy, r) && !Circle(x, y, cx, cy, r - t);
    static bool Border(float x, float y, float t) => x < t / 1.5f || x > 1f - t / 1.5f || y < t || y > 1f - t;
    static bool Band(float x, float y, float t) => Mathf.Abs((y - 0.5f) - (x - 0.5f) * 0.67f) < t * 1.2f;       // 왼쪽 아래 → 오른쪽 위 띠
    static bool Saltire(float x, float y, float t) => Mathf.Abs((y - 0.5f) - (x - 0.5f) * 0.67f) < t || Mathf.Abs((y - 0.5f) + (x - 0.5f) * 0.67f) < t;
    static bool Chevron(float x, float y, float depth) => x < depth * (1f - Mathf.Abs(y - 0.5f) * 2f);           // 깃대 쪽 삼각형
    static bool Diamond(float x, float y, float cx, float cy, float r) => Mathf.Abs((x - cx) * 1.5f) + Mathf.Abs(y - cy) < r;

    // 꼭짓점이 위를 향한 별 (rot = 회전 각도)
    static bool Star(float x, float y, float cx, float cy, float r, float rot)
    {
        float dx = (x - cx) * 1.5f, dy = y - cy;
        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg - 90f - rot, dist = Mathf.Sqrt(dx * dx + dy * dy);
        if (dist > r) return false;
        float seg = Mathf.Repeat(ang, 72f);
        float a = Mathf.Abs(seg - 36f) * Mathf.Deg2Rad;         // 0 = 안쪽 꼭짓점 방향, 36° = 바깥 꼭짓점
        Vector2 outer = new Vector2(Mathf.Cos(36f * Mathf.Deg2Rad), Mathf.Sin(36f * Mathf.Deg2Rad)) * r, inner = new Vector2(0.38f * r, 0f);
        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        Vector2 e = outer - inner;
        float denom = dir.x * e.y - dir.y * e.x;
        float edge = Mathf.Abs(denom) < 1e-6f ? r : (inner.x * e.y - inner.y * e.x) / denom;
        return dist < edge;
    }

    // ───── 문양 ─────
    // 가운데 둥근 해와 둘레로 뻗는 햇살
    static Color Sun(float x, float y)
    {
        float dx = (x - 0.5f) * 1.5f, dy = y - 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
        if (d < 0.17f) return Gold;
        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        if (d < 0.32f && Mathf.Repeat(ang, 30f) < 11f) return Gold;
        return Orange;
    }

    // 톱니바퀴: 톱니 달린 고리
    static Color Gear(float x, float y)
    {
        float dx = (x - 0.5f) * 1.5f, dy = y - 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        float outer = Mathf.Repeat(ang, 40f) < 20f ? 0.3f : 0.25f;
        if (d < outer && d > 0.12f) return Orange;
        return Gray;
    }

    // 눈 덮인 봉우리 두 개와 작은 별
    static Color Mountains(float x, float y)
    {
        float peak1 = 0.62f - Mathf.Abs(x - 0.35f) * 1.4f, peak2 = 0.5f - Mathf.Abs(x - 0.7f) * 1.3f;
        float m = Mathf.Max(peak1, peak2);
        if (y < m) return y > m - 0.08f ? White : Charcoal;
        if (Star(x, y, 0.85f, 0.8f, 0.08f, 0f)) return White;
        return Forest;
    }

    // 검은 바탕에 빨간 눈
    static Color Eye(float x, float y)
    {
        float dx = (x - 0.5f) * 1.5f / 0.36f, dy = (y - 0.5f) / 0.19f;
        if (dx * dx + dy * dy < 1f)
        {
            if (Circle(x, y, 0.5f, 0.5f, 0.12f)) return Circle(x, y, 0.5f, 0.5f, 0.05f) ? Black : Red;
            return White;
        }
        return Black;
    }

    // 짙은 남색 우주에 흩뿌린 별과 노란 고리 행성
    static Color Galaxy(float x, float y)
    {
        if (Circle(x, y, 0.62f, 0.5f, 0.13f)) return Gold;
        float rx = (x - 0.62f) * 1.5f, ry = (y - 0.5f) * 3.2f + rx * 0.6f;
        float rd = Mathf.Sqrt(rx * rx + ry * ry);
        if (rd > 0.21f && rd < 0.25f) return Gold;
        float h = Mathf.Sin(Mathf.Floor(x * 40f) * 12.9898f + Mathf.Floor(y * 27f) * 78.233f) * 43758.5453f;
        if (h - Mathf.Floor(h) > 0.965f) return White;
        return Space;
    }

    // 나뭇잎 (기울어진 타원 + 가운데 잎맥)
    static bool Leaf(float x, float y)
    {
        float px = (x - 0.62f) * 1.5f, py = y - 0.5f;
        float c = 0.7f, s = 0.7f, u = px * c + py * s, v = -px * s + py * c;
        bool inside = (u / 0.28f) * (u / 0.28f) + (v / 0.13f) * (v / 0.13f) < 1f;
        return inside && Mathf.Abs(v) > 0.012f;
    }

    // 빨간 바탕에 흰 등대와 노란 불빛
    static Color Lighthouse(float x, float y)
    {
        float cx = 0.5f, half = 0.07f - (y - 0.15f) * 0.05f;
        if (y > 0.15f && y < 0.65f && Mathf.Abs(x - cx) < half) return Mathf.FloorToInt((y - 0.15f) * 10f) % 2 == 0 ? White : Black;
        if (y >= 0.65f && y < 0.75f && Mathf.Abs(x - cx) < 0.05f) return Gold;
        if (y >= 0.75f && y < 0.85f && Mathf.Abs(x - cx) < 0.06f * (0.85f - y) / 0.1f) return Black;
        if (y >= 0.65f && y < 0.75f && Mathf.Abs(y - 0.7f) < (Mathf.Abs(x - cx) - 0.05f) * 0.25f + 0.01f && Mathf.Abs(x - cx) < 0.35f) return new Color(1f, 0.9f, 0.5f);
        return y < 0.15f ? Navy : Red;
    }

    // 방패 모양 (위는 네모, 아래는 뾰족)
    static bool Shield(float x, float y)
    {
        float dx = Mathf.Abs(x - 0.5f) * 1.5f;
        if (y > 0.82f || y < 0.15f || dx > 0.22f) return false;
        if (y > 0.45f) return true;
        return dx < 0.22f * (y - 0.15f) / 0.3f;
    }

    // 하늘색 바탕에 흰 왕관
    static Color Crown(float x, float y)
    {
        float dx = (x - 0.5f) * 1.5f;
        if (Mathf.Abs(dx) < 0.3f && y > 0.28f && y < 0.4f) return White;               // 왕관 띠
        if (Mathf.Abs(dx) < 0.3f && y >= 0.4f)
        {
            float k = Mathf.Repeat(dx + 0.3f, 0.15f) / 0.15f;                           // 뾰족한 봉우리 4개
            float tip = 0.4f + (1f - Mathf.Abs(k - 0.5f) * 2f) * 0.25f;
            if (y < tip) return White;
        }
        return Ice;
    }

    // 분홍 바탕에 흰 고양이 얼굴 (귀 두 개)
    static Color Cat(float x, float y)
    {
        if (Circle(x, y, 0.5f, 0.45f, 0.24f))
        {
            if (Circle(x, y, 0.42f, 0.5f, 0.035f) || Circle(x, y, 0.58f, 0.5f, 0.035f)) return Black;
            return White;
        }
        float dx = (x - 0.5f) * 1.5f;
        for (int s = -1; s <= 1; s += 2)
        {
            float ex = dx - s * 0.15f;
            if (y > 0.55f && y < 0.82f && Mathf.Abs(ex) < 0.1f * (0.82f - y) / 0.27f) return White;
        }
        return Pink;
    }

    // 바다거북: 둥근 등딱지와 네 다리, 머리
    static bool Turtle(float x, float y)
    {
        float dx = (x - 0.5f) * 1.5f, dy = y - 0.55f;
        if ((dx / 0.26f) * (dx / 0.26f) + (dy / 0.2f) * (dy / 0.2f) < 1f) return true;
        if (Circle(x, y, 0.5f, 0.8f, 0.06f)) return true;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                if (Circle(x, y, 0.5f + sx * 0.17f, 0.55f + sy * 0.16f, 0.06f)) return true;
        return false;
    }
}
