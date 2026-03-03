using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SVT.Core
{
    /// <summary>
    /// Handles asynchronous loading and unloading of SVT page textures.
    /// Textures are loaded via Resources.LoadAsync or Addressables and automatically
    /// unloaded when no longer referenced.
    /// 
    /// 负责SVT页面纹理的异步加载和卸载。
    /// 纹理通过Resources.LoadAsync（或Addressables）异步加载，并在不再引用时自动卸载。
    /// </summary>
    public class SVTTextureManager : IDisposable
    {
        // ------------------------------------------------------------------ //
        // Inner types
        // ------------------------------------------------------------------ //

        public struct PageTextures
        {
            public Texture2D Albedo;
            public Texture2D Normal;
        }

        public class LoadRequest
        {
            public SVTNode Node;
            public string AlbedoPath;
            public string NormalPath;
            public bool Cancelled;
        }

        // ------------------------------------------------------------------ //
        // Events
        // ------------------------------------------------------------------ //

        /// <summary>Fired when a page's textures have been loaded and are ready to upload.</summary>
        public event Action<SVTNode, PageTextures> OnPageLoaded;

        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        private readonly SVTSettings _settings;
        private readonly MonoBehaviour _coroutineHost;

        private readonly Queue<LoadRequest> _pendingRequests = new Queue<LoadRequest>();
        private readonly Dictionary<SVTPageId, LoadRequest> _activeRequests = new Dictionary<SVTPageId, LoadRequest>();
        private readonly Dictionary<SVTPageId, PageTextures> _loadedTextures = new Dictionary<SVTPageId, PageTextures>();

        private int _loadsInFlight;

        // ------------------------------------------------------------------ //
        // Construction
        // ------------------------------------------------------------------ //

        public SVTTextureManager(SVTSettings settings, MonoBehaviour coroutineHost)
        {
            _settings = settings;
            _coroutineHost = coroutineHost;
        }

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Enqueue a request to load textures for <paramref name="node"/>.
        /// 将节点纹理加载请求加入队列。
        /// </summary>
        public void RequestLoad(SVTNode node, string albedoPath, string normalPath)
        {
            if (_activeRequests.ContainsKey(node.PageId)) return;
            if (_loadedTextures.ContainsKey(node.PageId))
            {
                OnPageLoaded?.Invoke(node, _loadedTextures[node.PageId]);
                return;
            }

            var req = new LoadRequest
            {
                Node = node,
                AlbedoPath = albedoPath,
                NormalPath = normalPath
            };
            _pendingRequests.Enqueue(req);
            _activeRequests[node.PageId] = req;
        }

        /// <summary>
        /// Cancel the pending/active load request for <paramref name="node"/>.
        /// 取消节点的挂起/活动加载请求。
        /// </summary>
        public void CancelRequest(SVTNode node)
        {
            if (_activeRequests.TryGetValue(node.PageId, out var req))
            {
                req.Cancelled = true;
                _activeRequests.Remove(node.PageId);
            }
        }

        /// <summary>
        /// Unload textures for the given node and release GPU memory.
        /// 卸载给定节点的纹理并释放GPU内存。
        /// </summary>
        public void Unload(SVTNode node)
        {
            CancelRequest(node);

            if (_loadedTextures.TryGetValue(node.PageId, out var textures))
            {
                if (textures.Albedo != null) Resources.UnloadAsset(textures.Albedo);
                if (textures.Normal != null) Resources.UnloadAsset(textures.Normal);
                _loadedTextures.Remove(node.PageId);
            }
        }

        /// <summary>
        /// Process pending load requests up to the per-frame budget.
        /// Call once per frame from the main thread.
        /// 处理挂起的加载请求（每帧预算内），从主线程每帧调用一次。
        /// </summary>
        public void ProcessQueue()
        {
            int budget = _settings.maxPageLoadsPerFrame - _loadsInFlight;
            while (_pendingRequests.Count > 0 && budget > 0)
            {
                var req = _pendingRequests.Dequeue();
                if (req.Cancelled) continue;
                _coroutineHost.StartCoroutine(LoadPageCoroutine(req));
                _loadsInFlight++;
                budget--;
            }
        }

        // ------------------------------------------------------------------ //
        // Coroutine loader
        // ------------------------------------------------------------------ //

        private IEnumerator LoadPageCoroutine(LoadRequest req)
        {
            // Load albedo
            Texture2D albedo = null;
            if (!string.IsNullOrEmpty(req.AlbedoPath))
            {
                var albedoOp = Resources.LoadAsync<Texture2D>(req.AlbedoPath);
                yield return albedoOp;
                if (!req.Cancelled)
                    albedo = albedoOp.asset as Texture2D;
            }

            // Load normal
            Texture2D normal = null;
            if (!string.IsNullOrEmpty(req.NormalPath))
            {
                var normalOp = Resources.LoadAsync<Texture2D>(req.NormalPath);
                yield return normalOp;
                if (!req.Cancelled)
                    normal = normalOp.asset as Texture2D;
            }

            _loadsInFlight--;

            if (req.Cancelled) yield break;

            _activeRequests.Remove(req.Node.PageId);

            var textures = new PageTextures { Albedo = albedo, Normal = normal };
            _loadedTextures[req.Node.PageId] = textures;

            OnPageLoaded?.Invoke(req.Node, textures);
        }

        // ------------------------------------------------------------------ //
        // IDisposable
        // ------------------------------------------------------------------ //

        public void Dispose()
        {
            foreach (var kv in _loadedTextures)
            {
                if (kv.Value.Albedo != null) Resources.UnloadAsset(kv.Value.Albedo);
                if (kv.Value.Normal != null) Resources.UnloadAsset(kv.Value.Normal);
            }
            _loadedTextures.Clear();
            _activeRequests.Clear();
            _pendingRequests.Clear();
        }
    }
}
