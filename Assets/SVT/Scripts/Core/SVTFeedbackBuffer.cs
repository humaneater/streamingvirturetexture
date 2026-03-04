using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SVT.Core
{
    /// <summary>
    /// Captures per-frame GPU feedback to determine which virtual pages are visible.
    /// 
    /// A low-resolution "feedback" pass renders each visible surface encoding
    /// (virtualPageX, virtualPageY, mipLevel) into an offscreen buffer.
    /// The buffer is asynchronously read back to the CPU to drive page requests.
    /// 
    /// 捕获每帧GPU反馈，以确定哪些虚拟页面可见。
    /// 一个低分辨率的反馈pass将可见表面的(virtualPageX, virtualPageY, mipLevel)编码到离屏缓冲区，
    /// 然后异步回读到CPU以驱动页面请求。
    /// </summary>
    public class SVTFeedbackBuffer : IDisposable
    {
        // ------------------------------------------------------------------ //
        // Events
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Fired once per frame when the readback data is available.
        /// The Color32 array contains the raw feedback pixels.
        /// 每帧反馈数据可用时触发，Color32数组包含原始反馈像素。
        /// </summary>
        public event Action<Color32[]> OnFeedbackReady;

        // ------------------------------------------------------------------ //
        // State
        // ------------------------------------------------------------------ //

        private readonly SVTSettings _settings;

        private RenderTexture _feedbackRT;
        private Texture2D _readbackTexture;

        private AsyncGPUReadbackRequest _pendingRequest;
        private bool _requestInFlight;

        // ------------------------------------------------------------------ //
        // Construction
        // ------------------------------------------------------------------ //

        public SVTFeedbackBuffer(SVTSettings settings)
        {
            _settings = settings;
        }

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        public RenderTexture FeedbackRT => _feedbackRT;

        /// <summary>
        /// Create the feedback RenderTexture sized for the given camera.
        /// 根据相机分辨率创建反馈RenderTexture。
        /// </summary>
        public void Initialize(int screenWidth, int screenHeight)
        {
            int w = Mathf.Max(1, screenWidth / _settings.feedbackDownscale);
            int h = Mathf.Max(1, screenHeight / _settings.feedbackDownscale);

            if (_feedbackRT != null)
            {
                _feedbackRT.Release();
                SafeDestroy(_feedbackRT);
            }

            _feedbackRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "SVT_FeedbackBuffer",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _feedbackRT.Create();

            if (_readbackTexture != null)
                SafeDestroy(_readbackTexture);
            _readbackTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
        }

        /// <summary>
        /// Dispatch an async GPU readback of the feedback buffer.
        /// Call this after rendering the feedback pass each frame.
        /// 异步回读反馈缓冲区，在每帧渲染反馈pass后调用。
        /// </summary>
        public void RequestReadback()
        {
            if (_feedbackRT == null || _requestInFlight) return;

            _pendingRequest = AsyncGPUReadback.Request(_feedbackRT, 0, TextureFormat.RGBA32);
            _requestInFlight = true;
        }

        /// <summary>
        /// Poll for completed readback. Call once per frame.
        /// 每帧轮询已完成的回读请求。
        /// </summary>
        public void Poll()
        {
            if (!_requestInFlight) return;
            if (!_pendingRequest.done) return;

            _requestInFlight = false;

            if (_pendingRequest.hasError)
            {
                Debug.LogWarning("[SVT] GPU readback error in feedback buffer.");
                return;
            }

            var data = _pendingRequest.GetData<Color32>();
            Color32[] pixels = data.ToArray();
            OnFeedbackReady?.Invoke(pixels);
        }

        // ------------------------------------------------------------------ //
        // IDisposable
        // ------------------------------------------------------------------ //

        public void Dispose()
        {
            if (_feedbackRT != null)
            {
                _feedbackRT.Release();
                SafeDestroy(_feedbackRT);
                _feedbackRT = null;
            }
            if (_readbackTexture != null)
            {
                SafeDestroy(_readbackTexture);
                _readbackTexture = null;
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
