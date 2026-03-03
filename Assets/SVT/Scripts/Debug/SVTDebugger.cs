using System.Collections.Generic;
using UnityEngine;
using SVT.Core;

namespace SVT.Debug
{
    /// <summary>
    /// Runtime debugger for the SVT system.
    /// Draws Gizmos for quadtree nodes (loaded/unloaded/requested)
    /// and renders an on-screen overlay showing cache usage statistics.
    ///
    /// SVT系统运行时调试器。
    /// 为四叉树节点绘制Gizmos（已加载/未加载/已请求），
    /// 并渲染显示缓存使用统计的屏幕覆盖层。
    /// </summary>
    public class SVTDebugger : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Inspector
        // ------------------------------------------------------------------ //

        [Header("References")]
        public SVTManager svtManager;

        [Header("Gizmo Settings")]
        [Tooltip("Draw quadtree node gizmos in the Scene view and Game view.")]
        public bool drawGizmos = true;

        [Tooltip("Height offset above terrain surface for gizmo wireframes.")]
        public float gizmoHeightOffset = 1f;

        [Header("Overlay Settings")]
        [Tooltip("Show the on-screen HUD overlay in Play mode.")]
        public bool showOverlay = true;

        [Tooltip("Position of the overlay panel (normalised screen coords).")]
        public Vector2 overlayPosition = new Vector2(0.01f, 0.01f);

        [Tooltip("Size of the overlay panel (normalised screen coords).")]
        public Vector2 overlaySize = new Vector2(0.3f, 0.25f);

        // ------------------------------------------------------------------ //
        // Runtime
        // ------------------------------------------------------------------ //

        private readonly List<SVTNode> _allNodes = new List<SVTNode>();

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        private void Start()
        {
            if (svtManager == null)
                svtManager = SVTManager.Instance;
        }

        private void OnGUI()
        {
            if (!showOverlay || svtManager == null) return;
            EnsureStyles();
            DrawOverlay();
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || svtManager == null) return;
            DrawQuadTreeGizmos();
        }

        // ------------------------------------------------------------------ //
        // Overlay
        // ------------------------------------------------------------------ //

        private void DrawOverlay()
        {
            SVTSettings s = svtManager.Settings;
            if (s == null) return;

            IReadOnlyList<SVTNode> leaves = svtManager.QuadTreeLeaves;
            int totalLeaves = leaves?.Count ?? 0;
            int loadedLeaves = 0;
            int requestedLeaves = 0;
            if (leaves != null)
            {
                foreach (var leaf in leaves)
                {
                    if (leaf.State == SVTNode.LoadState.Loaded) loadedLeaves++;
                    else if (leaf.State == SVTNode.LoadState.Requested) requestedLeaves++;
                }
            }

            float sw = Screen.width;
            float sh = Screen.height;
            Rect panelRect = new Rect(
                overlayPosition.x * sw,
                overlayPosition.y * sh,
                overlaySize.x * sw,
                overlaySize.y * sh);

            GUI.Box(panelRect, GUIContent.none, _boxStyle);

            float padding = 8f;
            Rect labelRect = new Rect(
                panelRect.x + padding,
                panelRect.y + padding,
                panelRect.width - padding * 2,
                20f);

            DrawLabel(ref labelRect, "<b>SVT Debug</b>");
            DrawLabel(ref labelRect, $"Virtual Texture: {s.virtualTextureWidth}x{s.virtualTextureHeight}");
            DrawLabel(ref labelRect, $"Page Size: {s.pageSize}px | Max LOD: {s.maxLodLevels}");
            DrawLabel(ref labelRect, $"Cache: {s.cacheColumns}x{s.cacheRows} slots");
            DrawLabel(ref labelRect, $"Leaves: {totalLeaves} | Loaded: {loadedLeaves} | Requested: {requestedLeaves}");
            DrawLabel(ref labelRect, $"Frame: {Time.frameCount}");
        }

        private void DrawLabel(ref Rect rect, string text)
        {
            GUI.Label(rect, text, _labelStyle);
            rect.y += rect.height + 2f;
        }

        private void EnsureStyles()
        {
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle("box")
                {
                    normal = { background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.7f)) }
                };
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    normal = { textColor = Color.white },
                    richText = true
                };
            }
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            Texture2D result = new Texture2D(w, h);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        // ------------------------------------------------------------------ //
        // Gizmos
        // ------------------------------------------------------------------ //

        private void DrawQuadTreeGizmos()
        {
            if (svtManager == null) return;

            var leaves = svtManager.QuadTreeLeaves;
            if (leaves == null) return;

            foreach (SVTNode leaf in leaves)
            {
                Color c;
                switch (leaf.State)
                {
                    case SVTNode.LoadState.Loaded:    c = svtManager.Settings?.debugLoadedPageColor ?? Color.green; break;
                    case SVTNode.LoadState.Requested: c = Color.yellow; break;
                    default:                          c = svtManager.Settings?.debugMissingPageColor ?? Color.red; break;
                }

                Gizmos.color = new Color(c.r, c.g, c.b, 0.3f);
                DrawNodeGizmo(leaf);
            }
        }

        private void DrawNodeGizmo(SVTNode node)
        {
            Bounds b = node.WorldBounds;
            Vector3 center = new Vector3(b.center.x, b.min.y + gizmoHeightOffset, b.center.z);
            Vector3 size = new Vector3(b.size.x, 0.1f, b.size.z);
            Gizmos.DrawCube(center, size);

            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 1f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
