using UnityEngine;

namespace SVT.Core
{
    /// <summary>
    /// SVT system global settings ScriptableObject.
    /// SVT系统全局配置数据对象。
    /// </summary>
    [CreateAssetMenu(menuName = "SVT/SVTSettings", fileName = "SVTSettings")]
    public class SVTSettings : ScriptableObject
    {
        [Header("Virtual Texture Resolution")]
        [Tooltip("Width of the virtual texture (e.g. 8192 for 8K)")]
        public int virtualTextureWidth = 8192;

        [Tooltip("Height of the virtual texture (e.g. 8192 for 8K)")]
        public int virtualTextureHeight = 8192;

        [Header("Page Settings")]
        [Tooltip("Size of a single texture page in texels")]
        public int pageSize = 256;

        [Tooltip("Page border/padding in texels to avoid sampling artefacts")]
        public int pageBorder = 4;

        [Header("Cache Settings")]
        [Tooltip("Number of columns in the physical texture cache atlas")]
        public int cacheColumns = 16;

        [Tooltip("Number of rows in the physical texture cache atlas")]
        public int cacheRows = 16;

        [Tooltip("Maximum number of pages that may be loaded simultaneously")]
        public int maxLoadedPages = 256;

        [Header("Quadtree Settings")]
        [Tooltip("Maximum depth of the quadtree (0 = only root)")]
        public int maxLodLevels = 8;

        [Tooltip("World-space size represented by the virtual texture")]
        public float worldSize = 1024f;

        [Header("Streaming Settings")]
        [Tooltip("Maximum number of pages loaded per frame")]
        public int maxPageLoadsPerFrame = 4;

        [Tooltip("Maximum number of pages unloaded per frame")]
        public int maxPageUnloadsPerFrame = 8;

        [Tooltip("LOD bias: positive = prefer higher detail")]
        public float lodBias = 0f;

        [Header("Feedback Buffer")]
        [Tooltip("Downscale factor applied to screen resolution for the feedback pass")]
        public int feedbackDownscale = 4;

        [Header("Terrain Integration")]
        [Tooltip("Number of terrain layers supported. Each layer requires a separate blend channel.")]
        public int maxTerrainLayers = 8;

        [Tooltip("Resolution of the per-layer weight texture baked from terrain")]
        public int terrainLayerBakeResolution = 1024;

        [Header("Debug")]
        [Tooltip("Show SVT debug overlay in play mode")]
        public bool showDebugOverlay = false;

        [Tooltip("Color used to highlight loaded pages in the debug view")]
        public Color debugLoadedPageColor = new Color(0f, 1f, 0f, 0.4f);

        [Tooltip("Color used to highlight missing pages in the debug view")]
        public Color debugMissingPageColor = new Color(1f, 0f, 0f, 0.4f);

        // ------------------------------------------------------------------ //
        // Derived helpers
        // ------------------------------------------------------------------ //

        /// <summary>Physical page size without border.</summary>
        public int PageSizeNoBorder => pageSize - pageBorder * 2;

        /// <summary>Total number of virtual pages along one axis at mip 0.</summary>
        public int PagesX => virtualTextureWidth / pageSize;

        /// <summary>Total number of virtual pages along one axis at mip 0.</summary>
        public int PagesY => virtualTextureHeight / pageSize;

        /// <summary>Total capacity of the physical cache (pages).</summary>
        public int CacheCapacity => cacheColumns * cacheRows;
    }
}
