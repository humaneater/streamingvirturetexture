using UnityEngine;
using UnityEditor;
using SVT.Core;
using SVT.Terrain;

namespace SVT.Editor
{
    /// <summary>
    /// Unity Editor window for SVT tools:
    ///   - View SVT settings summary
    ///   - Trigger terrain layer baking
    ///   - Export terrain as quadtree mesh
    ///   - Debug quadtree visualization
    ///
    /// SVT工具Unity编辑器窗口：
    ///   - 查看SVT设置摘要
    ///   - 触发地形图层烘焙
    ///   - 将地形导出为四叉树网格
    ///   - 调试四叉树可视化
    /// </summary>
    public class SVTEditorWindow : EditorWindow
    {
        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        private SVTManager _svtManager;
        private SVTTerrainIntegration _terrainIntegration;
        private SVTTerrainLayerBaker _layerBaker;
        private SVTTerrainExporter _terrainExporter;

        private Vector2 _scrollPos;
        private bool _showSettings = true;
        private bool _showBakeTools = true;
        private bool _showExportTools = true;
        private bool _showDebugTools = true;

        // ------------------------------------------------------------------ //
        // Menu item
        // ------------------------------------------------------------------ //

        [MenuItem("Window/SVT/SVT Editor")]
        public static void ShowWindow()
        {
            var win = GetWindow<SVTEditorWindow>("SVT Editor");
            win.minSize = new Vector2(380f, 500f);
            win.Show();
        }

        // ------------------------------------------------------------------ //
        // GUI
        // ------------------------------------------------------------------ //

        private void OnEnable()
        {
            RefreshSceneReferences();
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(4f);

            DrawSceneObjects();
            EditorGUILayout.Space(4f);

            if (_showSettings) DrawSettingsSection();
            EditorGUILayout.Space(4f);

            if (_showBakeTools) DrawBakeSection();
            EditorGUILayout.Space(4f);

            if (_showExportTools) DrawExportSection();
            EditorGUILayout.Space(4f);

            if (_showDebugTools) DrawDebugSection();

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ //
        // Sections
        // ------------------------------------------------------------------ //

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Streaming Virtual Texture (SVT) Tools",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Unity 6000.3.7 Compatible", EditorStyles.miniLabel);
            EditorGUILayout.Separator();
        }

