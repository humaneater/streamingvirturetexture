using UnityEngine;
using SVT.Core;

namespace SVT.Rendering
{
    /// <summary>
    /// Thin helper that sits on the SVTManager GameObject and provides a
    /// public entry point for SVTRenderer to trigger the async feedback readback.
    ///
    /// 薄辅助类，挂载在SVTManager的GameObject上，为SVTRenderer提供触发异步反馈回读的入口。
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class SVTFeedbackRequestHelper : MonoBehaviour
    {
        private SVTManager _svtManager;

        private void Awake()
        {
            _svtManager = GetComponent<SVTManager>();
        }

        /// <summary>
        /// Request an async GPU readback of the current feedback buffer.
        /// Called by SVTRenderer after the feedback pass is rendered.
        /// 在反馈pass渲染后由SVTRenderer调用，发起异步GPU回读请求。
        /// </summary>
        public void RequestReadback()
        {
            if (_svtManager == null) return;

            // Access the internal feedback buffer through the manager's exposed RT.
            // The SVTFeedbackBuffer polls itself in SVTManager.Update(), so we just
            // need to signal that a readback should be queued this frame.
            // We expose a method via SVTManager for this.
            _svtManager.RequestFeedbackReadback();
        }
    }
}
