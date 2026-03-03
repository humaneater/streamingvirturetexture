// SVT/TerrainLayerBlend.shader
// Used by SVTTerrainLayerBaker to blend terrain layers into merged albedo and normal textures.
// Pass 0 = albedo blend, Pass 1 = normal blend.
//
// 用于SVTTerrainLayerBaker将地形图层混合为合并的反照率和法线纹理。
// Pass 0 = 反照率混合，Pass 1 = 法线混合。

Shader "SVT/TerrainLayerBlend"
{
    Properties
    {
        _Weight    ("Layer Weight Texture", 2D)    = "white" {}
        _LayerTex  ("Layer Albedo Texture", 2D)    = "white" {}
        _LayerNormal ("Layer Normal Texture", 2D)  = "bump"  {}
        _Tiling    ("Tiling (XY = tile count)", Vector) = (1,1,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Albedo
        Pass
        {
            Blend One One  // Additive to accumulate weighted layers
            CGPROGRAM
            #pragma vertex vert_blt
            #pragma fragment frag_albedo
            #include "UnityCG.cginc"

            sampler2D _Weight;
            sampler2D _LayerTex;
            float4    _Tiling;

            struct appdata_b { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f_b     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f_b vert_blt(appdata_b v)
            {
                v2f_b o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag_albedo(v2f_b i) : SV_Target
            {
                float weight = tex2D(_Weight, i.uv).r;
                float4 col   = tex2D(_LayerTex, i.uv * _Tiling.xy);
                return col * weight;
            }
            ENDCG
        }

        // Pass 1: Normal
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert_blt
            #pragma fragment frag_normal
            #include "UnityCG.cginc"

            sampler2D _Weight;
            sampler2D _LayerNormal;
            float4    _Tiling;

            struct appdata_b { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f_b     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f_b vert_blt(appdata_b v)
            {
                v2f_b o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag_normal(v2f_b i) : SV_Target
            {
                float weight  = tex2D(_Weight, i.uv).r;
                float4 normal = tex2D(_LayerNormal, i.uv * _Tiling.xy);
                return normal * weight;
            }
            ENDCG
        }
    }

    FallBack Off
}
