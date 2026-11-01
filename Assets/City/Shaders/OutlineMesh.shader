// 상자가 아닌 메시(지붕·뾰족탑·성가퀴·나무 등)에 윤곽선을 그리는 셰이더. OutlineBox와 같은 만화풍 음영·그림자.
// 모서리는 버텍스 컬러에 넣은 무게중심 좌표로 찾음 (면 대각선처럼 숨길 모서리는 그 성분을 1로 둠).
// 버텍스 컬러가 없는(흰색) 메시는 윤곽선 없이 색만 칠함 (실루엣은 화면 윤곽선 효과가 그림).
Shader "City/OutlineMesh"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.95,0.95,0.95,1)
        _LineColor ("Line Color", Color) = (0.12,0.12,0.14,1)
        _LineWidth ("Line Width (m)", Float) = 0.45
        _MinLinePixels ("Min Line Width (px)", Float) = 1.2
        _Shade ("Face Shading", Range(0,1)) = 0.3
        _ShadowColor ("Shadow Color", Color) = (0.62,0.66,0.78,1)
        [Toggle] _Tiles ("Roof Tiles (기와 줄무늬)", Float) = 0
        _TileRow ("Tile Row (m)", Float) = 0.9
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

            float4 _LineColor, _ShadowColor;
            float _LineWidth, _MinLinePixels, _Shade, _Tiles, _TileRow;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float3 wn : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                SHADOW_COORDS(4)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.bary = v.color.rgb;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // 모서리까지 거리(픽셀) → 선 굵기: 가까이선 _LineWidth(m), 멀리선 최소 _MinLinePixels(px)
                float3 fwB = max(fwidth(i.bary), 1e-6);
                float3 distPx = i.bary / fwB;
                float dmin = min(distPx.x, min(distPx.y, distPx.z));
                float pixM = max(length(fwidth(i.wp)) * 0.7071, 1e-5);   // 한 픽셀이 몇 m인지
                float wPx = max(_LineWidth / pixM, _MinLinePixels);
                float edgeMask = 1 - smoothstep(wPx - 0.75, wPx + 0.75, dmin);

                float3 wn = normalize(i.wn);
                float shade = 1 - _Shade * (1 - saturate(0.55 + 0.45 * dot(wn, normalize(float3(0.35, 1, 0.2)))));
                float atten = SHADOW_ATTENUATION(i);
                float facing = saturate(dot(wn, normalize(_WorldSpaceLightPos0.xyz)) * 4);
                float lit = min(atten, lerp(0.35, 1, facing));
                float3 shadowTint = lerp(_ShadowColor.rgb, float3(1, 1, 1), lit);
                float4 fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);

                // 지붕 기와: 비탈면에 높이 방향으로 가로 줄 (멀리서는 흐리게)
                float row = i.wp.y / _TileRow;
                float fr = fwidth(row);
                float dr = abs(frac(row + 0.5) - 0.5);
                float tile = (1 - smoothstep(0.06 - fr, 0.06 + fr, dr)) * (1 - saturate((fr - 0.15) / 0.25));
                float slope = step(0.15, abs(wn.y)) * step(abs(wn.y), 0.97);   // 비탈진 면에만
                float3 surface = fill.rgb * (1 - 0.22 * tile * slope * step(0.5, _Tiles));

                fixed4 col = lerp(float4(surface * shade * shadowTint, 1), _LineColor, edgeMask);
                col.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        // 그림자 맵 (GPU 인스턴싱 지원)
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
