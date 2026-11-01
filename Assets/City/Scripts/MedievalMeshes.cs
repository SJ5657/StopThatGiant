using System.Collections.Generic;
using UnityEngine;

// 중세 마을용 메시 (박공지붕·사각뿔 지붕·성가퀴·나무 수관). 모두 가로·세로·높이 1인 상자(-0.5~0.5) 안에 맞춰서
// 오브젝트 스케일 = 실제 크기 (건물 파괴·높이 판정이 상자 건물과 똑같이 동작).
// 버텍스 컬러에 무게중심 좌표를 넣어 OutlineMesh 셰이더가 모서리에 윤곽선을 그림 (사각형 면의 대각선은 숨김).
public static class MedievalMeshes
{
    class Builder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Color> c = new List<Color>();

        // 삼각형 (eAB·eBC·eCA = 그 모서리에 윤곽선을 그릴지). outward 쪽을 바라보게 감는 방향을 맞춤
        public void Tri(Vector3 a, Vector3 b, Vector3 cc, Vector3 outward, bool eAB = true, bool eBC = true, bool eCA = true)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, cc - a), outward) < 0f)
            {
                var t = b; b = cc; cc = t;          // 뒤집으면 모서리도 바뀜: AB↔CA(=AC), BC는 그대로
                bool e = eAB; eAB = eCA; eCA = e;
            }
            // 성분 k가 0인 곳 = 그 모서리. 숨길 모서리는 그 성분을 셋 다 1로
            v.Add(a);  c.Add(new Color(1, eCA ? 0 : 1, eAB ? 0 : 1));
            v.Add(b);  c.Add(new Color(eBC ? 0 : 1, 1, eAB ? 0 : 1));
            v.Add(cc); c.Add(new Color(eBC ? 0 : 1, eCA ? 0 : 1, 1));
        }

        // 사각형 p0-p1-p2-p3 (둘레 순서). 대각선 p0-p2는 숨김
        public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
        {
            Tri(p0, p1, p2, outward, true, true, false);
            Tri(p2, p3, p0, outward, true, true, false);
        }

        public void Box(Vector3 min, Vector3 max)
        {
            Vector3 P(int x, int y, int z) => new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
            Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), Vector3.back);
            Quad(P(0, 0, 1), P(0, 1, 1), P(1, 1, 1), P(1, 0, 1), Vector3.forward);
            Quad(P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), P(0, 0, 1), Vector3.left);
            Quad(P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), Vector3.right);
            Quad(P(0, 0, 0), P(0, 0, 1), P(1, 0, 1), P(1, 0, 0), Vector3.down);
            Quad(P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), Vector3.up);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetColors(c);
            var idx = new int[v.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            m.SetTriangles(idx, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    // 박공지붕: 아래는 상자 밑면 전체, 용마루는 맨 위 가운데. alongX = 용마루가 X 방향
    public static Mesh Gable(bool alongX)
    {
        var b = new Builder();
        Vector3 S(float x, float y, float z) => alongX ? new Vector3(x, y, z) : new Vector3(z, y, x);
        Vector3 b00 = S(-0.5f, -0.5f, -0.5f), b10 = S(0.5f, -0.5f, -0.5f), b01 = S(-0.5f, -0.5f, 0.5f), b11 = S(0.5f, -0.5f, 0.5f);
        Vector3 r0 = S(-0.5f, 0.5f, 0f), r1 = S(0.5f, 0.5f, 0f);
        b.Quad(b01, b11, r1, r0, S(0, 1, 1));    // 앞 비탈
        b.Quad(b10, b00, r0, r1, S(0, 1, -1));   // 뒤 비탈
        b.Tri(b00, b01, r0, S(-1, 0, 0));        // 박공 (세모 벽)
        b.Tri(b11, b10, r1, S(1, 0, 0));
        b.Quad(b00, b10, b11, b01, Vector3.down);
        return b.Build(alongX ? "GableRoofX" : "GableRoofZ");
    }

    // 사각뿔 지붕 (탑·뾰족탑)
    public static Mesh Pyramid()
    {
        var b = new Builder();
        Vector3 top = new Vector3(0f, 0.5f, 0f);
        Vector3 b00 = new Vector3(-0.5f, -0.5f, -0.5f), b10 = new Vector3(0.5f, -0.5f, -0.5f), b01 = new Vector3(-0.5f, -0.5f, 0.5f), b11 = new Vector3(0.5f, -0.5f, 0.5f);
        b.Tri(b00, b10, top, new Vector3(0, 0.5f, -1));
        b.Tri(b10, b11, top, new Vector3(1, 0.5f, 0));
        b.Tri(b11, b01, top, new Vector3(0, 0.5f, 1));
        b.Tri(b01, b00, top, new Vector3(-1, 0.5f, 0));
        b.Quad(b00, b10, b11, b01, Vector3.down);
        return b.Build("PyramidRoof");
    }

    // 성가퀴: 성벽 위 톱니 n개 (alongX = X 방향으로 늘어섬). 톱니 사이는 비어 있음
    public static Mesh Crenels(int n, bool alongX)
    {
        var b = new Builder();
        float p = 1f / n, tw = p * 0.55f;
        for (int i = 0; i < n; i++)
        {
            float xc = -0.5f + p * (i + 0.5f);
            Vector3 mn = new Vector3(xc - tw * 0.5f, -0.5f, -0.5f), mx = new Vector3(xc + tw * 0.5f, 0.5f, 0.5f);
            if (!alongX) { mn = new Vector3(mn.z, mn.y, mn.x); mx = new Vector3(mx.z, mx.y, mx.x); }
            b.Box(mn, mx);
        }
        return b.Build($"Crenels{n}{(alongX ? "X" : "Z")}");
    }

    // 나무 수관: 각진 구 (정이십면체)
    public static Mesh Blob()
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var p = new[]
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < p.Length; i++) p[i] = p[i].normalized * 0.5f;
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        var b = new Builder();
        for (int i = 0; i < f.Length; i += 3)
        {
            Vector3 a = p[f[i]], bb = p[f[i + 1]], c = p[f[i + 2]];
            b.Tri(a, bb, c, (a + bb + c) / 3f);
        }
        return b.Build("TreeCanopy");
    }
}
