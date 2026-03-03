using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVT.Core
{
    /// <summary>
    /// Manages the indirection (page) table that maps virtual pages to physical cache slots.
    /// Also maintains the GPU-side RenderTexture used as the indirection texture.
    /// 
    /// 管理将虚拟页面映射到物理缓存槽的间接（页面）表，并维护GPU侧的间接纹理（RenderTexture）。
    /// </summary>
    public class SVTPageTable : IDisposable
    {
        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        private readonly SVTSettings _settings;

        /// <summary>
        /// Indirection texture: RGBA32 or RGBAFloat.
        /// RG = physical cache atlas UV, B = mip level, A = validity flag.
        /// 间接纹理：RG分量存储物理缓存图集UV，B分量存储mip级别，A分量存储有效标志。
        /// </summary>
        private RenderTexture _indirectionTexture;

        /// <summary>CPU-side mirror used to build diffs before uploading.</summary>
        private Color32[] _cpuTable;

        private bool _dirty;

        // One entry per mip level, indexed by [mip][pageY * pagesX + pageX]
        private readonly List<Color32[]> _mipTables = new List<Color32[]>();

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        public RenderTexture IndirectionTexture => _indirectionTexture;

        public SVTPageTable(SVTSettings settings)
        {
            _settings = settings;
            Initialize();
        }

        private void Initialize()
        {
            // Indirection texture resolution = virtual pages per axis at mip 0
            int w = _settings.PagesX;
            int h = _settings.PagesY;

            _indirectionTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "SVT_IndirectionTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = true,
                autoGenerateMips = false,
                anisoLevel = 0
            };
            _indirectionTexture.Create();

            _cpuTable = new Color32[w * h];
            for (int i = 0; i < _cpuTable.Length; i++)
                _cpuTable[i] = new Color32(0, 0, 0, 0); // invalid

            _mipTables.Clear();
            int mipW = w, mipH = h;
            for (int mip = 0; mip <= _settings.maxLodLevels; mip++)
            {
                _mipTables.Add(new Color32[mipW * mipH]);
                mipW = Mathf.Max(1, mipW / 2);
                mipH = Mathf.Max(1, mipH / 2);
            }
        }

        /// <summary>
        /// Register that <paramref name="node"/> has been loaded into the cache slot
        /// (<paramref name="slotX"/>, <paramref name="slotY"/>).
        /// 注册节点已加载到缓存槽。
        /// </summary>
        public void SetPageLoaded(SVTNode node, int slotX, int slotY)
        {
            WriteEntry(node.PageId, slotX, slotY, valid: true);
        }

        /// <summary>
        /// Invalidate the entry for <paramref name="node"/>.
        /// 使节点的间接表项失效。
        /// </summary>
        public void SetPageUnloaded(SVTNode node)
        {
            WriteEntry(node.PageId, 0, 0, valid: false);
        }

        private void WriteEntry(SVTPageId pageId, int slotX, int slotY, bool valid)
        {
            int mip = pageId.mipLevel;
            if (mip >= _mipTables.Count) return;

            int pagesX = Mathf.Max(1, _settings.PagesX >> mip);
            int idx = pageId.pageY * pagesX + pageId.pageX;
            if (idx < 0 || idx >= _mipTables[mip].Length) return;

            byte cacheU = (byte)Mathf.Clamp(slotX, 0, 255);
            byte cacheV = (byte)Mathf.Clamp(slotY, 0, 255);
            byte mipByte = (byte)Mathf.Clamp(mip, 0, 255);
            byte validByte = valid ? (byte)255 : (byte)0;

            _mipTables[mip][idx] = new Color32(cacheU, cacheV, mipByte, validByte);
            _dirty = true;
        }

        /// <summary>
        /// Upload dirty mip tables to the GPU indirection texture.
        /// Must be called once per frame after all page state changes.
        /// 将脏的mip表数据上传到GPU间接纹理，每帧在所有页面状态变更后调用一次。
        /// </summary>
        public void Upload()
        {
            if (!_dirty) return;
            _dirty = false;

            int mipW = _settings.PagesX;
            int mipH = _settings.PagesY;

            for (int mip = 0; mip < _mipTables.Count; mip++)
            {
                Texture2D tmp = new Texture2D(mipW, mipH, TextureFormat.RGBA32, false);
                tmp.SetPixels32(_mipTables[mip]);
                tmp.Apply(false);
                Graphics.CopyTexture(tmp, 0, 0, _indirectionTexture, 0, mip);
                UnityEngine.Object.Destroy(tmp);

                mipW = Mathf.Max(1, mipW / 2);
                mipH = Mathf.Max(1, mipH / 2);
            }
        }

        public void Dispose()
        {
            if (_indirectionTexture != null)
            {
                _indirectionTexture.Release();
                UnityEngine.Object.Destroy(_indirectionTexture);
                _indirectionTexture = null;
            }
        }
    }
}
