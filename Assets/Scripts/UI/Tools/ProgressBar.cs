using UnityEngine;
using UnityEngine.UI;

namespace Game.UI.Tools
{
    /// <summary>把 0..1 的进度映射到 Slider，并在进度为 0 时隐藏填充。</summary>
    public class ProgressBar : MonoBehaviour
    {
        public Slider slider;
        public float minVisualProgress = 0.05f;

        public void SetProgress(float progress01)
        {
            progress01 = Mathf.Clamp01(progress01);

            // 进度为0时直接隐藏填充，只留背景
            if (progress01 <= 0.001f)
            {
                slider.fillRect.gameObject.SetActive(false);
                return;
            }

            slider.fillRect.gameObject.SetActive(true);

            // 视觉宽度 = 从最小宽度到最大宽度映射
            float visualProgress = Mathf.Max(progress01, minVisualProgress);
            slider.value = visualProgress;
        }
    }
}
