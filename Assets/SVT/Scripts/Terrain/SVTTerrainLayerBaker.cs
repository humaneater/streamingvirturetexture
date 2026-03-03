using System.Collections.Generic;
using UnityEngine;

namespace SVT.Terrain
{
    /// <summary>
    /// Bakes Unity Terrain layers into a set of weight textures and a merged
    /// "virtual albedo + normal" texture atlas for consumption by the SVT system.
    ///
    /// Supports an unlimited number of terrain layers by merging them
    /// progressively into a single RGBA output where each channel carries a
    /// weighted blend of the underlying terrain layer textures.
    ///
    /// 将Unity地形的图层烘焙成权重纹理集和合并后的虚拟反照率+法线纹理图集，供SVT系统使用。
    /// 支持无限数量的地形图层，通过逐步混合将所有图层合并为单一RGBA输出。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Terrain))]
    public class SVTTerrainLayerBaker : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Inspector
        // ------------------------------------------------------------------ //

        [Header("SVT Reference")]
        public SVTManager svtManager;

        [Header("Bake Settings")]
        [Tooltip("Resolution of the baked merged texture (per SVT page resolution)")]
        public int bakeResolution = 1024;

        [Tooltip("When enabled, the baked textures are regenerated every time this component wakes up.")]
        public bool bakeOnAwake = true;

        // ------------------------------------------------------------------ //
        // Runtime
        // ------------------------------------------------------------------ //

        private UnityEngine.Terrain _terrain;
        private TerrainData _terrainData;

        /// <summary>The merged albedo texture produced by the bake operation.</summary>
        public RenderTexture MergedAlbedo { get; private set; }

        /// <summary>The merged normal texture produced by the bake operation.</summary>
        public RenderTexture MergedNormal { get; private set; }

        // Shader for blending terrain layers
        private static readonly int ShaderPropWeight = Shader.PropertyToID("_Weight");
        private static readonly int ShaderPropLayerTex = Shader.PropertyToID("_LayerTex");
        private static readonly int ShaderPropLayerNormal = Shader.PropertyToID("_LayerNormal");
        private static readonly int ShaderPropTiling = Shader.PropertyToID("_Tiling");

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        private void Awake()
        {
            _terrain = GetComponent<UnityEngine.Terrain>();
            _terrainData = _terrain.terrainData;

            if (bakeOnAwake)
                BakeLayers();
        }

        private void OnDestroy()
        {
            ReleaseBakedTextures();
        }

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Bake all terrain layers into the merged SVT textures.
        /// Safe to call at runtime; re-creates RTs if resolution changed.
        /// 将所有地形图层烘焙为合并后的SVT纹理，运行时安全，分辨率变更时自动重建RT。
        /// </summary>
        public void BakeLayers()
        {
            if (_terrainData == null) return;

            EnsureRenderTextures();

            TerrainLayer[] layers = _terrainData.terrainLayers;
            if (layers == null || layers.Length == 0)
            {
                Debug.LogWarning("[SVT] Terrain has no layers to bake.");
                return;
            }

            // Get alpha maps (splat maps) – shape is [height, width, layerCount]
            float[,,] alphaMaps = _terrainData.GetAlphamaps(0, 0,
                _terrainData.alphamapWidth, _terrainData.alphamapHeight);

            // Clear merged textures
            ClearRT(MergedAlbedo);
            ClearRT(MergedNormal);

            Material blendMat = GetBlendMaterial();

            for (int i = 0; i < layers.Length; i++)
            {
                TerrainLayer layer = layers[i];
                if (layer == null) continue;

                // Build per-layer weight texture from alpha map channel i
                Texture2D weightTex = BuildWeightTexture(alphaMaps, i,
                    _terrainData.alphamapWidth, _terrainData.alphamapHeight);

                // Blend this layer into the merged output
                blendMat.SetTexture(ShaderPropWeight, weightTex);
                blendMat.SetTexture(ShaderPropLayerTex,
                    layer.diffuseTexture != null ? layer.diffuseTexture : Texture2D.whiteTexture);
                blendMat.SetTexture(ShaderPropLayerNormal,
                    layer.normalMapTexture != null ? layer.normalMapTexture : Texture2D.normalTexture);

                Vector2 tileSize = layer.tileSize.magnitude > 0f ? layer.tileSize : Vector2.one * 10f;
                blendMat.SetVector(ShaderPropTiling,
                    new Vector4(_terrainData.size.x / tileSize.x,
                                _terrainData.size.z / tileSize.y, 0f, 0f));

                // Albedo pass
                Graphics.Blit(null, MergedAlbedo, blendMat, 0);
                // Normal pass
                Graphics.Blit(null, MergedNormal, blendMat, 1);

                Object.Destroy(weightTex);
            }

            Object.Destroy(blendMat);
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private void EnsureRenderTextures()
        {
            if (MergedAlbedo != null && MergedAlbedo.width == bakeResolution) return;
            ReleaseBakedTextures();

            MergedAlbedo = new RenderTexture(bakeResolution, bakeResolution, 0, RenderTextureFormat.ARGB32)
            {
                name = "SVT_TerrainMergedAlbedo",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            MergedAlbedo.Create();

            MergedNormal = new RenderTexture(bakeResolution, bakeResolution, 0, RenderTextureFormat.ARGB32)
            {
                name = "SVT_TerrainMergedNormal",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            MergedNormal.Create();
        }

        private void ReleaseBakedTextures()
        {
            if (MergedAlbedo != null) { MergedAlbedo.Release(); Object.Destroy(MergedAlbedo); MergedAlbedo = null; }
            if (MergedNormal != null) { MergedNormal.Release(); Object.Destroy(MergedNormal); MergedNormal = null; }
        }

        private static Texture2D BuildWeightTexture(float[,,] alphaMaps, int layerIndex,
            int alphaMapWidth, int alphaMapHeight)
        {
            var tex = new Texture2D(alphaMapWidth, alphaMapHeight, TextureFormat.R8, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[alphaMapWidth * alphaMapHeight];
            for (int y = 0; y < alphaMapHeight; y++)
            {
                for (int x = 0; x < alphaMapWidth; x++)
                {
                    byte w = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(alphaMaps[y, x, layerIndex]) * 255f);
                    pixels[y * alphaMapWidth + x] = new Color32(w, 0, 0, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }

        private static void ClearRT(RenderTexture rt)
        {
            if (rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = prev;
        }

        private static Material GetBlendMaterial()
        {
            Shader shader = Shader.Find("SVT/TerrainLayerBlend");
            if (shader == null)
            {
                Debug.LogError("[SVT] Could not find shader 'SVT/TerrainLayerBlend'. Using fallback.");
                return new Material(Shader.Find("Hidden/BlitCopy"));
            }
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }
}
