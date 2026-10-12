using System;
using System.Collections.Generic;
using Framework.Abstractions.Input;
using Framework.Adapters.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// <see cref="InputSystemBackend"/> 的单元测试（用内存里的 <c>InputActionAsset</c>，无需真实设备）。
/// <para>
/// 🔴 <b>诚实标注（重要）</b>：本项目 Play 实测发现过一个真 bug——对 <c>Vector2</c> 动作调用
/// <c>GetAxis</c>（即 <c>InputAction.ReadValue&lt;float&gt;()</c>）会抛
/// <c>InvalidOperationException: Cannot read value of type 'Single' from composite ...(value type 'Vector2')</c>。
/// </para>
/// <para>
/// 该异常<b>只能在 Play 模式下复现</b>：EditMode 下动作没有绑定到真实设备时
/// <c>InputAction.activeValueType</c> 为 <c>null</c>，<c>ReadValue&lt;T&gt;()</c> 直接返回 0 而不做类型校验
/// （已实测：EditMode 下即使加设备 + <c>ProcessEventsManually</c> + <c>InputSystem.Update()</c> 也不产生设备状态）。
/// 因此这里<b>不写</b>「ReadAxis 不抛异常」那类在 EditMode 下永不失败的假测试。
/// </para>
/// <para>
/// 取而代之：把引发该 bug 的<b>类型路由判据</b>（<see cref="InputSystemBackend.IsScalarValueType"/> /
/// <see cref="InputSystemBackend.IsVector2ValueType"/>）做成可单测的真判据——
/// 若有人再把 Vector2 当成标量通道读，下面的用例会失败。
/// Play 侧的端到端验证记录见 <c>INPUT_DESIGN.md</c>。
/// </para>
/// </summary>
public class InputSystemBackendTests
{
    private const string ActionsJson = @"{
    ""name"": ""BackendTestActions"",
    ""maps"": [
        {
            ""name"": ""Gameplay"",
            ""id"": ""0000000000000000000000000000aa01"",
            ""actions"": [
                {
                    ""name"": ""Move"",
                    ""type"": ""Value"",
                    ""id"": ""0000000000000000000000000000aa02"",
                    ""expectedControlType"": ""Vector2"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": true
                },
                {
                    ""name"": ""Jump"",
                    ""type"": ""Button"",
                    ""id"": ""0000000000000000000000000000aa03"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                }
            ],
            ""bindings"": [
                {
                    ""name"": ""WASD"",
                    ""id"": ""0000000000000000000000000000aa04"",
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
                    ""id"": ""0000000000000000000000000000aa05"",
                    ""path"": ""<Keyboard>/w"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""down"",
                    ""id"": ""0000000000000000000000000000aa06"",
                    ""path"": ""<Keyboard>/s"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""left"",
                    ""id"": ""0000000000000000000000000000aa07"",
                    ""path"": ""<Keyboard>/a"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""right"",
                    ""id"": ""0000000000000000000000000000aa08"",
                    ""path"": ""<Keyboard>/d"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Move"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": """",
                    ""id"": ""0000000000000000000000000000aa09"",
                    ""path"": ""<Keyboard>/space"",
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
            ""id"": ""0000000000000000000000000000aa0a"",
            ""actions"": [],
            ""bindings"": []
        }
    ],
    ""controlSchemes"": []
}";

    private InputActionAsset _asset;

    [TearDown]
    public void TearDown()
    {
        if (_asset != null)
        {
            UnityEngine.Object.DestroyImmediate(_asset);
            _asset = null;
        }
    }

    private InputSystemBackend CreateInitializedBackend()
    {
        _asset = InputActionAsset.FromJson(ActionsJson);

        var backend = new InputSystemBackend();
        backend.Attach(_asset);
        backend.Initialize();
        return backend;
    }

    [Test]
    public void Attach_ExposesAssetMapsAndResolvableActions()
    {
        var backend = CreateInitializedBackend();

        Assert.IsTrue(backend.HasAsset);
        Assert.AreEqual(2, _asset.actionMaps.Count);

        // 空上下文 = 不限制 → 所有 map 启用；UI map 是空的，所以只有 Gameplay 的 2 个动作可解析
        Assert.AreEqual(2, backend.ActiveActionCount);
        CollectionAssert.AreEquivalent(new[] { "Gameplay", "UI" }, backend.EnabledMapNames);
    }

    // ---------- 值类型路由（Play 那个 bug 的根因，真判据） ----------

    [Test]
    public void ValueTypeRouting_Vector2MustNotBeTreatedAsScalar()
    {
        // 若把 Vector2 动作当标量通道读，Play 下会抛 InvalidOperationException
        Assert.IsFalse(InputSystemBackend.IsScalarValueType(typeof(Vector2)));
        Assert.IsTrue(InputSystemBackend.IsVector2ValueType(typeof(Vector2)));

        Assert.IsTrue(InputSystemBackend.IsScalarValueType(typeof(float)));
        Assert.IsTrue(InputSystemBackend.IsScalarValueType(typeof(double)));
        Assert.IsFalse(InputSystemBackend.IsVector2ValueType(typeof(float)));
        Assert.IsFalse(InputSystemBackend.IsVector2ValueType(typeof(double)));

        Assert.IsTrue(InputSystemBackend.IsVector2ValueType(typeof(Vector3)));
        Assert.IsFalse(InputSystemBackend.IsScalarValueType(typeof(Vector3)));

        // 未知类型两个判据都为 false → 走「读不到」分支返回默认值
        Assert.IsFalse(InputSystemBackend.IsScalarValueType(typeof(string)));
        Assert.IsFalse(InputSystemBackend.IsVector2ValueType(typeof(string)));
    }

    [Test]
    public void ReadButton_OnButtonAction_ReturnsFalseWhenNotPressed()
    {
        var backend = CreateInitializedBackend();

        Assert.IsFalse(backend.ReadButton("Jump"));
        Assert.IsFalse(backend.ReadButtonDown("Jump"));
        Assert.IsFalse(backend.ReadButtonUp("Jump"));
    }

    [Test]
    public void UnknownAction_ReturnsEmptyValues()
    {
        var backend = CreateInitializedBackend();

        Assert.IsFalse(backend.ReadButton("Nope"));
        Assert.AreEqual(0f, backend.ReadAxis("Nope"));
        Assert.AreEqual(Vector2.zero, backend.ReadVector2("Nope"));
    }

    [Test]
    public void ContextsWithoutMatchingMap_BlockEveryAction()
    {
        var backend = CreateInitializedBackend();
        Assert.AreEqual(2, backend.ActiveActionCount);

        // 只推 UI（其 map 里没有任何动作）→ Gameplay map 被禁用 → 动作全不可解析
        backend.ApplyContexts(new[] { new InputContext("UI", 100) });

        Assert.AreEqual(0, backend.ActiveActionCount, "被上下文阻断的动作不应留在查找表里");
        CollectionAssert.AreEquivalent(new[] { "UI" }, backend.EnabledMapNames);
        Assert.AreEqual(0f, backend.ReadAxis("Move"));
        Assert.IsFalse(backend.ReadButton("Jump"));

        // 回到不限制（空集合）→ Gameplay 重新启用
        backend.ApplyContexts(Array.Empty<InputContext>());

        Assert.AreEqual(2, backend.ActiveActionCount);
        CollectionAssert.AreEquivalent(new[] { "Gameplay", "UI" }, backend.EnabledMapNames);
    }

    [Test]
    public void ContextMapping_CanRenameContextToMap()
    {
        _asset = InputActionAsset.FromJson(ActionsJson);

        var backend = new InputSystemBackend(new Dictionary<string, string> { { "Play", "Gameplay" } });
        backend.Attach(_asset);
        backend.Initialize();

        // 上下文名 "Play" 被映射到 "Gameplay" map
        backend.ApplyContexts(new[] { new InputContext("Play", 0) });

        Assert.AreEqual(2, backend.ActiveActionCount);
        CollectionAssert.AreEquivalent(new[] { "Gameplay" }, backend.EnabledMapNames);
    }

    [Test]
    public void Attach_Null_IsSafe()
    {
        var backend = new InputSystemBackend();
        backend.Attach(null);
        backend.Initialize();

        Assert.IsFalse(backend.HasAsset);
        Assert.AreEqual(0f, backend.ReadAxis("Move"));
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        var backend = CreateInitializedBackend();

        Assert.DoesNotThrow(() => backend.Dispose());
        Assert.DoesNotThrow(() => backend.Dispose());
        Assert.AreEqual(0f, backend.ReadAxis("Move"));
    }
}
