// SVT/SVTTerrain.shader
// Main shader for SVT-driven terrain rendering.
// Reads the indirection texture to look up the physical cache atlas and
// produces albedo + normal output matching standard Unity terrain appearance.
//
// SVT地形主着色器：通过间接纹理查找物理缓存图集，产生与Unity标准地形一致的反照率+法线输出。

Shader "SVT/SVTTerrain"
{
    Properties
    {
        // ----- SVT core textures -----
        _SVT_IndirectionTex  ("SVT Indirection Texture", 2D)     = "black" {}
        _SVT_AlbedoAtlas     ("SVT Albedo Cache Atlas",  2D)     = "white" {}
        _SVT_NormalAtlas     ("SVT Normal Cache Atlas",  2D)     = "bump"  {}

        // ----- SVT parameters -----
        _SVT_PageTableSize   ("Page Table Size (XY=pages)", Vector) = (32, 32, 0, 0)
        _SVT_CacheSize       ("Cache Atlas Size (XY=slots)", Vector) = (16, 16, 0, 0)
        _SVT_PageSize        ("Page Size (texels)", Float) = 256
        _SVT_WorldRect       ("World Rect (XZ origin, XZ size)", Vector) = (0,0,1024,1024)

        // ----- Lighting -----
        _Glossiness          ("Smoothness", Range(0,1)) = 0.2
        _Metallic            ("Metallic",   Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "UnityCG.cginc"
        #include "UnityStandardUtils.cginc"

        // SVT textures
        sampler2D _SVT_IndirectionTex;
        sampler2D _SVT_AlbedoAtlas;
        sampler2D _SVT_NormalAtlas;

        float4 _SVT_PageTableSize;   // xy = pages per axis at mip0
        float4 _SVT_CacheSize;       // xy = cache slots per axis
        float  _SVT_PageSize;        // texels per page
        float4 _SVT_WorldRect;       // xy = world origin (XZ), zw = world size (XZ)

        // Convert world XZ to virtual UV [0,1]
        float2 WorldToVirtualUV(float3 worldPos)
        {
            return (worldPos.xz - _SVT_WorldRect.xy) / _SVT_WorldRect.zw;
        }

        // Sample the SVT: given a virtual UV, look up the indirection table and
        // sample the physical cache atlas. Returns albedo in rgb, mip in a.
        float4 SampleSVTAlbedo(float2 virtualUV)
        {
            // Sample indirection texture (point filter, mip0)
            float4 ind = tex2D(_SVT_IndirectionTex, virtualUV);
            // ind.rg = cache slot (xy) normalised to [0,1]
            // ind.b  = mip level
            // ind.a  = validity (0 = no page loaded)

            if (ind.a < 0.5)
                return float4(0.5, 0.5, 0.5, 1); // fallback grey

            // Cache slot UV offset
            float2 slotUV = ind.rg; // already [0,1] in cache columns/rows space
            float  mip    = ind.b * 255.0;

            // Intra-page UV: fractional part of (virtualUV * pagesPerAxis)
            float2 pagesPerAxis = _SVT_PageTableSize.xy / exp2(mip);
            float2 pageLocalUV  = frac(virtualUV * pagesPerAxis);

            // Map into cache atlas slot
            float2 cacheSlotSize = 1.0 / _SVT_CacheSize.xy;
            float2 atlasUV = (slotUV + pageLocalUV) * cacheSlotSize;

            return tex2D(_SVT_AlbedoAtlas, atlasUV);
        }

        float3 SampleSVTNormal(float2 virtualUV)
        {
            float4 ind = tex2D(_SVT_IndirectionTex, virtualUV);
            if (ind.a < 0.5)
                return float3(0, 0, 1);

            float2 slotUV = ind.rg;
            float  mip    = ind.b * 255.0;
            float2 pagesPerAxis = _SVT_PageTableSize.xy / exp2(mip);
            float2 pageLocalUV  = frac(virtualUV * pagesPerAxis);
            float2 cacheSlotSize = 1.0 / _SVT_CacheSize.xy;
            float2 atlasUV = (slotUV + pageLocalUV) * cacheSlotSize;

            float4 normalSample = tex2D(_SVT_NormalAtlas, atlasUV);
            // DXT5nm / BC5 encoded normal
            float3 normal;
            normal.xy = normalSample.wy * 2.0 - 1.0;
            normal.z  = sqrt(max(0, 1.0 - dot(normal.xy, normal.xy)));
            return normalize(normal);
        }
        ENDHLSL

        // --- Surface Pass ---
        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "AutoLight.cginc"
            #include "Lighting.cginc"

            float _Glossiness;
            float _Metallic;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 normal   : TEXCOORD2;
                float3 tangent  : TEXCOORD3;
                float3 binormal : TEXCOORD4;
                LIGHTING_COORDS(5, 6)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv       = v.uv;
                o.normal   = UnityObjectToWorldNormal(v.normal);
                o.tangent  = UnityObjectToWorldDir(v.tangent.xyz);
                o.binormal = cross(o.normal, o.tangent) * v.tangent.w;
                TRANSFER_VERTEX_TO_FRAGMENT(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 vuv = WorldToVirtualUV(i.worldPos);

                // Sample SVT
                float4 albedo = SampleSVTAlbedo(vuv);
                float3 tsNormal = SampleSVTNormal(vuv);

                // Transform normal from tangent to world space
                float3x3 TBN = float3x3(
                    normalize(i.tangent),
                    normalize(i.binormal),
                    normalize(i.normal));
                float3 worldNormal = normalize(mul(tsNormal, TBN));

                // Simple Lambertian lighting
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float  NdotL    = max(0, dot(worldNormal, lightDir));
                float3 diffuse  = albedo.rgb * _LightColor0.rgb * NdotL;
                float3 ambient  = ShadeSH9(float4(worldNormal, 1.0)) * albedo.rgb;

                return float4(diffuse + ambient, 1.0);
            }
            ENDCG
        }

        // --- Shadow Caster ---
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert_shadow
            #pragma fragment frag_shadow
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct appdata_s { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f_s { V2F_SHADOW_CASTER; };
            v2f_s vert_shadow(appdata_s v)
            {
                v2f_s o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }
            float4 frag_shadow(v2f_s i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
