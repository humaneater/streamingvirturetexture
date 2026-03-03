// SVT/SVTFeedback.shader
// Feedback pass shader.
// Renders each fragment encoding its virtual page address into RGBA:
//   R = pageX (0-255)
//   G = pageY (0-255)
//   B = unused
//   A = mipLevel + 1  (0 means "background / no SVT surface")
//
// SVT反馈pass着色器：将每个片元的虚拟页面地址编码到RGBA输出。

Shader "SVT/SVTFeedback"
{
    Properties
    {
        _SVT_IndirectionTex ("SVT Indirection Texture", 2D) = "black" {}
        _SVT_WorldRect      ("World Rect (XZ origin, XZ size)", Vector) = (0,0,1024,1024)
        _SVT_PageTableSize  ("Page Table Size (XY=pages at mip0)", Vector) = (32,32,0,0)
        _SVT_MaxMip         ("Max Mip Level", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SVT_IndirectionTex;
            float4 _SVT_WorldRect;
            float4 _SVT_PageTableSize;
            float  _SVT_MaxMip;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float2 uv       : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv       = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Compute virtual UV
                float2 virtualUV = (i.worldPos.xz - _SVT_WorldRect.xy) / _SVT_WorldRect.zw;

                // Clamp to [0,1]; out-of-range pixels are background
                if (any(virtualUV < 0) || any(virtualUV > 1))
                    return float4(0,0,0,0);

                // Estimate required mip level from screen-space derivatives
                float2 dx = ddx(virtualUV) * _SVT_PageTableSize.x;
                float2 dy = ddy(virtualUV) * _SVT_PageTableSize.y;
                float  mipF = 0.5 * log2(max(dot(dx, dx), dot(dy, dy)));
                int    mip  = clamp((int)mipF, 0, (int)_SVT_MaxMip);

                // Page index at this mip
                float2 pagesAtMip = _SVT_PageTableSize.xy / exp2(mip);
                int2   pageIdx    = (int2)(virtualUV * pagesAtMip);

                // Encode into RGBA8
                // R = pageX (0-255), G = pageY (0-255), B = 0, A = mip+1 (1-based so 0 = background)
                float r = (float)pageIdx.x / 255.0;
                float g = (float)pageIdx.y / 255.0;
                float a = ((float)(mip + 1)) / 255.0;

                return float4(r, g, 0, a);
            }
            ENDCG
        }
    }

    FallBack Off
}
