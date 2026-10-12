using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using Framework.Abstractions.UI;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.UI
{
    public sealed class PanelFactory : IPanelFactory
    {
        private readonly UIRoot _uiRoot;   // UI根节点
        private readonly ILogger _logger;
        private readonly IAssetService _assetService;
        private readonly Dictionary<IUIPanel, IAssetHandle<GameObject>> _assetHandles = new();

        public PanelFactory(IAssetService assetService,ILogger logger,UISettings uiSettings = null,UIRoot uiRoot = null)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger;

            if (uiRoot == null)
                UIRootCreator.CreateUIRoot(out _uiRoot, null, uiSettings);
            else
                _uiRoot = uiRoot;
        }

        public IUIPanel Create(PanelDefinition definition)
        {
            if (definition == null) return null;

            if (!_assetService.TryGetLoaded<GameObject>(definition.PrefabAddress, out var prefab))
            {
                _logger?.Error($"[UIFactory] Prefab not preloaded: {definition.PrefabAddress}");
                return null;
            }

            return InstantiatePanel(prefab, definition);
        }

        public async Task<IUIPanel> CreateAsync(PanelDefinition definition)
        {
            if (definition == null) return null;

            var handle = await _assetService.LoadAsync<GameObject>(definition.PrefabAddress);
            if (handle == null || !handle.IsValid)
            {
                _logger?.Error($"[UIFactory] Load failed: {definition.PrefabAddress}");
                return null;
            }

            var panel = InstantiatePanel(handle.Asset, definition);
            if (panel == null)
            {
                handle.Dispose();
                return null;
            }

            // 句柄和实例绑定，销毁时一起释放
            _assetHandles[panel] = handle;
            return panel;
        }

        public void Destroy(IUIPanel panel)
        {
            if (panel == null) return;

            if (_assetHandles.TryGetValue(panel, out var handle))
            {
                handle?.Dispose();
                _assetHandles.Remove(panel);
            }

            if (panel is MonoBehaviour mb && mb != null)
                UnityEngine.Object.Destroy(mb.gameObject);
        }

        private IUIPanel InstantiatePanel(GameObject prefab, PanelDefinition definition)
        {
            if (prefab == null) return null;

            var go = UnityEngine.Object.Instantiate(prefab);
            var parent = _uiRoot != null ? _uiRoot.GetLayer(definition.Layer) : null;
            if (parent != null) go.transform.SetParent(parent, false);
            go.name = prefab.name;

            // 面板脚本可能挂在根节点或其子节点上
            var panel = go.GetComponent<IUIPanel>() ?? go.GetComponentInChildren<IUIPanel>(true);
            if (panel == null)
            {
                _logger?.Error($"[UIFactory] Panel component not found on {prefab.name}");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            return panel;
        }
    }
}