        private void DrawSceneObjects()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Scene References", EditorStyles.boldLabel);
                if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                    RefreshSceneReferences();
            }

            using (new EditorGUI.IndentLevelScope())
            {
                _svtManager = (SVTManager)EditorGUILayout.ObjectField(
                    "SVT Manager", _svtManager, typeof(SVTManager), true);
                _terrainIntegration = (SVTTerrainIntegration)EditorGUILayout.ObjectField(
                    "Terrain Integration", _terrainIntegration, typeof(SVTTerrainIntegration), true);
                _layerBaker = (SVTTerrainLayerBaker)EditorGUILayout.ObjectField(
                    "Layer Baker", _layerBaker, typeof(SVTTerrainLayerBaker), true);
                _terrainExporter = (SVTTerrainExporter)EditorGUILayout.ObjectField(
                    "Terrain Exporter", _terrainExporter, typeof(SVTTerrainExporter), true);
            }
        }

        private void DrawSettingsSection()
        {
            _showSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showSettings, "SVT Settings");
            if (_showSettings)
            {
                if (_svtManager != null && _svtManager.settings != null)
                {
                    var s = _svtManager.settings;
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.LabelField(
                            $"Virtual Texture: {s.virtualTextureWidth}x{s.virtualTextureHeight}");
                        EditorGUILayout.LabelField($"Page Size: {s.pageSize}px (border: {s.pageBorder}px)");
                        EditorGUILayout.LabelField(
                            $"Cache: {s.cacheColumns}x{s.cacheRows} = {s.CacheCapacity} slots");
                        EditorGUILayout.LabelField($"Max LOD Levels: {s.maxLodLevels}");
                        EditorGUILayout.LabelField($"World Size: {s.worldSize}m");
                        EditorGUILayout.LabelField($"Feedback Downscale: {s.feedbackDownscale}x");

                        EditorGUILayout.Space(4f);
                        if (GUILayout.Button("Edit Settings..."))
                            Selection.activeObject = s;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No SVTManager or SVTSettings found in scene.", MessageType.Info);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBakeSection()
        {
            _showBakeTools = EditorGUILayout.BeginFoldoutHeaderGroup(_showBakeTools, "Terrain Bake Tools");
            if (_showBakeTools)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.HelpBox(
                        "Bake all terrain layers into the merged SVT albedo and normal textures. " +
                        "Run this after painting terrain layers.", MessageType.Info);

                    EditorGUI.BeginDisabledGroup(_layerBaker == null);
                    if (GUILayout.Button("Bake Terrain Layers"))
                    {
                        Undo.RecordObject(_layerBaker, "SVT Bake Terrain Layers");
                        _layerBaker.BakeLayers();
                        EditorUtility.SetDirty(_layerBaker);
                        Debug.Log("[SVT] Terrain layers baked successfully.");
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.Space(4f);

                    EditorGUI.BeginDisabledGroup(_terrainIntegration == null);
                    if (GUILayout.Button("Setup From Terrain (Sync Dimensions)"))
                    {
                        _terrainIntegration.ManualRebake();
                        Debug.Log("[SVT] Terrain integration synced.");
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawExportSection()
        {
            _showExportTools = EditorGUILayout.BeginFoldoutHeaderGroup(_showExportTools, "Quadtree Mesh Export");
            if (_showExportTools)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.HelpBox(
                        "Export the terrain as a set of quadtree mesh patches. " +
                        "Each patch represents one SVT leaf node and can be rendered " +
                        "independently with the SVT shader.", MessageType.Info);

                    EditorGUI.BeginDisabledGroup(_terrainExporter == null);
                    if (GUILayout.Button("Export Quadtree Mesh Patches"))
                    {
                        if (EditorUtility.DisplayDialog("SVT Export",
                            "This will replace all existing exported patch GameObjects. Continue?",
                            "Export", "Cancel"))
                        {
                            _terrainExporter.Export();
                            Debug.Log("[SVT] Terrain exported as quadtree mesh patches.");
                        }
                    }
                    if (GUILayout.Button("Clear Exported Patches"))
                    {
                        _terrainExporter.ClearExportedPatches();
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDebugSection()
        {
            _showDebugTools = EditorGUILayout.BeginFoldoutHeaderGroup(_showDebugTools, "Debug");
            if (_showDebugTools)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    if (_svtManager != null)
                    {
                        var leaves = _svtManager.QuadTreeLeaves;
                        if (leaves != null)
                        {
                            int total = leaves.Count;
                            int loaded = 0, requested = 0, unloaded = 0;
                            foreach (var leaf in leaves)
                            {
                                switch (leaf.State)
                                {
                                    case SVTNode.LoadState.Loaded:    loaded++;    break;
                                    case SVTNode.LoadState.Requested: requested++; break;
                                    default:                          unloaded++;  break;
                                }
                            }
                            EditorGUILayout.LabelField($"Quadtree Leaves: {total}");
                            EditorGUILayout.LabelField($"  Loaded: {loaded}  |  Requested: {requested}  |  Unloaded: {unloaded}");
                        }

                        EditorGUILayout.Space(4f);
                        if (GUILayout.Button("Rebuild Quadtree"))
                        {
                            _svtManager.RebuildQuadTree();
                            Repaint();
                        }

                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField("Indirection Texture:", EditorStyles.boldLabel);
                        if (_svtManager.IndirectionTexture != null)
                        {
                            EditorGUI.DrawPreviewTexture(
                                GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(false)),
                                _svtManager.IndirectionTexture, null, ScaleMode.ScaleToFit);
                        }
                        else
                        {
                            EditorGUILayout.LabelField("  (not initialized)");
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("SVTManager not found in scene.", MessageType.Warning);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private void RefreshSceneReferences()
        {
            _svtManager = FindFirstObjectByType<SVTManager>();
            _terrainIntegration = FindFirstObjectByType<SVTTerrainIntegration>();
            _layerBaker = FindFirstObjectByType<SVTTerrainLayerBaker>();
            _terrainExporter = FindFirstObjectByType<SVTTerrainExporter>();
        }
    }
}
