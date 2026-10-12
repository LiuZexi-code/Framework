using System.IO;
using Framework.Abstractions.Assets;
using Framework.Abstractions.Storage;
using Framework.Adapters.Assets;
using Framework.Adapters.Logging;
using Framework.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 编辑器自测菜单（Tools/我的工具/...）：验证资源加载与本地/内存持久化。
    /// 依赖 ServiceLocator 已就绪 —— 需在**运行模式**下使用。
    /// </summary>
    public class FrameworkTest
    {
        [MenuItem("Tools/我的工具/Addressables资源加载测试")]
        public static void FrameworkTestAddressablesAssetsLoad()
        {
            LoadTest();
        }

        private static void LoadTest()
        {
            IAssetService assetService = ServiceLocator.Get<IAssetService>();

            assetService.LoadAsync<GameObject>("Cube", handle =>
            {
                Object.Instantiate(handle.Asset);
                handle.Dispose();
            });

            if (assetService is IAddressablesCapability addressablesCapability)
            {
                addressablesCapability.LoadAsyncByLable<GameObject>("Enemy", handle =>
                {
                    foreach (var obj in handle.Asset)
                    {
                        Object.Instantiate(obj);
                    }
                    handle.Dispose();
                });
            }
        }

        [MenuItem("Tools/我的工具/内存持久化测试/1数据存储")]
        public static void FrameworkTestInMemoryStorageSave()
        {
            IStorage storage = ServiceLocator.Get<IStorage>();
            if (storage.Save("Test", "数据存储测试"))
            {
                Log.Info("数据存储成功");
            }
        }

        [MenuItem("Tools/我的工具/内存持久化测试/2数据加载")]
        public static void FrameworkTestInMemoryStorageLoad()
        {
            IStorage storage = ServiceLocator.Get<IStorage>();
            string key = "Test";
            var result = storage.Load<string>(key);
            if (result.Success)
            {
                Log.Info($"数据加载成功：key:{key},value:{result.Value}");
            }
            else
            {
                Log.Error($"数据加载失败，失败原因：{result.Error}");
            }
        }

        private static int saveCount = default;

        [MenuItem("Tools/我的工具/本地Json持久化测试/1数据存储")]
        public static void FrameworkTestLocalJsonStorageSave()
        {
            IStorage storage = ServiceLocator.Get<IStorage>();
            if (storage.Save("Test", "数据存储测试" + ++saveCount))
            {
                Log.Info("数据存储成功,次数：" + saveCount);
            }
        }

        [MenuItem("Tools/我的工具/本地Json持久化测试/2数据加载")]
        public static void FrameworkTestLocalJsonStorageLoad()
        {
            IStorage storage = ServiceLocator.Get<IStorage>();
            string key = "Test";
            var result = storage.Load<string>(key);
            if (result.Success)
            {
                Log.Info($"数据加载成功：key:{key},value:{result.Value},数据存储地址：{Path.Combine(Application.persistentDataPath, "Storage")}");
            }
            else
            {
                Log.Error($"数据加载失败，失败原因：{result.Error}");
            }
        }

        // ---------------- 场景切换（运行模式下用，方便手动验证 A→B）----------------

        [MenuItem("Tools/我的工具/场景切换/切到 Level2")]
        public static void SwitchToLevel2() => SwitchScene("Level2");

        [MenuItem("Tools/我的工具/场景切换/切回 SampleScene")]
        public static void SwitchToSampleScene() => SwitchScene("SampleScene");

        /// <summary>运行模式下触发切换：框架会自己跑 Loading 进度 + 目标场景的预加载管线。</summary>
        private static void SwitchScene(string sceneName)
        {
            if (!Application.isPlaying)
            {
                Log.Warn($"[SceneMenu] 请先运行游戏，再切到 {sceneName}");
                return;
            }

            if (!ServiceLocator.TryGet<ISceneFlow>(out var flow) || flow == null)
            {
                Log.Error("[SceneMenu] ISceneFlow 未就绪（框架可能还没装配完）");
                return;
            }

            Log.Info($"[SceneMenu] 切换场景：{sceneName}");
            flow.SwitchScene(sceneName);
        }
    }
}

