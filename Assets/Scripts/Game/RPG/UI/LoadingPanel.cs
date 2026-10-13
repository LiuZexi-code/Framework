using Framework.Abstractions.UI;
using Framework.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.RPG.UI
{
    [UIPanel("LoadingPanel")]
    public sealed class LoadingPanel : UIPanelBase, ILoadingPanel
    {
        [SerializeField] private Slider _progressBar;
        [SerializeField] private TMP_Text _stageText;

        public void SetProgress(float progress, string stage)
        {
            if (_progressBar != null) _progressBar.value = Mathf.Clamp01(progress);
            if (_stageText != null) _stageText.text = stage;
        }
    }
}