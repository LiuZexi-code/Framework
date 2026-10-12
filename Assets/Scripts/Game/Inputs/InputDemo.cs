using System.Collections;
using Framework.Abstractions.Input;
using Framework.Bootstrap;
using UnityEngine;

namespace Game.Inputs
{
    /// <summary>
    /// 输入演示：**完全事件驱动**（不依赖任何 Update / tick），验证
    /// ① 动作事件能拿到 actionId / phase / value；
    /// ② 上下文 Push/Pop 能真正阻断与恢复玩法输入。
    /// <para>
    /// 玩法：启动时推入 <c>Gameplay</c> 上下文；按 <c>Escape</c> → 推入 <c>UI</c> 上下文（玩法输入被阻断）；
    /// 再按 <c>Escape</c> → 弹出 <c>UI</c> 上下文（玩法输入恢复）。同一个键在不同上下文含义不同。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputDemo : MonoBehaviour
    {
        private const int ResolveTimeoutFrames = 600;

        private IInputService _input;

        /// <summary>自举：无需改场景、无需挂脚本（演示用）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Spawn()
        {
            var go = new GameObject("[InputDemo]");
            DontDestroyOnLoad(go);
            go.AddComponent<InputDemo>();
        }

        private IEnumerator Start()
        {
            // 服务要等引导流程注册完模块才可解析 → 轮询等待（有上限，避免死等）
            int waited = 0;
            while ((_input == null) && waited++ < ResolveTimeoutFrames)
            {
                if (ServiceLocator.TryGet<IInputService>(out var service) && service != null)
                {
                    _input = service;
                    break;
                }
                yield return null;
            }

            if (_input == null)
            {
                Debug.LogWarning("[InputDemo] 未能解析 IInputService，演示跳过");
                yield break;
            }

            _input.ActionTriggered += OnAction;
            _input.PushContext(GameActions.Contexts.Gameplay, GameActions.Priorities.Gameplay);
            Debug.Log("[InputDemo] 演示就绪：WASD/左摇杆 → Move，空格/A → Jump，鼠标左键/X → Attack，Escape → 切换 UI 上下文");
        }

        private void OnDestroy()
        {
            if (_input == null) return;

            _input.ActionTriggered -= OnAction;

            // 只在确实推入过时才 Pop（PopContext 对未推入的上下文会记警告）
            if (_input.IsContextActive(GameActions.Contexts.UI))
                _input.PopContext(GameActions.Contexts.UI);

            if (_input.IsContextActive(GameActions.Contexts.Gameplay))
                _input.PopContext(GameActions.Contexts.Gameplay);
        }

        private void OnAction(InputActionEvent e)
        {
            Debug.Log($"[InputDemo] {e}");

            if (e.Phase != InputTriggerPhase.Performed) return;

            if (e.ActionId == GameActions.Pause)
            {
                _input.PushContext(GameActions.Contexts.UI, GameActions.Priorities.UI);
                Debug.Log("[InputDemo] 打开 UI：Push UI（Gameplay 动作已被上下文阻断）");
            }
            else if (e.ActionId == GameActions.Cancel)
            {
                _input.PopContext(GameActions.Contexts.UI);
                Debug.Log("[InputDemo] 关闭 UI：Pop UI（Gameplay 动作已恢复）");
            }
        }
    }
}
