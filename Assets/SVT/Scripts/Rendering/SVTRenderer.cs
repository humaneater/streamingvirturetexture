using UnityEngine;
using SVT.Core;

namespace SVT.Rendering
{
    /// <summary>
    /// Manages the SVT rendering pass:
    ///   1. Renders the feedback pass to <see cref="SVTManager.FeedbackRT"/>.
    ///   2. Issues the async readback request.
    ///   3. Applies the SVT material to all registered renderers each frame.
    ///
    /// SVT渲染管线管理器：
    ///   1. 将反馈pass渲染到FeedbackRT。
    ///   2. 发起异步回读请求。
    ///   3. 每帧将SVT材质应用到所有已注册的渲染器。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SVTRenderer : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Inspector
        // ------------------------------------------------------------------ //

        [Header("References")]
        public SVTManager svtManager;

        [Header("Feedback Pass")]
        [Tooltip("Material used to render the feedback pass (uses SVT/Feedback shader).")]
        public Material feedbackMaterial;

        [Header("Scene Renderers")]
        [Tooltip("Renderers that use the SVT material. Leave empty to auto-collect.")]
        public Renderer[] svtRenderers;

        // ------------------------------------------------------------------ //
        // Runtime
        // ------------------------------------------------------------------ //

        private Camera _camera;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        private void Start()
        {
            if (svtManager == null)
                svtManager = SVTManager.Instance;

            _camera = GetComponent<Camera>();
            if (_camera == null)
                _camera = Camera.main;

            if (svtRenderers == null || svtRenderers.Length == 0)
                svtRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        }

        private void OnPreRender()
        {
            RenderFeedbackPass();
        }

        // ------------------------------------------------------------------ //
        // Feedback pass
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Renders each SVT mesh into the feedback RenderTexture using the feedback shader.
        /// Uses Graphics.DrawMeshNow to avoid modifying sharedMaterials at runtime.
        /// 使用反馈着色器将每个SVT网格渲染到反馈RenderTexture，
        /// 使用Graphics.DrawMeshNow避免运行时修改sharedMaterials。
        /// </summary>
        private void RenderFeedbackPass()
        {
            if (svtManager == null || feedbackMaterial == null) return;
            RenderTexture feedbackRT = svtManager.FeedbackRT;
            if (feedbackRT == null) return;

            SVTSettings settings = svtManager.Settings;
            if (settings == null) return;

            // Bind SVT parameters to the feedback material
            feedbackMaterial.SetTexture("_SVT_IndirectionTex", svtManager.IndirectionTexture);
            feedbackMaterial.SetVector("_SVT_WorldRect",
                new Vector4(svtManager.terrainOrigin.x, svtManager.terrainOrigin.z,
                            settings.worldSize, settings.worldSize));
            feedbackMaterial.SetVector("_SVT_PageTableSize",
                new Vector4(settings.PagesX, settings.PagesY, 0f, 0f));
            feedbackMaterial.SetFloat("_SVT_MaxMip", settings.maxLodLevels);

            var prevRT = RenderTexture.active;
            RenderTexture.active = feedbackRT;
            GL.Clear(false, true, Color.clear);

            if (feedbackMaterial.SetPass(0))
            {
                foreach (Renderer r in svtRenderers)
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;

                    Graphics.DrawMeshNow(mf.sharedMesh, r.transform.localToWorldMatrix);
                }
            }

            RenderTexture.active = prevRT;

            // Kick off async readback for next frame
            svtManager.GetComponent<SVTFeedbackRequestHelper>()?.RequestReadback();
        }
    }
}
