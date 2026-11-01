// 경험치 구슬용: 가산 블렌드 빛 + 깊이 무시 (나무·건물에 가려도 거인에게 빨려 드는 게 보임)
Shader "Military/XpGlow"
{
    Properties { _MainTex ("Glow", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha One
        ZWrite Off ZTest Always Cull Off Lighting Off Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _MainTex); o.color = v.color; return o; }
            fixed4 frag(v2f i) : SV_Target { fixed4 t = tex2D(_MainTex, i.uv); return fixed4(i.color.rgb * 1.6, i.color.a * t.a); }
            ENDCG
        }
    }
}
