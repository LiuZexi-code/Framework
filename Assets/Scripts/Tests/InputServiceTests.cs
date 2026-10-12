using System;
using System.Collections.Generic;
using Framework.Abstractions.Input;
using Framework.Abstractions.Logging;
using Framework.Adapters.Input;
using NUnit.Framework;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

/// <summary>
/// 输入服务（上下文栈 / 查询转发 / 事件转发 / 上下文阻断）与 MockInputService 的单元测试。
/// <para>全部为同步用例：本项目 UTF 版本不支持 <c>async Task</c> 测试方法。</para>
/// </summary>
public class InputServiceTests
{
    /// <summary>可编程假后端：记录 ApplyContexts 调用，并可模拟「按动作所属上下文过滤查询」。</summary>
    private sealed class FakeBackend : IInputBackend
    {
        /// <summary>每次 ApplyContexts 的上下文 id 快照（**必须拷贝**：服务会复用传入的容器）。</summary>
        public readonly List<List<string>> Applied = new List<List<string>>();

        private readonly Dictionary<string, string> _actionContext = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> _pressed = new Dictionary<string, bool>();
        private List<string> _active = new List<string>();

        public bool Initialized;
        public bool Disposed;
        public int DisposeCount;

        public event Action<InputActionEvent> ActionTriggered;

        public void Map(string actionId, string contextId) => _actionContext[actionId] = contextId;

        public void Press(string actionId, bool pressed) => _pressed[actionId] = pressed;

        public void Initialize() => Initialized = true;

        public void ApplyContexts(IReadOnlyList<InputContext> activeContexts)
        {
            var ids = new List<string>();
            if (activeContexts != null)
                for (int i = 0; i < activeContexts.Count; i++)
                    ids.Add(activeContexts[i].ContextId);

            Applied.Add(ids);
            _active = ids;
        }

        public void Tick(float deltaTime) { }

        public bool ReadButton(string actionId, int playerId = 0)
        {
            if (!_pressed.TryGetValue(actionId, out var pressed) || !pressed) return false;

            // 空上下文集合 = 不限制（与框架约定一致）
            if (_active.Count == 0) return true;
            if (!_actionContext.TryGetValue(actionId, out var contextId)) return true;

            return _active.Contains(contextId);
        }

        public bool ReadButtonDown(string actionId, int playerId = 0) => ReadButton(actionId, playerId);

        public bool ReadButtonUp(string actionId, int playerId = 0) => false;

        public float ReadAxis(string actionId, int playerId = 0) => actionId == "Move" ? 0.5f : 0f;

        public Vector2 ReadVector2(string actionId, int playerId = 0)
            => actionId == "Move" ? new Vector2(1f, -1f) : default;

        public void Raise(InputActionEvent e) => ActionTriggered?.Invoke(e);

        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public readonly List<string> Warnings = new List<string>();

