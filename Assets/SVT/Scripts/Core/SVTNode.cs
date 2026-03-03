using System;
using UnityEngine;

namespace SVT.Core
{
    /// <summary>
    /// Identifies a single virtual texture page by its mip level and 2-D page index.
    /// 通过mip级别和2D页索引来标识一张虚拟纹理页。
    /// </summary>
    [Serializable]
    public struct SVTPageId : IEquatable<SVTPageId>
    {
        public int mipLevel;
        public int pageX;
        public int pageY;

        public SVTPageId(int mipLevel, int pageX, int pageY)
        {
            this.mipLevel = mipLevel;
            this.pageX = pageX;
            this.pageY = pageY;
        }

        public bool Equals(SVTPageId other) =>
            mipLevel == other.mipLevel && pageX == other.pageX && pageY == other.pageY;

        public override bool Equals(object obj) => obj is SVTPageId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(mipLevel, pageX, pageY);

        public override string ToString() => $"Page(mip={mipLevel}, x={pageX}, y={pageY})";
    }

    /// <summary>
    /// Represents a single node in the SVT quadtree.
    /// Each node maps to a virtual texture region and tracks its load state.
    /// 代表SVT四叉树中的单个节点，对应虚拟纹理的一个区域，并追踪其加载状态。
    /// </summary>
    public class SVTNode
    {
        // ------------------------------------------------------------------ //
        // Tree topology
        // ------------------------------------------------------------------ //

        /// <summary>Parent node. Null for the root.</summary>
        public SVTNode Parent { get; private set; }

        /// <summary>Child nodes in order: NW, NE, SW, SE.</summary>
        public SVTNode[] Children { get; private set; }

        // ------------------------------------------------------------------ //
        // Spatial info
        // ------------------------------------------------------------------ //

        /// <summary>LOD / mip level (0 = highest detail).</summary>
        public int LodLevel { get; private set; }

        /// <summary>Normalised bounds of this node in [0,1]² virtual texture space.</summary>
        public Rect NormalizedBounds { get; private set; }

        /// <summary>World-space AABB of this node.</summary>
        public Bounds WorldBounds { get; private set; }

        // ------------------------------------------------------------------ //
        // Page identifier
        // ------------------------------------------------------------------ //

        public SVTPageId PageId { get; private set; }

        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        public enum LoadState { Unloaded, Requested, Loaded, Fallback }
        public LoadState State { get; set; } = LoadState.Unloaded;

        /// <summary>
        /// Frame counter used for LRU eviction. Updated when this page is visible.
        /// </summary>
        public int LastVisibleFrame { get; set; } = -1;

        /// <summary>Priority used to sort load requests (higher = more urgent).</summary>
        public float LoadPriority { get; set; }

        /// <summary>Location of this page inside the physical cache atlas (column, row).</summary>
        public int CacheSlotX { get; set; } = -1;
        public int CacheSlotY { get; set; } = -1;

        public bool IsLeaf => Children == null;
        public bool IsSplit => Children != null;

        // ------------------------------------------------------------------ //
        // Construction
        // ------------------------------------------------------------------ //

        public SVTNode(SVTNode parent, int lodLevel, Rect normalizedBounds, Bounds worldBounds, int pageX, int pageY)
        {
            Parent = parent;
            LodLevel = lodLevel;
            NormalizedBounds = normalizedBounds;
            WorldBounds = worldBounds;
            PageId = new SVTPageId(lodLevel, pageX, pageY);
        }

        // ------------------------------------------------------------------ //
        // Tree manipulation
        // ------------------------------------------------------------------ //

        /// <summary>Split this leaf node into four children.</summary>
        public void Split(float worldSize)
        {
            if (IsSplit) return;

            Children = new SVTNode[4];
            float halfW = NormalizedBounds.width * 0.5f;
            float halfH = NormalizedBounds.height * 0.5f;

            int childLod = LodLevel + 1;
            int baseX = PageId.pageX * 2;
            int baseY = PageId.pageY * 2;

            // NW
            Children[0] = new SVTNode(this, childLod,
                new Rect(NormalizedBounds.x, NormalizedBounds.y + halfH, halfW, halfH),
                CreateChildBounds(0, worldSize), baseX, baseY + 1);
            // NE
            Children[1] = new SVTNode(this, childLod,
                new Rect(NormalizedBounds.x + halfW, NormalizedBounds.y + halfH, halfW, halfH),
                CreateChildBounds(1, worldSize), baseX + 1, baseY + 1);
            // SW
            Children[2] = new SVTNode(this, childLod,
                new Rect(NormalizedBounds.x, NormalizedBounds.y, halfW, halfH),
                CreateChildBounds(2, worldSize), baseX, baseY);
            // SE
            Children[3] = new SVTNode(this, childLod,
                new Rect(NormalizedBounds.x + halfW, NormalizedBounds.y, halfW, halfH),
                CreateChildBounds(3, worldSize), baseX + 1, baseY);
        }

        /// <summary>Collapse children back to leaf.</summary>
        public void Merge()
        {
            if (IsLeaf) return;
            Children = null;
        }

        private Bounds CreateChildBounds(int quadrant, float worldSize)
        {
            float halfSizeX = WorldBounds.size.x * 0.5f;
            float halfSizeZ = WorldBounds.size.z * 0.5f;
            float offsetX = (quadrant == 1 || quadrant == 3) ? halfSizeX : 0f;
            float offsetZ = (quadrant == 0 || quadrant == 1) ? halfSizeZ : 0f;
            Vector3 childMin = WorldBounds.min + new Vector3(offsetX, 0f, offsetZ);
            Vector3 childSize = new Vector3(halfSizeX, WorldBounds.size.y, halfSizeZ);
            return new Bounds(childMin + childSize * 0.5f, childSize);
        }
    }
}
