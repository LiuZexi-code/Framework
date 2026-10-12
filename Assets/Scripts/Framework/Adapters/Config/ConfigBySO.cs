using System;
using System.Collections.Generic;
using Framework.Adapters;
using Framework.Abstractions.Assets;
using Framework.Abstractions.GameConfig;
using Framework.Abstractions.Logging;
using Framework.UI;
using UnityEngine;
using Framework.Adapters.Logging;

namespace Framework.Adapters.Config
{
    public class ConfigBySO<T> : GameConfigBase where T : ScriptableObject
    {
        public override bool IsPersistent => false;

        public T Data { get; private set; }

        public ConfigBySO(string loadAddress, string configId = null, int loadOrder = 0)
            : base(string.IsNullOrWhiteSpace(configId) ? typeof(T).Name : configId, loadOrder)
        {
            LoadAddress = loadAddress;
        }

        public override void Load(IAssetService assetService)
        {
            var handle = assetService.Load<T>(LoadAddress);
            if (handle == null || !handle.IsValid)
            {
                Log.Error($"ConfigAsset 加载失败：{LoadAddress}");
                return;
            }
            Data = handle.Asset;
            if (Data == null)
            {
                Data = ScriptableObject.CreateInstance<T>();
                Debug.LogWarning("Config not found, use default.");
            }
            IsLoaded = true;
        }
    }
}