        public LogLevel Level { get; set; }

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception exception = null) { }
    }

    private static InputService Create(FakeBackend backend, ILogger logger = null)
        => new InputService(backend, logger);

    // ---------- 上下文：Push / Pop ----------

    [Test]
    public void Push_And_Pop_Context_TogglesActiveState()
    {
        var input = Create(new FakeBackend());

        Assert.IsFalse(input.IsContextActive("Gameplay"));

        input.PushContext("Gameplay");
        Assert.IsTrue(input.IsContextActive("Gameplay"));
        Assert.AreEqual(1, input.ActiveContexts.Count);

        input.PopContext("Gameplay");
        Assert.IsFalse(input.IsContextActive("Gameplay"));
        Assert.AreEqual(0, input.ActiveContexts.Count);
    }

    [Test]
    public void HigherPriorityContext_SuppressesLowerPriorityOnes()
    {
        var input = Create(new FakeBackend());

        input.PushContext("Gameplay", 0);
        Assert.IsTrue(input.IsContextActive("Gameplay"));

        input.PushContext("UI", 100);
        Assert.IsTrue(input.IsContextActive("UI"));
        Assert.IsFalse(input.IsContextActive("Gameplay"), "高优先级上下文应独占有效集");
        Assert.AreEqual(1, input.ActiveContexts.Count);

        input.PopContext("UI");
        Assert.IsTrue(input.IsContextActive("Gameplay"), "Pop 后应回落到次高优先级");
    }

    [Test]
    public void SamePriorityContexts_Coexist()
    {
        var input = Create(new FakeBackend());

        input.PushContext("Vehicle", 5);
        input.PushContext("Gameplay", 5);

        Assert.IsTrue(input.IsContextActive("Vehicle"));
        Assert.IsTrue(input.IsContextActive("Gameplay"));
        Assert.AreEqual(2, input.ActiveContexts.Count);
    }

    [Test]
    public void DuplicatePush_RequiresEqualNumberOfPops()
    {
        var input = Create(new FakeBackend());

        input.PushContext("UI", 10);
        input.PushContext("UI", 10);

        input.PopContext("UI");
        Assert.IsTrue(input.IsContextActive("UI"), "推入两次应只抵消一次");

        input.PopContext("UI");
        Assert.IsFalse(input.IsContextActive("UI"));
    }

    [Test]
    public void Pop_UnknownContext_WarnsButDoesNotThrow()
    {
        var logger = new RecordingLogger();
        var input = Create(new FakeBackend(), logger);

        Assert.DoesNotThrow(() => input.PopContext("Never"));

        Assert.AreEqual(1, logger.Warnings.Count);
        StringAssert.Contains("Never", logger.Warnings[0]);
    }

    [Test]
    public void Push_EmptyContextId_IsIgnoredWithWarning()
    {
        var logger = new RecordingLogger();
        var input = Create(new FakeBackend(), logger);

        input.PushContext(null);
        input.PushContext(string.Empty);

        Assert.AreEqual(0, input.ActiveContexts.Count);
        Assert.AreEqual(2, logger.Warnings.Count);
    }

    // ---------- 上下文 → 后端 ----------

    [Test]
    public void EmptyStack_AppliesUnrestrictedEmptySet()
    {
        var backend = new FakeBackend();
        Create(backend);

        Assert.AreEqual(1, backend.Applied.Count, "构造后应通知后端一次（空 = 不限制）");
        Assert.AreEqual(0, backend.Applied[0].Count);
        Assert.IsTrue(backend.Initialized);
    }

    [Test]
    public void ApplyContexts_IsCalledOnlyWhenEffectiveSetChanges()
    {
        var backend = new FakeBackend();
        var input = Create(backend);

        Assert.AreEqual(1, backend.Applied.Count);

        input.PushContext("A");
        Assert.AreEqual(2, backend.Applied.Count);

        input.PushContext("A");   // 有效集未变 → 不应重复通知
        Assert.AreEqual(2, backend.Applied.Count);

        input.PopContext("A");    // 仍剩一份引用 → 有效集仍未变
        Assert.AreEqual(2, backend.Applied.Count);

        input.PopContext("A");    // 真正移除 → 回到空集
        Assert.AreEqual(3, backend.Applied.Count);
        Assert.AreEqual(0, backend.Applied[2].Count);
    }

    [Test]
    public void EffectiveSet_IsSentToBackend()
    {
        var backend = new FakeBackend();
        var input = Create(backend);

        input.PushContext("Gameplay", 0);
        input.PushContext("UI", 100);

        var last = backend.Applied[backend.Applied.Count - 1];
        Assert.AreEqual(1, last.Count);
        Assert.AreEqual("UI", last[0]);
    }

    // ---------- 上下文阻断（端到端） ----------

    [Test]
    public void ContextBlocking_UiContextSuppressesGameplayAction()
    {
        var backend = new FakeBackend();
        backend.Map("Attack", "Gameplay");
        backend.Press("Attack", true);

        var input = Create(backend);

        // 空上下文 = 不限制 → 可读到
        Assert.IsTrue(input.IsPressed("Attack"));

        input.PushContext("Gameplay", 0);
        Assert.IsTrue(input.IsPressed("Attack"));

        input.PushContext("UI", 100);
        Assert.IsFalse(input.IsPressed("Attack"), "UI 上下文激活时应阻断玩法动作");

        input.PopContext("UI");
        Assert.IsTrue(input.IsPressed("Attack"), "UI 关闭后玩法动作应恢复");
    }

    // ---------- 查询转发 ----------

    [Test]
    public void Queries_AreForwardedToBackend()
    {
        var backend = new FakeBackend();
        backend.Press("Jump", true);
        var input = Create(backend);

        Assert.IsTrue(input.IsPressed("Jump"));
        Assert.IsTrue(input.WasPressedThisFrame("Jump"));
        Assert.IsFalse(input.WasReleasedThisFrame("Jump"));
        Assert.IsFalse(input.IsPressed("Missing"));

        Assert.AreEqual(0.5f, input.GetAxis("Move"), 0.0001f);
        Assert.AreEqual(new Vector2(1f, -1f), input.GetVector2("Move"));
        Assert.AreEqual(0f, input.GetAxis("Missing"));
    }

    // ---------- 事件转发 ----------

    [Test]
    public void ActionTriggered_IsForwardedFromBackend()
    {
        var backend = new FakeBackend();
        var input = Create(backend);

        InputActionEvent received = default;
        int count = 0;
        input.ActionTriggered += e => { received = e; count++; };

        backend.Raise(new InputActionEvent("Jump", 0, InputTriggerPhase.Performed, 1f));

        Assert.AreEqual(1, count);
        Assert.AreEqual("Jump", received.ActionId);
        Assert.AreEqual(InputTriggerPhase.Performed, received.Phase);
        Assert.AreEqual(1f, received.Scalar);
    }

    [Test]
    public void ActionTriggered_OneBadSubscriberDoesNotBlockOthers()
    {
        var backend = new FakeBackend();
        var input = Create(backend);

        int hits = 0;
        input.ActionTriggered += e => { throw new InvalidOperationException("boom"); };
        input.ActionTriggered += e => hits++;

        Assert.DoesNotThrow(() => backend.Raise(new InputActionEvent("Jump", 0, InputTriggerPhase.Performed)));

        Assert.AreEqual(1, hits, "坏订阅者不应影响其它订阅者");
    }

    // ---------- 释放 ----------

    [Test]
    public void Dispose_DisposesBackend_AndQueriesReturnEmpty()
    {
        var backend = new FakeBackend();
        backend.Press("Jump", true);
        var input = Create(backend);

        input.PushContext("Gameplay");
        Assert.IsTrue(input.IsPressed("Jump"));

        input.Dispose();
        input.Dispose();   // 幂等

        Assert.AreEqual(1, backend.DisposeCount);
        Assert.IsTrue(backend.Disposed);
        Assert.IsFalse(input.IsPressed("Jump"));
        Assert.AreEqual(0f, input.GetAxis("Move"));
        Assert.AreEqual(0, input.ActiveContexts.Count);
    }

    // ---------- MockInputService ----------

    [Test]
    public void Mock_ButtonState_And_FrameFlags()
    {
        var mock = new MockInputService();

        Assert.IsFalse(mock.IsPressed("Jump"));

        mock.SetButton("Jump", true);
        Assert.IsTrue(mock.IsPressed("Jump"));
        Assert.IsTrue(mock.WasPressedThisFrame("Jump"));
        Assert.IsFalse(mock.WasReleasedThisFrame("Jump"));

        mock.ClearFrameFlags();
        Assert.IsTrue(mock.IsPressed("Jump"), "按住状态跨帧保持");
        Assert.IsFalse(mock.WasPressedThisFrame("Jump"), "边缘标记应被清空");

        mock.SetButton("Jump", false);
        Assert.IsFalse(mock.IsPressed("Jump"));
        Assert.IsTrue(mock.WasReleasedThisFrame("Jump"));
    }

    [Test]
    public void Mock_AxisAndVector()
    {
        var mock = new MockInputService();

        Assert.AreEqual(0f, mock.GetAxis("Move"));
        Assert.AreEqual(default(Vector2), mock.GetVector2("Move"));

        mock.SetAxis("Move", 0.75f);
        mock.SetVector2("Move", new Vector2(0f, 1f));

        Assert.AreEqual(0.75f, mock.GetAxis("Move"), 0.0001f);
        Assert.AreEqual(new Vector2(0f, 1f), mock.GetVector2("Move"));
    }

    [Test]
    public void Mock_Raise_TriggersEvent()
    {
        var mock = new MockInputService();

        InputActionEvent received = default;
        mock.ActionTriggered += e => received = e;

        mock.Raise("Attack", InputTriggerPhase.Canceled, 0f, default, 3);

        Assert.AreEqual("Attack", received.ActionId);
        Assert.AreEqual(InputTriggerPhase.Canceled, received.Phase);
        Assert.AreEqual(3, received.PlayerId);
    }

    [Test]
    public void Mock_TracksContexts()
    {
        var mock = new MockInputService();

        mock.PushContext("Gameplay");
        mock.PushContext("UI");
        mock.PushContext("UI");   // 重复推入不叠加

        Assert.AreEqual(2, mock.ActiveContexts.Count);
        Assert.IsTrue(mock.IsContextActive("UI"));

        mock.PopContext("UI");
        Assert.IsFalse(mock.IsContextActive("UI"));
    }
}
