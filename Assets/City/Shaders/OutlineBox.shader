// 박스(큐브) 메시의 모서리에 일정 두께의 윤곽선을 그리는 셰이더.
// 기본 Cube 메시 + 오브젝트 스케일만으로 동작 (모든 건물이 메시/머티리얼 공유 → GPU 인스턴싱)
// 해(Directional Light)의 그림자를 받아 그늘진 곳을 어둡게 칠함 (만화풍: 그림자 색 _ShadowColor를 곱함)
Shader "City/OutlineBox"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.95,0.95,0.95,1)
        _LineColor ("Line Color", Color) = (0.12,0.12,0.14,1)
        _LineWidth ("Line Width (m)", Float) = 0.6
        _MinLinePixels ("Min Line Width (px)", Float) = 1.2
        _Shade ("Face Shading", Range(0,1)) = 0.25
        _ShadowColor ("Shadow Color", Color) = (0.62,0.66,0.78,1)
        [Toggle] _Windows ("Windows (건물 옆면 창문)", Float) = 0
        _WindowColor ("Window Color", Color) = (0.87,0.9,0.95,1)
        _FloorHeight ("Floor Height (m)", Float) = 4.6
        _WindowSpacing ("Window Spacing (m)", Float) = 4.8
        _WindowSize ("Window Size (m) xy", Vector) = (2.6, 2.7, 0, 0)
        _WindowMargin ("Wall Margin (m)", Float) = 1.4
        [Toggle] _ModernWindows ("Band/Tall Windows (현대식 띠·세로 창 섞기)", Float) = 1
        [Header(Medieval)]
        [Toggle] _Timber ("Half-timber (목조 골조 벽)", Float) = 0
        _BeamColor ("Beam Color", Color) = (0.30,0.20,0.14,1)
        _BeamWidth ("Beam Width (m)", Float) = 0.38
        _TimberBay ("Timber Bay (m)", Float) = 2.6
        _TimberFloor ("Timber Floor (m)", Float) = 3.6
        _GlassColor ("Small Window Color", Color) = (0.20,0.24,0.30,1)
        _DoorColor ("Door Color", Color) = (0.36,0.23,0.15,1)
        [Toggle] _Stone ("Stone Courses (돌 쌓은 벽)", Float) = 0
        _StoneRow ("Stone Row (m)", Float) = 1.2
        _StoneBlock ("Stone Block (m)", Float) = 2.0
        [Toggle] _Cobble ("Cobblestones on top (윗면 돌바닥)", Float) = 0
        _CobbleSize ("Cobble Size (m)", Float) = 1.3
        [Toggle] _Meadow ("Meadow patches on top (윗면 풀밭 얼룩)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            float4 _LineColor, _ShadowColor, _WindowColor, _WindowSize;
            float _LineWidth, _MinLinePixels, _Shade, _Windows, _FloorHeight, _WindowSpacing, _WindowMargin, _ModernWindows;
            float4 _BeamColor, _GlassColor, _DoorColor;
            float _Timber, _BeamWidth, _TimberBay, _TimberFloor, _Stone, _StoneRow, _StoneBlock, _Cobble, _CobbleSize, _Meadow;

            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = Hash(float3(i, 0)), b = Hash(float3(i + float2(1, 0), 0)), c = Hash(float3(i + float2(0, 1), 0)), d = Hash(float3(i + float2(1, 1), 0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 풀밭 얼룩: 큰 얼룩(짙은 풀·옅은 풀) + 작은 얼룩 + 풀포기 점. 반환 xyz = 곱할 색
            float3 MeadowTint(float2 xz)
            {
                float big = VNoise(xz / 70) * 0.65 + VNoise(xz / 19) * 0.35;
                float3 tint = lerp(float3(0.80, 0.86, 0.80), float3(1.10, 1.08, 0.86), big); // 짙은 초록 ↔ 햇볕 든 연두
                float dry = saturate((VNoise(xz / 33 + 17.3) - 0.72) * 4);                     // 군데군데 마른 풀 (누런빛)
                tint = lerp(tint, float3(1.18, 1.05, 0.72), dry * 0.6);
                // 풀포기: 1.4m 칸마다 작은 짙은 점 (멀리선 흐리게)
                float2 cell = floor(xz / 1.4), f = frac(xz / 1.4) - 0.5;
                float2 o = float2(Hash(float3(cell, 3.1)), Hash(float3(cell, 7.7))) - 0.5;
                float tuft = 1 - smoothstep(0.12, 0.2, length(f - o * 0.6));
                float pix = max(fwidth(xz.x), fwidth(xz.y)) / 1.4;
                tint *= 1 - 0.12 * tuft * (1 - saturate((pix - 0.15) / 0.3));
                return tint;
            }


            // 하프팀버(목조 골조) 벽: 층마다 가로보, 일정 간격 기둥, 일부 칸에 대각 가새, 일부 칸에 작은 창, 1층 한 칸에 문.
            // h = 벽 가로 위치(가운데 0), y = 바닥에서 높이, halfW·height = 벽 크기 (m). 반환: xyz = 색, w = 덮임
            float4 Timber(float h, float y, float halfW, float height, float faceId, float3 seed)
            {
                float wallW = halfW * 2;
                float bays = max(1, round(wallW / _TimberBay));
                float bayW = wallW / bays;
                float floors = max(1, round(height / _TimberFloor));
                float floorH = height / floors;
                float2 uv = float2((h + halfW) / bayW, y / floorH);
                float2 cell = floor(uv);
                float2 fm = frac(uv) * float2(bayW, floorH);          // 칸 안 위치 (m)
                float pixM = max(max(fwidth(h), fwidth(y)), 1e-4);    // 한 픽셀이 몇 m인지
                float bw = max(_BeamWidth * 0.5, pixM * 0.6);
                // 기둥·보 (칸 경계)
                float frame = 1 - smoothstep(bw - pixM, bw + pixM, min(min(fm.x, bayW - fm.x), min(fm.y, floorH - fm.y)));
                float rnd = Hash(float3(cell, faceId) + seed), rnd2 = Hash(float3(cell, faceId) + seed + 5.1);
                // 가새: 칸 대각선 (방향은 칸마다 다름)
                float2 a = float2(rnd2 < 0.5 ? 0 : bayW, 0), b = float2(rnd2 < 0.5 ? bayW : 0, floorH);
                float2 ab = b - a;
                float t = saturate(dot(fm - a, ab) / dot(ab, ab));
                float brace = (1 - smoothstep(bw - pixM, bw + pixM, length(fm - (a + ab * t)))) * step(0.62, rnd);
                // 1층의 한 칸은 문, 가새 없는 칸 일부는 작은 창
                float doorBay = floor(Hash(float3(faceId, 7.7, 1.3) + seed) * bays);
                float isDoor = step(cell.y, 0.5) * step(abs(cell.x - doorBay), 0.5);
                float2 wsz = float2(min(1.1, bayW * 0.42), min(1.25, floorH * 0.38)) * 0.5;
                float2 wd = wsz - abs(fm - float2(bayW * 0.5, floorH * 0.56));
                float winM = saturate(wd.x / pixM + 0.5) * saturate(wd.y / pixM + 0.5) * step(rnd, 0.42) * (1 - isDoor);
                float2 dsz = float2(min(1.4, bayW * 0.5), min(2.5, floorH * 0.7)) * 0.5;
                float2 dd = dsz - abs(fm - float2(bayW * 0.5, dsz.y));
                float doorM = saturate(dd.x / pixM + 0.5) * saturate(dd.y / pixM + 0.5) * isDoor;
                float hole = max(winM, doorM) * (1 - frame);
                float3 col = lerp(_BeamColor.rgb, lerp(_GlassColor.rgb, _DoorColor.rgb, isDoor), hole);
                float wgt = max(max(frame, brace * (1 - winM)), hole);
                float far = saturate((pixM / max(_BeamWidth, 1e-3) - 0.6) / 1.2); // 멀리서 보가 1~2픽셀이면 흐리게 (깜빡임 방지)
                return float4(col, wgt * lerp(1, 0.35, far));
            }

            // 돌 쌓은 벽: 가로 줄눈 + 줄마다 엇갈린 세로 줄눈. 반환: 줄눈 덮임
            float StoneLines(float h, float y)
            {
                float v = y / _StoneRow;
                float row = floor(v);
                float u = h / _StoneBlock + frac(row * 0.5);
                float pixM = max(max(fwidth(h), fwidth(y)), 1e-4);
                float lw = max(0.06, pixM * 0.5);
                float du = abs(frac(u + 0.5) - 0.5) * _StoneBlock, dv = abs(frac(v + 0.5) - 0.5) * _StoneRow;
                float m = 1 - smoothstep(lw - pixM, lw + pixM, min(du, dv));
                return m * (1 - saturate((pixM / 0.12 - 0.5) / 1.0));
            }

            // 윗면 자갈 돌바닥 (도로·광장). 반환: 줄눈 덮임
            float Cobble(float2 xz)
            {
                float v = xz.y / _CobbleSize;
                float row = floor(v);
                float len = _CobbleSize * 1.4;
                float u = xz.x / len + frac(row * 0.37);
                float pixM = max(max(fwidth(xz.x), fwidth(xz.y)), 1e-4);
                float lw = max(0.05, pixM * 0.5);
                float du = abs(frac(u + 0.5) - 0.5) * len, dv = abs(frac(v + 0.5) - 0.5) * _CobbleSize;
                float m = 1 - smoothstep(lw - pixM, lw + pixM, min(du, dv));
                return m * (1 - saturate((pixM / 0.1 - 0.5) / 1.0));
            }

            // 옆면 창문: h = 벽 가로 위치(가운데 0), y = 바닥에서 높이, halfW·height = 벽 크기 (모두 m).
            // seed = 건물마다 다른 값(건물 크기): 건물마다 층 높이·창 간격·창 모양이 달라지고, 몇몇 창은 비어 있음.
            // 반환: xyz = 창문 색, w = 창문 덮임 정도 (멀리서 창문이 몇 픽셀 안 되면 흐리게 섞음)
            float4 Window(float h, float y, float halfW, float height, float faceId, float3 seed)
            {
                float r1 = Hash(seed), r2 = Hash(seed + 1.7), r3 = Hash(seed + 3.1), r4 = Hash(seed + 4.9), r5 = Hash(seed + 6.3), r6 = Hash(seed + 8.2);
                float floorH = _FloorHeight * lerp(0.8, 1.25, r1);
                float spacing = _WindowSpacing * lerp(0.75, 1.35, r2);
                float2 size;
                if (r3 < 0.2 * _ModernWindows) size = float2(spacing * 0.9, floorH * 0.42);        // 가로로 긴 띠 창
                else if (r3 < 0.35 * _ModernWindows) size = float2(spacing * 0.36, floorH * 0.7);  // 세로로 길쭉한 창
                else size = float2(min(_WindowSize.x * lerp(0.75, 1.2, r4), spacing * 0.8),
                                   min(_WindowSize.y * lerp(0.8, 1.15, r5), floorH * 0.75));
                float missChance = lerp(0.0, 0.22, r6);                            // 비어 있는(창 없는) 칸 비율

                float u = h / spacing + 0.5, v = y / floorH;
                float2 fw = float2(fwidth(u), fwidth(v));
                float2 cell = float2(frac(u) - 0.5, frac(v));
                // 창문 사각형 (가운데 정렬 가로, 각 층 아래쪽 sill 위)
                float sill = (floorH - size.y) * 0.45 / floorH;
                float2 halfSz = float2(size.x * 0.5 / spacing, size.y * 0.5 / floorH);
                float2 c = float2(cell.x, cell.y - sill - halfSz.y);
                float2 dd = halfSz - abs(c);
                float2 aa = max(fw, 1e-4);
                float m = saturate(dd.x / aa.x + 0.5) * saturate(dd.y / aa.y + 0.5);
                // 벽 가장자리·1층 아래·옥상 근처는 창문 없음
                m *= step(abs(h), halfW - _WindowMargin) * step(floorH * 0.8, y) * step(y, height - _WindowMargin);
                float3 id = float3(floor(u), floor(v), faceId) + seed;
                m *= step(missChance, Hash(id + 2.3));
                // 멀리서(창문 한 칸이 몇 픽셀 안 되면) 깜빡이지 않게 창문 대신 옅은 평균색으로
                float far = saturate((max(fw.x, fw.y) - 0.12) / 0.2);
                float3 glass = _WindowColor.rgb; // 모든 창문 같은 색
                return float4(glass, lerp(m, 0.15, far));
            }
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 p : TEXCOORD0;      // 오브젝트 공간 위치 * 스케일 (m)
                float3 halfSize : TEXCOORD1;
                float3 n : TEXCOORD2;      // 오브젝트 공간 노멀
                float3 wn : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                SHADOW_COORDS(5)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 s = float3(length(unity_ObjectToWorld._m00_m10_m20),
                                  length(unity_ObjectToWorld._m01_m11_m21),
                                  length(unity_ObjectToWorld._m02_m12_m22));
                o.pos = UnityObjectToClipPos(v.vertex);
                o.p = v.vertex.xyz * s;
                o.halfSize = s * 0.5;
                o.n = v.normal;
                o.wn = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 d = i.halfSize - abs(i.p);      // 각 축 방향으로 면 가장자리까지 거리
                float3 an = abs(i.n);
                // 면의 법선 축은 제외하고 나머지 두 축 중 가까운 가장자리
                float e = 1e5;
                if (an.x < 0.5) e = min(e, d.x);
                if (an.y < 0.5) e = min(e, d.y);
                if (an.z < 0.5) e = min(e, d.z);
                float aa = fwidth(e);
                float w = max(_LineWidth, aa * _MinLinePixels);
                float edgeMask = 1 - smoothstep(w - aa, w + aa, e);

                float3 wn = normalize(i.wn);
                float shade = 1 - _Shade * (1 - saturate(0.55 + 0.45 * dot(wn, normalize(float3(0.35, 1, 0.2)))));
                // 그림자: 그림자 맵에 가려진 곳 + 해를 등진 면은 그늘 색으로
                float atten = SHADOW_ATTENUATION(i);
                float facing = saturate(dot(wn, normalize(_WorldSpaceLightPos0.xyz)) * 4);
                float lit = min(atten, lerp(0.35, 1, facing));
                float3 shadowTint = lerp(_ShadowColor.rgb, float3(1, 1, 1), lit);
                float4 fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                // 옆면에만 창문 (fwidth를 쓰므로 분기 없이 계산하고 가중치로 켜고 끔)
                bool xFace = an.x > 0.5;
                float h = xFace ? i.p.z : i.p.x, halfW = xFace ? i.halfSize.z : i.halfSize.x;
                float faceId = xFace ? sign(i.n.x) * 2 : sign(i.n.z) * 3;
                float3 seed = round(i.halfSize * 10) * 0.1; // 건물 크기 = 건물마다 다른 고정값 (부서지며 기울어도 그대로)
                float yb = i.p.y + i.halfSize.y, side = step(an.y, 0.5);
                float4 win = Window(h, yb, halfW, i.halfSize.y * 2, faceId, seed);
                float winW = win.w * step(0.5, _Windows) * side;
                // 건물 색이 어두워지면(부서지는 중) 창문도 같이 어두워지게 건물 색 밝기를 곱함
                float bright = saturate(max(fill.r, max(fill.g, fill.b)) * 1.1);
                // 중세풍 무늬 (재질에서 켤 때만): 돌 줄눈·윗면 자갈 → 창문 → 목조 골조 순서로 덮음
                float stoneM = StoneLines(h, yb) * step(0.5, _Stone) * side;
                float cob = Cobble(i.p.xz) * step(0.5, _Cobble) * (1 - side);
                float3 surface = fill.rgb * (1 - 0.18 * stoneM) * (1 - 0.2 * cob);
                surface *= lerp(float3(1, 1, 1), MeadowTint(i.p.xz), step(0.5, _Meadow) * (1 - side));
                surface = lerp(surface, win.rgb * bright, winW);
                float4 tim = Timber(h, yb, halfW, i.halfSize.y * 2, faceId, seed);
                surface = lerp(surface, tim.rgb * bright, tim.w * step(0.5, _Timber) * side);
                fixed4 col = lerp(float4(surface * shade * shadowTint, 1), _LineColor, edgeMask);
                col.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        // 그림자 맵에 건물을 그리는 패스 (GPU 인스턴싱 지원)
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f { V2F_SHADOW_CASTER; };

            v2f vert (appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag (v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback Off
}
