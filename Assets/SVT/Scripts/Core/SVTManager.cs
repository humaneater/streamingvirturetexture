using System.Collections.Generic;
using UnityEngine;

namespace SVT.Core
{
    /// <summary>
    /// Main SVT Manager MonoBehaviour.
    /// Orchestrates the quadtree, page table, cache, texture manager, and feedback buffer.
    /// Attach to a GameObject in the scene and assign a SVTSettings asset.
    ///
    /// SVT主管理器MonoBehaviour。
    /// 协调四叉树、页表、缓存、纹理管理器和反馈缓冲区。
    /// 将此脚本挂载到场景中的GameObject，并赋值SVTSettings资源。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SVTManager : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Inspector
        // ------------------------------------------------------------------ //

        [Header("Settings")]
        [Tooltip("SVT configuration asset")]
        public SVTSettings settings;

        [Tooltip("Camera used for LOD and feedback calculations. Leave null to use Camera.main.")]
        public Camera svtCamera;

        [Tooltip("Terrain origin in world space (lower-left corner of the terrain)")]
        public Vector3 terrainOrigin = Vector3.zero;

        [Header("Shader")]
        [Tooltip("Material that contains the SVT shader (will receive indirection/cache texture references)")]
        public Material svtMaterial;

        // ------------------------------------------------------------------ //
        // Runtime systems
        // ------------------------------------------------------------------ //

        private SVTQuadTree _quadTree;
        private SVTPageTable _pageTable;
        private SVTCache _cache;
        private SVTTextureManager _textureManager;
        private SVTFeedbackBuffer _feedbackBuffer;

        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        private readonly List<SVTNode> _allNodes = new List<SVTNode>();
        private Camera _activeCamera;

        // ------------------------------------------------------------------ //
        // Static singleton (optional helper)
        // ------------------------------------------------------------------ //

        public static SVTManager Instance { get; private set; }

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (settings == null)
            {
                Debug.LogError("[SVT] SVTSettings is not assigned!");
                enabled = false;
                return;
            }

