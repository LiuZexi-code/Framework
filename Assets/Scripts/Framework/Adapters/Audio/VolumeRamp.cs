namespace Framework.Adapters.Audio
{
    /// <summary>
    /// 线性音量渐变（纯逻辑，无 Unity 依赖 → 可同步单测）。
    /// <para>
    /// 用途有两个：① 播放淡入 / 停止淡出；② Ducking 的平滑过渡。
    /// 之所以抽成独立类而不是内联在 <c>AudioSource</c> 上，是为了让「插值是否正确」
    /// 变成**可断言的纯函数行为**，而不是只能靠 Play 目测。
    /// </para>
    /// </summary>
    public sealed class VolumeRamp
    {
        private float _from;
        private float _to;
        private float _duration;
        private float _elapsed;

        /// <summary>当前插值结果。</summary>
        public float Value { get; private set; }

        /// <summary>是否已到达目标（未开始时为 true）。</summary>
        public bool IsDone { get; private set; }

        /// <summary>以某个音量作为起点（视为已到达）。</summary>
        public VolumeRamp(float start)
        {
            Value = start;
            _from = start;
            _to = start;
            IsDone = true;
        }

        /// <summary>从**当前值**重新设定目标与时长（&lt;= 0 秒则立即到达）。</summary>
        public void Retarget(float to, float duration)
        {
            _from = Value;
            _to = to;
            _duration = duration < 0f ? 0f : duration;
            _elapsed = 0f;

            if (_duration <= 0f)
            {
                Value = _to;
                IsDone = true;
            }
            else
            {
                IsDone = false;
            }
        }

        /// <summary>立即跳到目标值。</summary>
        public void Finish()
        {
            _elapsed = _duration;
            Value = _to;
            IsDone = true;
        }

        public void Tick(float deltaTime)
        {
            if (IsDone) return;

            _elapsed += deltaTime < 0f ? 0f : deltaTime;

            if (_elapsed >= _duration)
            {
                Value = _to;
                IsDone = true;
                return;
            }

            Value = _from + (_to - _from) * (_elapsed / _duration);
        }
    }
}
