using System.Collections.Generic;
using Framework.Abstractions.Scenes;
using UnityEngine;

namespace Game.Scenes.Config
{
    /// <summary>
    /// 单个场景的配置项。字段含义见 <see cref="SceneDefinition"/>。
    /// <para>只存字符串，不引用场景资产（原因见 <see cref="SceneNameAttribute"/>）。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Config/Scene Config", fileName = "SceneConfig")]
    public sealed class SceneConfigSO : ScriptableObject
    {
        [SceneName]
        [Tooltip("场景逻辑名（= Build Settings 中的场景名）")]
        public string sceneName;

        [Tooltip("Addressables 地址；留空表示与 sceneName 相同")]
        public string address;

        [Tooltip("常驻：切到其它场景时不卸载本场景（**仅 Additive 系策略支持**）")]
        public bool persistent;

        [Tooltip("进入本场景时是否显示 Loading（常驻 Loading 场景 + Loading 遮罩面板）")]
        public bool showLoading = true;

        [Tooltip("进入本场景前预加载的 Addressables 地址")]
        public List<string> preloadAddresses = new();

        [Tooltip("场景标签（供面板 SceneTags / 资源筛选对齐）")]
        public List<string> tags = new();

        [Tooltip("等待场景容器建立的超时（秒）；<= 0 表示不限")]
        public float waitTimeoutSeconds = 10f;

        /// <summary>转换为运行期只读定义；场景名为空时返回 null（调用方跳过并告警）。</summary>
        public SceneDefinition ToDefinition()
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return null;

            return new SceneDefinition(
                sceneName.Trim(),
                string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
                persistent,
                showLoading,
                preloadAddresses,
                tags,
                waitTimeoutSeconds);
        }
    }
}
