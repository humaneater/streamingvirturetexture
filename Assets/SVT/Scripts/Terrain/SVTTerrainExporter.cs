using System.Collections.Generic;
using UnityEngine;
using SVT.Core;

namespace SVT.Terrain
{
    /// <summary>
    /// Exports Unity Terrain geometry as a quadtree-based mesh dataset.
    /// Each quadtree leaf node becomes a separate mesh patch that can be
    /// rendered independently with the SVT shader, producing identical visual
    /// results to the original Unity Terrain.
    ///
    /// 将Unity地形几何体导出为基于四叉树的网格数据集。
    /// 每个四叉树叶节点成为一个独立的网格块，可使用SVT着色器独立渲染，
    /// 产生与原始Unity地形相同的视觉效果。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Terrain))]
    public class SVTTerrainExporter : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Inspector
        // ------------------------------------------------------------------ //

        [Header("References")]
        public SVTManager svtManager;

        [Header("Export Settings")]
        [Tooltip("Number of vertices per edge of each exported mesh patch.")]
        [Range(4, 64)]
        public int verticesPerPatchEdge = 16;

        [Tooltip("Layer mask for the exported mesh objects.")]
        public LayerMask exportLayer;

        [Tooltip("Material applied to the exported mesh objects.")]
        public Material exportMaterial;

        [Tooltip("Root GameObject to parent all exported patches to.")]
        public Transform exportRoot;

        // ------------------------------------------------------------------ //
        // Runtime
        // ------------------------------------------------------------------ //

        private UnityEngine.Terrain _terrain;
        private TerrainData _terrainData;
        private readonly List<GameObject> _exportedPatches = new List<GameObject>();

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        private void Awake()
        {
            _terrain = GetComponent<UnityEngine.Terrain>();
            _terrainData = _terrain.terrainData;
        }

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Export the terrain as a collection of quadtree mesh patches.
        /// All previously exported patches are destroyed first.
        /// 将地形导出为四叉树网格块集合，先销毁所有已导出的块。
        /// </summary>
        public void Export()
        {
            if (svtManager == null)
            {
                Debug.LogError("[SVT] SVTTerrainExporter: SVTManager not assigned.");
                return;
            }

            ClearExportedPatches();

            Transform root = exportRoot;
            if (root == null)
            {
                GameObject rootGO = new GameObject("SVT_TerrainMeshRoot");
                rootGO.transform.SetParent(transform, false);
                root = rootGO.transform;
            }

            IReadOnlyList<SVTNode> leaves = svtManager.QuadTreeLeaves;
            if (leaves == null) return;

            foreach (SVTNode leaf in leaves)
                ExportPatch(leaf, root);
        }

        /// <summary>
        /// Destroy all previously exported patch GameObjects.
        /// 销毁所有已导出的网格块GameObject。
        /// </summary>
        public void ClearExportedPatches()
        {
            foreach (GameObject go in _exportedPatches)
            {
                if (go != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        DestroyImmediate(go);
                    else
                        Destroy(go);
#else
                    Destroy(go);
#endif
                }
            }
            _exportedPatches.Clear();
        }

        // ------------------------------------------------------------------ //
        // Patch generation
        // ------------------------------------------------------------------ //

        private void ExportPatch(SVTNode leaf, Transform parent)
        {
            Mesh mesh = BuildPatchMesh(leaf);
            if (mesh == null) return;

            GameObject patchGO = new GameObject(
                $"SVTPatch_mip{leaf.LodLevel}_{leaf.PageId.pageX}_{leaf.PageId.pageY}");
            // Extract the lowest set layer index from the mask, default to 0
            int layerValue = exportLayer.value;
            int layerIndex = layerValue != 0
                ? Mathf.FloorToInt(Mathf.Log(layerValue & -layerValue, 2))
                : 0;
            patchGO.layer = layerIndex;
            patchGO.transform.SetParent(parent, false);

            MeshFilter mf = patchGO.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = patchGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = exportMaterial != null
                ? exportMaterial
                : new Material(Shader.Find("SVT/SVTTerrain"));

            // Assign SVT UV transform so the patch samples the correct region
            if (mr.sharedMaterial != null)
            {
                Rect nb = leaf.NormalizedBounds;
                mr.sharedMaterial.SetVector("_SVT_UVOffset",
                    new Vector4(nb.x, nb.y, nb.width, nb.height));
            }

            _exportedPatches.Add(patchGO);
        }

        /// <summary>
        /// Build a mesh for a single quadtree leaf patch by sampling terrain height.
        /// 通过采样地形高度，为单个四叉树叶节点构建网格。
        /// </summary>
        private Mesh BuildPatchMesh(SVTNode leaf)
        {
            int n = verticesPerPatchEdge;
            int vertCount = n * n;
            int triCount = (n - 1) * (n - 1) * 6;

            Vector3[] verts = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            int[] tris = new int[triCount];

            Rect nb = leaf.NormalizedBounds;
            Vector3 terrainPos = _terrain.transform.position;
            Vector3 terrainSize = _terrainData.size;

            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++)
                {
                    float t = (float)col / (n - 1);
                    float s = (float)row / (n - 1);

                    float normX = nb.x + t * nb.width;
                    float normZ = nb.y + s * nb.height;

                    float worldX = terrainPos.x + normX * terrainSize.x;
                    float worldZ = terrainPos.z + normZ * terrainSize.z;
                    float worldY = _terrain.SampleHeight(new Vector3(worldX, 0f, worldZ))
                                   + terrainPos.y;

                    verts[row * n + col] = new Vector3(worldX, worldY, worldZ);
                    uvs[row * n + col] = new Vector2(normX, normZ);
                }
            }

            int idx = 0;
            for (int row = 0; row < n - 1; row++)
            {
                for (int col = 0; col < n - 1; col++)
                {
                    int bl = row * n + col;
                    int br = bl + 1;
                    int tl = bl + n;
                    int tr = tl + 1;

                    tris[idx++] = bl; tris[idx++] = tl; tris[idx++] = tr;
                    tris[idx++] = bl; tris[idx++] = tr; tris[idx++] = br;
                }
            }

            Mesh mesh = new Mesh { name = $"SVTPatch_{leaf.PageId}" };
            mesh.indexFormat = vertCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
