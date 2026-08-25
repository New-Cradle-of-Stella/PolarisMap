Shader "Polaris/Map/ImageWithLightNoCameraFade"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _LightTex ("Light Texture", 2D) = "black" {}
        _LightLevel ("LightLevel", Float) = 1.0
        _MoverTex ("Mover Texture", 2D) = "black" {}
        _MoverLightLevel ("MoverLightLevel", Float) = 0.5
        _White ("_WhiteColor", Color) = (1,1,1,1)
        _DarkColor ("_DarkColor", Color) = (1,1,1,1)
        _Map_Scale ("_Map_Scale", Float) = 1.0
        _StencilRef ("Stencil Reference", Float) = 0.0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Compare", Float) = 8.0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilOp ("Stencil Operation", Float) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest Always
            ZWrite Off

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilOp]
            }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _LightTex;
            sampler2D _MoverTex;
            float _LightLevel;
            float _MoverLightLevel;
            fixed4 _White;
            fixed4 _DarkColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityPixelSnap(UnityObjectToClipPos(input.vertex));
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 mainColor = tex2D(_MainTex, input.uv);
                fixed4 light = tex2D(_LightTex, input.uv);
                fixed moverAlpha = tex2D(_MoverTex, input.uv).a;

                fixed darkness = saturate(1.1 - light.r - light.g - light.b);
                darkness *= lerp(_LightLevel, _MoverLightLevel, moverAlpha);

                fixed3 tint = lerp(_White.rgb, _DarkColor.rgb, darkness);
                fixed3 lightScreen = 1.0 - light.rgb * light.a * 0.5;
                fixed3 color = 1.0 - (1.0 - mainColor.rgb * tint) * lightScreen;
                return fixed4(color, mainColor.a);
            }
            ENDCG
        }
    }
}
