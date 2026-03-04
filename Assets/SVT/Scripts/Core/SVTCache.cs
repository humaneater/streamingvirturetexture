using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVT.Core
{
    /// <summary>
    /// Physical texture cache (atlas) for SVT pages.
    /// Manages allocation/eviction of cache slots and the underlying RenderTexture array.
    /// 
    /// SVT页面的物理纹理缓存（图集）。
    /// 管理缓存槽的分配/淘汰以及底层RenderTexture数组。
    /// </summary>
    public class SVTCache : IDisposable
    {
        // ------------------------------------------------------------------ //
        // Inner types
        // ------------------------------------------------------------------ //

        public struct CacheSlot
        {
            public int X;
            public int Y;
            public SVTPageId PageId;
            public bool IsOccupied;
        }

        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        private readonly SVTSettings _settings;

        /// <summary>Albedo physical cache atlas.</summary>
        private RenderTexture _albedoAtlas;

        /// <summary>Normal physical cache atlas.</summary>
        private RenderTexture _normalAtlas;

        private CacheSlot[,] _slots;

        /// <summary>Free slots ordered for fast allocation.</summary>
        private readonly Queue<(int x, int y)> _freeSlots = new Queue<(int, int)>();

        /// <summary>LRU list: front = oldest (evict first).</summary>
        private readonly LinkedList<(int x, int y)> _lruList = new LinkedList<(int, int)>();
        private readonly Dictionary<(int, int), LinkedListNode<(int, int)>> _lruMap
            = new Dictionary<(int, int), LinkedListNode<(int, int)>>();

        // ------------------------------------------------------------------ //
        // Public Properties
        // ------------------------------------------------------------------ //

        public RenderTexture AlbedoAtlas => _albedoAtlas;
        public RenderTexture NormalAtlas => _normalAtlas;

        public int PhysicalWidth => _settings.cacheColumns * _settings.pageSize;
        public int PhysicalHeight => _settings.cacheRows * _settings.pageSize;

        // ------------------------------------------------------------------ //
        // Construction / Initialization
        // ------------------------------------------------------------------ //

        public SVTCache(SVTSettings settings)
        {
            _settings = settings;
            Initialize();
        }

        private void Initialize()
        {
            int w = PhysicalWidth;
            int h = PhysicalHeight;

            _albedoAtlas = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "SVT_AlbedoCache",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                anisoLevel = 1
            };
            _albedoAtlas.Create();

            _normalAtlas = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "SVT_NormalCache",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                anisoLevel = 1
            };
            _normalAtlas.Create();

            _slots = new CacheSlot[_settings.cacheColumns, _settings.cacheRows];
            _freeSlots.Clear();
            _lruList.Clear();
            _lruMap.Clear();

            for (int y = 0; y < _settings.cacheRows; y++)
            {
                for (int x = 0; x < _settings.cacheColumns; x++)
                {
                    _slots[x, y] = new CacheSlot { X = x, Y = y };
                    _freeSlots.Enqueue((x, y));
                }
            }
        }

        // ------------------------------------------------------------------ //
        // Slot allocation
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Allocate a free cache slot.
        /// If no free slot exists, evict the least recently used occupied slot.
        /// Returns false if no slot could be allocated.
        /// 分配一个空闲缓存槽，如果没有空闲槽则淘汰最近最少使用的槽。
        /// </summary>
        public bool TryAllocateSlot(SVTPageId pageId, out int slotX, out int slotY)
        {
            if (_freeSlots.Count > 0)
            {
                var (x, y) = _freeSlots.Dequeue();
                AssignSlot(x, y, pageId);
                slotX = x;
                slotY = y;
                return true;
            }

            // Evict LRU
            if (_lruList.Count > 0)
            {
                var (x, y) = _lruList.First.Value;
                _lruList.RemoveFirst();
                _lruMap.Remove((x, y));
                _slots[x, y].IsOccupied = false;
                AssignSlot(x, y, pageId);
                slotX = x;
                slotY = y;
                return true;
            }

            slotX = -1;
            slotY = -1;
            return false;
        }

        private void AssignSlot(int x, int y, SVTPageId pageId)
        {
            _slots[x, y].PageId = pageId;
            _slots[x, y].IsOccupied = true;
            TouchSlot(x, y);
        }

        /// <summary>
        /// Mark a slot as recently used (moves it to the back of the LRU list).
        /// 将槽标记为最近使用（移到LRU列表末尾）。
        /// </summary>
        public void TouchSlot(int x, int y)
        {
            if (_lruMap.TryGetValue((x, y), out var node))
            {
                _lruList.Remove(node);
                _lruMap.Remove((x, y));
            }
            var newNode = _lruList.AddLast((x, y));
            _lruMap[(x, y)] = newNode;
        }

        /// <summary>
        /// Free a slot back to the pool.
        /// 将槽释放回空闲池。
        /// </summary>
        public void FreeSlot(int x, int y)
        {
            if (x < 0 || y < 0) return;
            if (_lruMap.TryGetValue((x, y), out var node))
            {
                _lruList.Remove(node);
                _lruMap.Remove((x, y));
            }
            _slots[x, y].IsOccupied = false;
            _slots[x, y].PageId = default;
            _freeSlots.Enqueue((x, y));
        }

        /// <summary>
        /// Compute the pixel rect (in atlas space) for a given cache slot.
        /// 计算给定缓存槽在图集空间中的像素矩形。
        /// </summary>
        public RectInt GetSlotPixelRect(int slotX, int slotY)
        {
            int border = _settings.pageBorder;
            int fullPage = _settings.pageSize;
            return new RectInt(
                slotX * fullPage + border,
                slotY * fullPage + border,
                fullPage - border * 2,
                fullPage - border * 2);
        }

        /// <summary>
        /// Compute the atlas UV offset and scale for a given cache slot.
        /// Returns (offsetX, offsetY, scaleX, scaleY).
        /// 计算给定缓存槽在图集UV空间中的偏移和缩放。
        /// </summary>
        public Vector4 GetSlotUVTransform(int slotX, int slotY)
        {
            float atlasW = PhysicalWidth;
            float atlasH = PhysicalHeight;
            int border = _settings.pageBorder;
            int fullPage = _settings.pageSize;
            int inner = fullPage - border * 2;

            float ox = (slotX * fullPage + border) / atlasW;
            float oy = (slotY * fullPage + border) / atlasH;
            float sx = inner / atlasW;
            float sy = inner / atlasH;
            return new Vector4(ox, oy, sx, sy);
        }

        public void Dispose()
        {
            if (_albedoAtlas != null)
            {
                _albedoAtlas.Release();
                SafeDestroy(_albedoAtlas);
                _albedoAtlas = null;
            }
            if (_normalAtlas != null)
            {
                _normalAtlas.Release();
                SafeDestroy(_normalAtlas);
                _normalAtlas = null;
            }
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
