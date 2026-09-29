// 박스(큐브) 메시의 모서리에 일정 두께의 윤곽선을 그리는 셰이더.
// 기본 Cube 메시 + 오브젝트 스케일만으로 동작 (모든 건물이 메시/머티리얼 공유 → GPU 인스턴싱)
Shader "City/OutlineBox"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.95,0.95,0.95,1)
        _LineColor ("Line Color", Color) = (0.12,0.12,0.14,1)
        _LineWidth ("Line Width (m)", Float) = 0.6
        _MinLinePixels ("Min Line Width (px)", Float) = 1.2
        _Shade ("Face Shading", Range(0,1)) = 0.25
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
            #include "UnityCG.cginc"

            float4 _LineColor;
            float _LineWidth, _MinLinePixels, _Shade;
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
                float4 fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                fixed4 col = lerp(fill * shade, _LineColor, edgeMask);
                col.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback "VertexLit"
}
