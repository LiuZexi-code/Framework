using Framework.UI;
using TMPro;
using UnityEngine;

namespace Game.RPG.UI
{
    /// <summary>
    /// M4 示例面板：验证「新增面板」只需 4 步（脚本 / 预制体 / Addressables 地址 / UIConfigAsset 条目），
    /// 无需改动任何框架代码。
    /// </summary>
    [UIPanel("DemoPanel")]
    public sealed class DemoPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _title;

        public void SetTitle(string text)
        {
            if (_title != null) _title.text = text;
        }
    }
}
