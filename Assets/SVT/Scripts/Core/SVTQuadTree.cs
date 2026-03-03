using System;
using System.Collections.Generic;
using UnityEngine;

namespace SVT.Core
{
    /// <summary>
    /// Quadtree that drives LOD-based virtual texture page selection.
    /// Each frame it is updated against a camera frustum to split/merge nodes
    /// and produce lists of pages that need to be loaded or unloaded.
    /// 
    /// 基于四叉树的SVT LOD选择系统。
    /// 每帧根据相机视锥体来分裂/合并节点，并生成需要加载或卸载的页面列表。
    /// </summary>
    public class SVTQuadTree
    {
        // ------------------------------------------------------------------ //
        // Events
        // ------------------------------------------------------------------ //

        /// <summary>Fired when a leaf node should load its page.</summary>
        public event Action<SVTNode> OnPageRequested;

        /// <summary>Fired when a page is no longer needed.</summary>
        public event Action<SVTNode> OnPageReleased;

        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        private readonly SVTSettings _settings;
        private SVTNode _root;

        /// <summary>All current leaf nodes (visible resolution).</summary>
        private readonly List<SVTNode> _leaves = new List<SVTNode>();

        /// <summary>Nodes that were split this frame.</summary>
        private readonly List<SVTNode> _splitThisFrame = new List<SVTNode>();

        /// <summary>Nodes that were merged this frame.</summary>
        private readonly List<SVTNode> _mergedThisFrame = new List<SVTNode>();

        // ------------------------------------------------------------------ //
        // Construction
        // ------------------------------------------------------------------ //

        public SVTQuadTree(SVTSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Initialise / rebuild the tree from scratch.
        /// Call when the terrain or settings change.
        /// 初始化或重建整个四叉树。当地形或配置改变时调用。
        /// </summary>
        public void Initialize(Vector3 terrainOrigin)
        {
            Vector3 size = new Vector3(_settings.worldSize, 500f, _settings.worldSize);
            Bounds worldBounds = new Bounds(terrainOrigin + size * 0.5f, size);
            _root = new SVTNode(null, 0, new Rect(0f, 0f, 1f, 1f), worldBounds, 0, 0);
            _leaves.Clear();
            _leaves.Add(_root);
        }

        // ------------------------------------------------------------------ //
        // Per-frame update
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Update the quadtree for the given camera.
        /// Splits nodes that need higher detail and merges those that don't.
        /// 每帧根据相机参数更新四叉树，分裂需要更高细节的节点，合并不需要的节点。
        /// </summary>
        public void Update(Camera camera)
        {
            if (_root == null) return;

            _splitThisFrame.Clear();
            _mergedThisFrame.Clear();

            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
            UpdateNode(_root, camera, frustumPlanes);
        }

        private void UpdateNode(SVTNode node, Camera camera, Plane[] frustumPlanes)
        {
            // Frustum cull
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, node.WorldBounds))
            {
                // Out of frustum – if this is a leaf, request its page anyway as a fallback
                if (node.IsLeaf)
                    RequestPageIfNeeded(node);
                return;
            }

            float desiredLod = ComputeDesiredLod(node, camera);

            if (node.IsLeaf)
            {
                if (ShouldSplit(node, desiredLod))
                {
                    SplitNode(node);
                }
                else
                {
                    RequestPageIfNeeded(node);
                    node.LastVisibleFrame = Time.frameCount;
                }
            }
            else
            {
                if (ShouldMerge(node, desiredLod))
                {
                    MergeNode(node);
                    RequestPageIfNeeded(node);
                    node.LastVisibleFrame = Time.frameCount;
                }
                else
                {
                    foreach (SVTNode child in node.Children)
                        UpdateNode(child, camera, frustumPlanes);
                }
            }
        }

        // ------------------------------------------------------------------ //
        // LOD heuristic
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns the desired LOD level for this node given camera position.
        /// 0 = highest detail (finest mip).
        /// 根据相机位置计算所需LOD级别，0为最高细节。
        /// </summary>
        private float ComputeDesiredLod(SVTNode node, Camera camera)
        {
            float dist = Vector3.Distance(camera.transform.position, node.WorldBounds.center);
            float nodeWorldSize = node.WorldBounds.size.x;
            // Basic screen-space metric: how many pixels does this node cover?
            float screenSize = nodeWorldSize / Mathf.Max(dist, 0.001f) * camera.pixelHeight;
            // Desired lod: the level at which one page maps to ~pageSize pixels on screen
            float desiredLod = Mathf.Log(_settings.pageSize / Mathf.Max(screenSize, 1f), 2f) + _settings.lodBias;
            return desiredLod;
        }

        private bool ShouldSplit(SVTNode node, float desiredLod) =>
            node.LodLevel < _settings.maxLodLevels && desiredLod < node.LodLevel;

        private bool ShouldMerge(SVTNode node, float desiredLod) =>
            desiredLod >= node.LodLevel + 1;

        // ------------------------------------------------------------------ //
        // Split / merge helpers
        // ------------------------------------------------------------------ //

        private void SplitNode(SVTNode node)
        {
            _leaves.Remove(node);
            node.Split(_settings.worldSize);
            foreach (SVTNode child in node.Children)
                _leaves.Add(child);
            _splitThisFrame.Add(node);

            // Release parent page
            if (node.State == SVTNode.LoadState.Loaded || node.State == SVTNode.LoadState.Requested)
                OnPageReleased?.Invoke(node);
            node.State = SVTNode.LoadState.Unloaded;
        }

        private void MergeNode(SVTNode node)
        {
            // Release child pages
            foreach (SVTNode child in node.Children)
            {
                _leaves.Remove(child);
                if (child.State == SVTNode.LoadState.Loaded || child.State == SVTNode.LoadState.Requested)
                    OnPageReleased?.Invoke(child);
            }
            node.Merge();
            _leaves.Add(node);
            _mergedThisFrame.Add(node);
        }

        private void RequestPageIfNeeded(SVTNode node)
        {
            if (node.State == SVTNode.LoadState.Unloaded)
            {
                node.State = SVTNode.LoadState.Requested;
                OnPageRequested?.Invoke(node);
            }
        }

        // ------------------------------------------------------------------ //
        // Public accessors
        // ------------------------------------------------------------------ //

        /// <summary>All current leaf nodes.</summary>
        public IReadOnlyList<SVTNode> Leaves => _leaves;

        /// <summary>The root node of the tree.</summary>
        public SVTNode Root => _root;

        /// <summary>
        /// Collect all nodes via pre-order traversal into the provided list.
        /// 前序遍历收集所有节点。
        /// </summary>
        public void CollectAllNodes(List<SVTNode> result)
        {
            result.Clear();
            if (_root != null) CollectRecursive(_root, result);
        }

        private static void CollectRecursive(SVTNode node, List<SVTNode> result)
        {
            result.Add(node);
            if (node.IsSplit)
                foreach (SVTNode child in node.Children)
                    CollectRecursive(child, result);
        }
    }
}
