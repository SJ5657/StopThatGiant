// 중세 맵 마을 밖 언덕 지형: 풀밭 얼룩(OutlineBox의 풀밭과 같은 무늬) + 가파른 비탈은 흙·바위색 + 성문 길(흙길)을 바닥에 그림.
// 만화풍 음영·그림자는 OutlineBox와 같음. 길 위치는 WildTerrain이 재질 값으로 넘겨줌.
Shader "City/Terrain"
{
    Properties
    {
        _Color ("Grass Color", Color) = (0.50,0.67,0.38,1)
        _RockColor ("Steep Slope Color", Color) = (0.55,0.50,0.42,1)
        _RoadColor ("Road Color", Color) = (0.63,0.51,0.37,1)
        _Shade ("Face Shading", Range(0,1)) = 0.35
        _ShadowColor ("Shadow Color", Color) = (0.62,0.66,0.78,1)
        _Road ("Road (x = 남북 길 X, y = 동서 길 Z, z = 성벽 반폭 X, w = 성벽 반폭 Z)", Vector) = (0,0,1450,1450)
        _RoadHalfWidth ("Road Half Width (m)", Float) = 7.5
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
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            float4 _Color, _RockColor, _RoadColor, _ShadowColor, _Road;
            float _Shade, _RoadHalfWidth;

            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = Hash(float3(i, 0)), b = Hash(float3(i + float2(1, 0), 0)), c = Hash(float3(i + float2(0, 1), 0)), d = Hash(float3(i + float2(1, 1), 0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float3 MeadowTint(float2 xz)
            {
                float big = VNoise(xz / 70) * 0.65 + VNoise(xz / 19) * 0.35;
                float3 tint = lerp(float3(0.80, 0.86, 0.80), float3(1.10, 1.08, 0.86), big);
                float dry = saturate((VNoise(xz / 33 + 17.3) - 0.72) * 4);
                tint = lerp(tint, float3(1.18, 1.05, 0.72), dry * 0.6);
                float2 cell = floor(xz / 1.4), f = frac(xz / 1.4) - 0.5;
                float2 o = float2(Hash(float3(cell, 3.1)), Hash(float3(cell, 7.7))) - 0.5;
                float tuft = 1 - smoothstep(0.12, 0.2, length(f - o * 0.6));
                float pix = max(fwidth(xz.x), fwidth(xz.y)) / 1.4;
                tint *= 1 - 0.12 * tuft * (1 - saturate((pix - 0.15) / 0.3));
                return tint;
            }

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wp : TEXCOORD0;
                float3 wn : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                SHADOW_COORDS(3)
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 wn = normalize(i.wn);
                float2 xz = i.wp.xz;
                float3 col = _Color.rgb * MeadowTint(xz);
                // 가파른 비탈: 흙·바위색
                float steep = saturate((0.93 - wn.y) / 0.12);
                col = lerp(col, _RockColor.rgb * (0.9 + 0.2 * VNoise(xz / 6)), steep);
                // 성문 길 (마을 밖 부분)
                float d = 1e5;
                if (abs(xz.y) > _Road.w - 10) d = min(d, abs(xz.x - _Road.x));
                if (abs(xz.x) > _Road.z - 10) d = min(d, abs(xz.y - _Road.y));
                float aa = fwidth(d) + 0.3;
                float road = 1 - smoothstep(_RoadHalfWidth - aa, _RoadHalfWidth + aa, d);
                col = lerp(col, _RoadColor.rgb * (0.95 + 0.1 * VNoise(xz / 3)), road);

                float shade = 1 - _Shade * (1 - saturate(0.55 + 0.45 * dot(wn, normalize(float3(0.35, 1, 0.2)))));
                float atten = SHADOW_ATTENUATION(i);
                float facing = saturate(dot(wn, normalize(_WorldSpaceLightPos0.xyz)) * 4);
                float lit = min(atten, lerp(0.35, 1, facing));
                float3 shadowTint = lerp(_ShadowColor.rgb, float3(1, 1, 1), lit);
                fixed4 c = fixed4(col * shade * shadowTint, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct v2f { V2F_SHADOW_CASTER; };
            v2f vert (appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) return o; }
            float4 frag (v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback Off
}