            InitializeSystems();
        }

        private void OnDestroy()
        {
            ShutdownSystems();
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            _activeCamera = svtCamera != null ? svtCamera : Camera.main;
            if (_activeCamera == null)
                Debug.LogWarning("[SVT] No camera found. SVT LOD updates will be skipped.");
        }

        private void Update()
        {
            if (_activeCamera == null)
                _activeCamera = svtCamera != null ? svtCamera : Camera.main;

            if (_activeCamera == null || settings == null) return;

            // 1. Update quadtree LOD
            _quadTree.Update(_activeCamera);

            // 2. Process texture load queue
            _textureManager.ProcessQueue();

            // 3. Poll GPU feedback readback
            _feedbackBuffer.Poll();

            // 4. Upload dirty page table entries
            _pageTable.Upload();

            // 5. Bind textures to SVT material
            BindMaterialTextures();
        }

        // ------------------------------------------------------------------ //
        // Initialization / Shutdown
        // ------------------------------------------------------------------ //

        private void InitializeSystems()
        {
            _quadTree = new SVTQuadTree(settings);
            RegisterQuadTreeEvents();
            _quadTree.Initialize(terrainOrigin);

            _pageTable = new SVTPageTable(settings);
            _cache = new SVTCache(settings);
            _textureManager = new SVTTextureManager(settings, this);
            _textureManager.OnPageLoaded += OnPageLoaded;

            _feedbackBuffer = new SVTFeedbackBuffer(settings);
            _feedbackBuffer.OnFeedbackReady += OnFeedbackReady;

            // Initialize feedback buffer with default resolution; resize in OnPreRender if needed.
            _feedbackBuffer.Initialize(Screen.width, Screen.height);
        }

        private void ShutdownSystems()
        {
            if (_textureManager != null) { _textureManager.Dispose(); _textureManager = null; }
            if (_feedbackBuffer != null) { _feedbackBuffer.Dispose(); _feedbackBuffer = null; }
            if (_pageTable != null) { _pageTable.Dispose(); _pageTable = null; }
            if (_cache != null) { _cache.Dispose(); _cache = null; }
        }

        // ------------------------------------------------------------------ //
        // Quadtree events
        // ------------------------------------------------------------------ //

        private void RegisterQuadTreeEvents()
        {
            _quadTree.OnPageRequested += OnPageRequested;
            _quadTree.OnPageReleased += OnPageReleased;
        }

        private void OnPageRequested(SVTNode node)
        {
            // Build resource paths from page id
            string albedoPath = BuildPagePath(node.PageId, "albedo");
            string normalPath = BuildPagePath(node.PageId, "normal");
            _textureManager.RequestLoad(node, albedoPath, normalPath);
        }

        private void OnPageReleased(SVTNode node)
        {
            _textureManager.CancelRequest(node);
            // Return cache slot
            if (node.CacheSlotX >= 0)
            {
                _cache.FreeSlot(node.CacheSlotX, node.CacheSlotY);
                _pageTable.SetPageUnloaded(node);
                node.CacheSlotX = -1;
                node.CacheSlotY = -1;
                node.State = SVTNode.LoadState.Unloaded;
            }
        }

        // ------------------------------------------------------------------ //
        // Texture load callback
        // ------------------------------------------------------------------ //

        private void OnPageLoaded(SVTNode node, SVTTextureManager.PageTextures textures)
        {
            if (!_cache.TryAllocateSlot(node.PageId, out int slotX, out int slotY))
            {
                Debug.LogWarning($"[SVT] Cache full – could not allocate slot for {node.PageId}");
                return;
            }

            node.CacheSlotX = slotX;
            node.CacheSlotY = slotY;
            node.State = SVTNode.LoadState.Loaded;

            // Blit source texture into atlas slot
            RectInt pixRect = _cache.GetSlotPixelRect(slotX, slotY);

            if (textures.Albedo != null)
                BlitIntoAtlas(textures.Albedo, _cache.AlbedoAtlas, pixRect);

            if (textures.Normal != null)
                BlitIntoAtlas(textures.Normal, _cache.NormalAtlas, pixRect);

            // Update indirection table
            _pageTable.SetPageLoaded(node, slotX, slotY);
        }

        // ------------------------------------------------------------------ //
        // Feedback readback
        // ------------------------------------------------------------------ //

        private void OnFeedbackReady(Color32[] pixels)
        {
            // Decode encoded page ids from feedback pixels
            // Encoding: R = pageX (low byte), G = pageX (high byte), B = pageY, A = mipLevel
            // (This matches the SVTFeedback shader encoding)
            var requestedPages = new HashSet<SVTPageId>();

            foreach (Color32 px in pixels)
            {
                if (px.a == 0) continue; // background / invalid
                int mip = px.a - 1; // mip encoded as 1-based to distinguish from background
                int pageX = px.r;
                int pageY = px.g;
                requestedPages.Add(new SVTPageId(mip, pageX, pageY));
            }

            // For requested pages that are not yet loaded, boost their priority
            _quadTree.CollectAllNodes(_allNodes);
            foreach (SVTNode node in _allNodes)
            {
                if (requestedPages.Contains(node.PageId))
                {
                    node.LastVisibleFrame = Time.frameCount;
                    node.LoadPriority = 1f;
                }
            }
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private static string BuildPagePath(SVTPageId pageId, string type) =>
            $"SVTPages/mip{pageId.mipLevel}/page_{pageId.pageX}_{pageId.pageY}_{type}";

        private static void BlitIntoAtlas(Texture2D src, RenderTexture atlas, RectInt destRect)
        {
            // Temporarily make atlas the active render target and blit
            var prevRT = RenderTexture.active;
            RenderTexture.active = atlas;
            Graphics.DrawTexture(
                new Rect(destRect.x, destRect.y, destRect.width, destRect.height),
                src);
            RenderTexture.active = prevRT;
        }

        private void BindMaterialTextures()
        {
            if (svtMaterial == null) return;
            svtMaterial.SetTexture("_SVT_IndirectionTex", _pageTable.IndirectionTexture);
            svtMaterial.SetTexture("_SVT_AlbedoAtlas", _cache.AlbedoAtlas);
            svtMaterial.SetTexture("_SVT_NormalAtlas", _cache.NormalAtlas);
            svtMaterial.SetVector("_SVT_PageTableSize",
                new Vector4(settings.PagesX, settings.PagesY, 0f, 0f));
            svtMaterial.SetVector("_SVT_CacheSize",
                new Vector4(settings.cacheColumns, settings.cacheRows, 0f, 0f));
            svtMaterial.SetFloat("_SVT_PageSize", settings.pageSize);
            svtMaterial.SetVector("_SVT_WorldRect",
                new Vector4(terrainOrigin.x, terrainOrigin.z, settings.worldSize, settings.worldSize));
        }

        // ------------------------------------------------------------------ //
        // Public helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Force rebuild the quadtree (e.g. after terrain changes).
        /// 强制重建四叉树（例如地形变更后调用）。
        /// </summary>
        public void RebuildQuadTree()
        {
            _quadTree.Initialize(terrainOrigin);
        }

        /// <summary>Expose the page table indirection texture for external use.</summary>
        public RenderTexture IndirectionTexture => _pageTable?.IndirectionTexture;

        /// <summary>Expose the physical albedo cache atlas for external use.</summary>
        public RenderTexture AlbedoAtlas => _cache?.AlbedoAtlas;

        /// <summary>Expose the physical normal cache atlas for external use.</summary>
        public RenderTexture NormalAtlas => _cache?.NormalAtlas;

        /// <summary>Expose the feedback RenderTexture for the feedback pass.</summary>
        public RenderTexture FeedbackRT => _feedbackBuffer?.FeedbackRT;

        /// <summary>Expose the current quadtree leaf nodes for debug/terrain tools.</summary>
        public IReadOnlyList<SVTNode> QuadTreeLeaves => _quadTree?.Leaves;

        /// <summary>The SVT settings in use.</summary>
        public SVTSettings Settings => settings;

        /// <summary>
        /// Trigger an async GPU readback of the feedback buffer.
        /// Called by SVTFeedbackRequestHelper after the feedback pass is rendered.
        /// 触发反馈缓冲区的异步GPU回读，在反馈pass渲染后由SVTFeedbackRequestHelper调用。
        /// </summary>
        public void RequestFeedbackReadback()
        {
            _feedbackBuffer?.RequestReadback();
        }
    }
}
