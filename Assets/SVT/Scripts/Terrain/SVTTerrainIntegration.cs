using UnityEngine;
using SVT.Core;

namespace SVT.Terrain
{
    /// <summary>
    /// Bridges Unity's Terrain system with the SVT Manager.
    /// Reads terrain properties and drives the SVT page-capture camera to
    /// sample the 8K virtual texture from the terrain surface.
    ///
    /// 桥接Unity地形系统与SVT管理器。
    /// 读取地形属性，驱动SVT页面捕获相机对地形表面进行8K虚拟纹理采样。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Terrain))]
    public class SVTTerrainIntegration : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Inspector
        // ------------------------------------------------------------------ //

        [Header("References")]
        public SVTManager svtManager;
        public SVTTerrainLayerBaker layerBaker;

        [Header("Capture Camera")]
        [Tooltip("Orthographic camera used to render each page region onto the physical cache.")]
        public Camera captureCamera;

        [Tooltip("Material applied to the terrain capture plane during page capture.")]
        public Material captureMaterial;

        [Header("Settings")]
        [Tooltip("World Y offset above terrain surface for the capture camera.")]
        public float captureCameraHeight = 500f;

        [Tooltip("Re-capture all pages when terrain data changes (editor-only).")]
        public bool autoReCaptureOnChange = true;

        // ------------------------------------------------------------------ //
        // Runtime
        // ------------------------------------------------------------------ //

        private UnityEngine.Terrain _terrain;
        private TerrainData _terrainData;
        private bool _initialized;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        private void Awake()
        {
            _terrain = GetComponent<UnityEngine.Terrain>();
            _terrainData = _terrain.terrainData;
        }

        private void Start()
        {
            if (svtManager == null)
                svtManager = SVTManager.Instance;

            if (svtManager == null)
            {
                Debug.LogError("[SVT] SVTTerrainIntegration: no SVTManager found in scene.");
                enabled = false;
                return;
            }

            SetupFromTerrain();
            _initialized = true;
        }

        // ------------------------------------------------------------------ //
        // Terrain → SVT setup
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Sync terrain dimensions into the SVT manager and rebuild the quadtree.
        /// 同步地形尺寸到SVT管理器并重建四叉树。
        /// </summary>
        public void SetupFromTerrain()
        {
            if (_terrainData == null || svtManager == null) return;

            // Mirror terrain origin & world size into SVT settings
            svtManager.terrainOrigin = _terrain.transform.position;
            svtManager.settings.worldSize = _terrainData.size.x; // Assume square terrain

            svtManager.RebuildQuadTree();

            // Bind merged terrain textures to the SVT capture material
            if (layerBaker != null && captureMaterial != null)
            {
                if (layerBaker.MergedAlbedo != null)
                    captureMaterial.SetTexture("_MainTex", layerBaker.MergedAlbedo);
                if (layerBaker.MergedNormal != null)
                    captureMaterial.SetTexture("_BumpMap", layerBaker.MergedNormal);

                captureMaterial.SetVector("_TerrainSize",
                    new Vector4(_terrainData.size.x, _terrainData.size.y, _terrainData.size.z, 0f));
                captureMaterial.SetVector("_TerrainOrigin",
                    new Vector4(_terrain.transform.position.x, _terrain.transform.position.y,
                                _terrain.transform.position.z, 0f));
            }

            SetupCaptureCamera();
        }

        // ------------------------------------------------------------------ //
        // Capture camera
        // ------------------------------------------------------------------ //

        private void SetupCaptureCamera()
        {
            if (captureCamera == null) return;

            captureCamera.orthographic = true;
            captureCamera.orthographicSize = _terrainData.size.z * 0.5f;
            captureCamera.aspect = _terrainData.size.x / _terrainData.size.z;
            captureCamera.nearClipPlane = 1f;
            captureCamera.farClipPlane = _terrainData.size.y + captureCameraHeight + 10f;

            Vector3 terrainCenter = _terrain.transform.position
                + new Vector3(_terrainData.size.x * 0.5f, 0f, _terrainData.size.z * 0.5f);
            captureCamera.transform.position = terrainCenter + Vector3.up * (captureCameraHeight);
            captureCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (svtManager != null)
                captureCamera.targetTexture = svtManager.FeedbackRT;
        }

        // ------------------------------------------------------------------ //
        // Public helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Convert a world XZ position to normalised UV coordinates in the virtual texture.
        /// 将世界XZ坐标转换为虚拟纹理中的归一化UV坐标。
        /// </summary>
        public Vector2 WorldToVirtualUV(Vector3 worldPos)
        {
            if (_terrainData == null) return Vector2.zero;
            Vector3 localPos = worldPos - _terrain.transform.position;
            return new Vector2(
                localPos.x / _terrainData.size.x,
                localPos.z / _terrainData.size.z);
        }

        /// <summary>
        /// Trigger a manual re-bake and re-capture of all terrain pages.
        /// 触发手动重新烘焙和重新捕获所有地形页面。
        /// </summary>
        public void ManualRebake()
        {
            if (layerBaker != null)
                layerBaker.BakeLayers();
            SetupFromTerrain();
        }
    }
}
