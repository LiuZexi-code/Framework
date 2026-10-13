using UnityEngine;

namespace Game.RPG.Players
{
    /// <summary>
    /// 点击落点标记：出现时由小放大（并轻微下沉），随后回到略大的尺寸并自行隐藏。
    /// <para>
    /// 刻意**不做透明度淡出**：URP Lit 要淡出必须切成 Transparent 表面类型并改混合/队列，
    /// 占位阶段不值得；用「缩放」表达已是足够的视觉反馈。
    /// </para>
    /// <para>
    /// 实例由 <see cref="PlayerMotor"/> 复用一个（不每次点击都 Instantiate），
    /// 结束时 <c>SetActive(false)</c> 而不是销毁。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClickMarkerView : MonoBehaviour
    {
        [SerializeField] private float duration = 0.7f;
        [SerializeField] private float startScale = 0.5f;
        [SerializeField] private float peakScale = 1.4f;
        [SerializeField] private float endScale = 1.7f;
        [SerializeField] private float growPortion = 0.35f;
        [SerializeField] private float surfaceOffset = 0.02f;

        private Vector3 _baseScale;
        private bool _initialized;
        private float _elapsed;
        private bool _playing;

        /// <summary>在世界坐标处播放一次标记动画。</summary>
        public void Play(Vector3 worldPosition)
        {
            if (!_initialized)
            {
                _baseScale = transform.localScale;
                _initialized = true;
            }

            transform.position = worldPosition + Vector3.up * surfaceOffset;
            transform.localScale = _baseScale * startScale;

            _elapsed = 0f;
            _playing = true;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!_playing) return;

            _elapsed += Time.deltaTime;
            var t = duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / duration);

            var grow = Mathf.Clamp01(growPortion);
            var scale = t < grow
                ? Mathf.Lerp(startScale, peakScale, grow <= 0f ? 1f : t / grow)
                : Mathf.Lerp(peakScale, endScale, (1f - grow) <= 0f ? 1f : (t - grow) / (1f - grow));

            transform.localScale = _baseScale * scale;

            if (t < 1f) return;

            _playing = false;
            gameObject.SetActive(false);
        }
    }
}
