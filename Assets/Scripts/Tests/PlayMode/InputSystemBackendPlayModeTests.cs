using System;
using Framework.Abstractions.Input;
using Framework.Adapters.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// <see cref="InputSystemBackend"/> 的 **PlayMode** 测试。
    /// <para>
    /// 为什么必须放在 PlayMode：真实输入只有在引擎的输入更新里才会产生「设备状态」。
    /// EditMode 下动作没绑定设备时 <c>InputAction.activeValueType</c> 为 <c>null</c>、
    /// <c>ReadValue&lt;T&gt;()</c> 直接返回 0 而不做类型校验 —— 所以 EditMode **复现不出**
    /// 「对 Vector2 动作调用 <c>ReadValue&lt;float&gt;()</c> 抛异常」这个真 bug。
    /// </para>
    /// <para>
    /// 手法：把输入系统切到 <c>ProcessEventsManually</c>，用 <c>QueueStateEvent</c> + <c>InputSystem.Update()</c>
    /// 注入确定性的合成输入（不依赖真实按键/手柄，也不受原生后端干扰）。
    /// 这正是本工程调试时踩出来的可行姿势：**默认动态更新模式下 QueueStateEvent 对 ReadValue 不生效**。
    /// </para>
    /// </summary>
    [TestFixture]
    public class InputSystemBackendPlayModeTests
    {
        private InputActionAsset _asset;
        private Gamepad _pad;
        private InputSettings.UpdateMode _previousUpdateMode;

        private const string ActionsJson = @"{
    ""name"": ""PlayModeBackendTestActions"",
    ""maps"": [
        {
            ""name"": ""Gameplay"",
            ""id"": ""0000000000000000000000000000bb01"",
            ""actions"": [
                {
                    ""name"": ""Move"",
                    ""type"": ""Value"",
                    ""id"": ""0000000000000000000000000000bb02"",
                    ""expectedControlType"": ""Vector2"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": true
                },
                {
                    ""name"": ""Jump"",
                    ""type"": ""Button"",
                    ""id"": ""0000000000000000000000000000bb03"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                }
            ],
            ""bindings"": [
                {
                    ""name"": ""Left Stick"",
                    ""id"": ""0000000000000000000000000000bb04"",
                    ""path"": ""2DVector"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": true,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": ""up"",
                    ""id"": ""0000000000000000000000000000bb05"",
                    ""path"": ""<Gamepad>/leftStick/up"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""down"",
                    ""id"": ""0000000000000000000000000000bb06"",
                    ""path"": ""<Gamepad>/leftStick/down"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""left"",
                    ""id"": ""0000000000000000000000000000bb07"",
                    ""path"": ""<Gamepad>/leftStick/left"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""right"",
                    ""id"": ""0000000000000000000000000000bb08"",
                    ""path"": ""<Gamepad>/leftStick/right"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": """",
                    ""id"": ""0000000000000000000000000000bb09"",
                    ""path"": ""<Gamepad>/buttonSouth"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Jump"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                }
            ]
        },
        {
            ""name"": ""UI"",
            ""id"": ""0000000000000000000000000000bb0a"",
            ""actions"": [],
            ""bindings"": []
        }
    ],
    ""controlSchemes"": []
}";

        [SetUp]
        public void SetUp()
        {
            _previousUpdateMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;

            _asset = InputActionAsset.FromJson(ActionsJson);
            _pad = InputSystem.AddDevice<Gamepad>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
            _pad = null;

            if (_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(_asset);
                _asset = null;
            }

            InputSystem.settings.updateMode = _previousUpdateMode;
        }

        private InputSystemBackend CreateBackend()
        {
            var backend = new InputSystemBackend();
            backend.Attach(_asset);
            backend.Initialize();
            return backend;
        }

        /// <summary>把左摇杆推到指定位置并让输入系统更新一次。</summary>
        private void PushLeftStick(Vector2 value)
        {
            var state = new GamepadState();
            state.leftStick = value;
            InputSystem.QueueStateEvent(_pad, state);
            InputSystem.Update();
        }

        /// <summary>
        /// 🔴 真回归：对 <c>Vector2</c> 动作调用 <c>GetAxis</c>（= <c>ReadValue&lt;float&gt;()</c>）
        /// 在旧实现下会抛 <c>InvalidOperationException: Cannot read value of type 'Single' from composite ...</c>。
        /// EditMode 复现不出（见类注释），所以这条必须留在 PlayMode。
        /// </summary>
        [Test]
        public void ReadVector2_ReturnsDeviceValue_AndReadAxisDoesNotThrow()
        {
            var backend = CreateBackend();
            PushLeftStick(new Vector2(0f, 1f));

            // 正向：向量通道读得到真实值
            Assert.AreEqual(new Vector2(0f, 1f), backend.ReadVector2("Move"));

            // 回归：标量通道读向量动作 → 必须是 0，而不是抛异常
            Assert.DoesNotThrow(() => backend.ReadAxis("Move"));
            Assert.AreEqual(0f, backend.ReadAxis("Move"));

            // 反向：向量通道读按钮动作 → 也必须是零向量，而不是抛异常
            Assert.DoesNotThrow(() => backend.ReadVector2("Jump"));
            Assert.AreEqual(Vector2.zero, backend.ReadVector2("Jump"));
        }

        [Test]
        public void ApplyContexts_BlocksThenRestoresValues()
        {
            var backend = CreateBackend();
            PushLeftStick(new Vector2(0f, 1f));

            Assert.AreEqual(new Vector2(0f, 1f), backend.ReadVector2("Move"));
            Assert.AreEqual(2, backend.ActiveActionCount);

            // 只推 UI（该 map 里没有动作）→ Gameplay map 被禁用
            backend.ApplyContexts(new[] { new InputContext("UI", 100) });

            Assert.AreEqual(0, backend.ActiveActionCount);
            Assert.AreEqual(Vector2.zero, backend.ReadVector2("Move"));
            Assert.IsFalse(backend.ReadButton("Jump"));

            // 回到不限制 → 重新启用并按当前设备状态重算（Move 的 initialStateCheck = true）
            backend.ApplyContexts(Array.Empty<InputContext>());
            InputSystem.Update();

            Assert.AreEqual(2, backend.ActiveActionCount);
            Assert.AreEqual(new Vector2(0f, 1f), backend.ReadVector2("Move"), "取消阻断后应恢复读取真实输入");
        }

        [Test]
        public void ActionTriggered_FiresOnButtonPress()
        {
            var backend = CreateBackend();

            InputActionEvent received = default;
            int count = 0;
            backend.ActionTriggered += e => { received = e; count++; };

            // ⚠️ GamepadButton 是**顺序枚举**（South == 6），不是位标志 —— 必须用 WithButton 映射到状态位
            var pressed = new GamepadState().WithButton(GamepadButton.South);
            InputSystem.QueueStateEvent(_pad, pressed);
            InputSystem.Update();

            Assert.GreaterOrEqual(count, 1);
            Assert.AreEqual("Jump", received.ActionId);
            Assert.AreEqual(InputTriggerPhase.Performed, received.Phase);
            Assert.AreEqual(0, received.PlayerId);
        }
    }
}
