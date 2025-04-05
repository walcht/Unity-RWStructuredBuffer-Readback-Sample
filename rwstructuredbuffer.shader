Shader "Unlit/NewUnlitShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            // register(uX) with X > 0, is not necessarily needed for OpenGLCore but is crucial for
            // D3D11/D3D12/Vulkan
            uniform RWStructuredBuffer<float> _Buf : register(u1);

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                _Buf[0] = 1.0f; _Buf[3] = 1.0f;
                fixed4 col = fixed4(_Buf[0], _Buf[1], _Buf[2], _Buf[3]);
                return col;
            }
            ENDCG
        }
    }
}
