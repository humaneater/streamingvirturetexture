// SVT/SVTDebugOverlay.shader
// Visualises the SVT page state: draws coloured rectangles on top of the scene
// to show loaded (green), requested (yellow) and missing (red) pages.
//
// SVT调试叠加层着色器：在场景上方绘制彩色矩形，显示已加载（绿色）、请求中（黄色）和缺失（红色）的页面。

Shader "SVT/SVTDebugOverlay"
{
    Properties
    {
        _Color ("Debug Color", Color) = (0,1,0,0.4)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay+100" }
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos    : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }

    FallBack Off
}
