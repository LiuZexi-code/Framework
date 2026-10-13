using Game.RPG.Interaction;
using Game.RPG.Players;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 交互服务（状态机 / 范围判定 / 意图生命周期 / 目标失效清理）的单元测试。
/// <para>
/// 全部同步：本项目 UTF 版本不支持 <c>async Task</c> 测试方法。
/// </para>
/// <para>
/// 之所以能在 EditMode 里完整测：<see cref="InteractionService"/> 只依赖
/// <see cref="IPlayerLocomotion"/> 与 <see cref="IInteractable"/> 两个契约，
/// 替身可以精确控制「玩家在哪 / 移动是否被拒绝」与「目标是否存活」，
/// 不需要 NavMesh、不需要相机、也不需要输入设备。
/// </para>
/// </summary>
public class InteractionServiceTests
{
    /// <summary>假位移：<c>TryMoveTo</c> **不真的移动**（由测试手动改 Position 模拟走路过程）。</summary>
    private sealed class FakeLocomotion : IPlayerLocomotion
    {
        public Vector3 Position { get; set; }
        public bool IsMoving { get; private set; }

        /// <summary>true = 所有移动请求都失败（模拟不可达）。</summary>
        public bool RejectMoves { get; set; }

        public int MoveCalls;
        public int StopCalls;
        public Vector3 LastRequested;

        public bool TryMoveTo(Vector3 worldPoint)
        {
            MoveCalls++;
            LastRequested = worldPoint;
            if (RejectMoves) return false;

            IsMoving = true;
            return true;
        }

        public void Stop()
        {
            StopCalls++;
            IsMoving = false;
        }

        public bool TeleportTo(Vector3 worldPoint)
        {
            Position = worldPoint;
            return true;
        }
    }

    /// <summary>假可交互物（纯 C#，不是 UnityEngine.Object）。</summary>
    private sealed class FakeInteractable : IInteractable
    {
        public InteractionKind Kind { get; set; } = InteractionKind.Pickup;
        public string DisplayName { get; set; } = "测试物品";
        public float InteractionRange { get; set; } = 1.5f;
        public Vector3 ApproachPosition { get; set; }

        public int InteractCalls;

        public void Interact() => InteractCalls++;
    }

    private static (InteractionService service, FakeLocomotion loco) Create(Vector3 playerPosition = default)
    {
        var loco = new FakeLocomotion { Position = playerPosition };
        var service = new InteractionService();
        service.AttachPlayer(loco);
        return (service, loco);
    }

    // ---------------------------------------------------------------- 初始状态

    [Test]
    public void Initial_IsIdle_WithoutIntent()
    {
        var (service, _) = Create();

        Assert.AreEqual(InteractionState.Idle, service.State);
        Assert.IsFalse(service.Intent.HasValue);
        Assert.IsFalse(service.CanExecute);
        Assert.IsNull(service.Hover);
        Assert.AreEqual(string.Empty, service.ExecuteLabel);
    }

    // ---------------------------------------------------------------- 状态机

    [Test]
    public void OrderInteraction_OutOfRange_WalksThenBecomesReady()
    {
        var (service, loco) = Create(Vector3.zero);
        var target = new FakeInteractable { ApproachPosition = new Vector3(10f, 0f, 0f) };

        Assert.IsTrue(service.OrderInteraction(target));

        Assert.AreEqual(InteractionState.Moving, service.State);
        Assert.IsFalse(service.CanExecute, "还在路上，不应可执行");
        Assert.AreEqual("拾取", service.ExecuteLabel);
        Assert.AreEqual(1, loco.MoveCalls);
        Assert.AreEqual(new Vector3(10f, 0f, 0f), loco.LastRequested);

        // 模拟角色走到目标附近
        loco.Position = new Vector3(9f, 0.5f, 0f);   // y 不同也应忽略（按平面距离判定）
        service.Tick();

        Assert.AreEqual(InteractionState.Ready, service.State);
        Assert.IsTrue(service.CanExecute);
    }

    [Test]
    public void OrderInteraction_AlreadyInRange_DoesNotMove_AndIsReadyImmediately()
    {
        var (service, loco) = Create(new Vector3(9.5f, 0f, 0f));
        var target = new FakeInteractable { ApproachPosition = new Vector3(10f, 0f, 0f) };

        Assert.IsTrue(service.OrderInteraction(target));

        Assert.AreEqual(0, loco.MoveCalls, "已在范围内不该再走");
        Assert.AreEqual(1, loco.StopCalls, "应停住（避免残留的旧移动）");
        Assert.AreEqual(InteractionState.Ready, service.State);
    }

    [Test]
    public void OrderInteraction_Unreachable_ReturnsFalse_WithoutIntent()
    {
        var (service, loco) = Create();
        loco.RejectMoves = true;

        var target = new FakeInteractable { ApproachPosition = new Vector3(30f, 0f, 0f) };

        Assert.IsFalse(service.OrderInteraction(target));
        Assert.IsFalse(service.Intent.HasValue);
        Assert.AreEqual(InteractionState.Idle, service.State);
    }

    // ---------------------------------------------------------------- 执行

    [Test]
    public void TryExecute_InRange_InvokesTarget_AndClearsIntent()
    {
        var (service, loco) = Create(new Vector3(9.5f, 0f, 0f));
        var target = new FakeInteractable { ApproachPosition = new Vector3(10f, 0f, 0f) };

        service.OrderInteraction(target);
        Assert.IsTrue(service.CanExecute);

        Assert.IsTrue(service.TryExecute());

        Assert.AreEqual(1, target.InteractCalls, "应真正调用目标的 Interact");
        Assert.IsFalse(service.Intent.HasValue, "执行后应清空意图");
        Assert.AreEqual(InteractionState.Idle, service.State);
    }

    [Test]
    public void TryExecute_OutOfRange_ReturnsFalse_AndDoesNotInvoke()
    {
        var (service, _) = Create();
        var target = new FakeInteractable { ApproachPosition = new Vector3(10f, 0f, 0f) };

        service.OrderInteraction(target);   // 仍在 Moving

        Assert.IsFalse(service.TryExecute());
        Assert.AreEqual(0, target.InteractCalls);
        Assert.IsTrue(service.Intent.HasValue, "执行失败不应吞掉意图");
    }

    // ---------------------------------------------------------------- 意图生命周期

    [Test]
    public void OrderMoveTo_CancelsInteractionIntent_AndMoves()
    {
        var (service, loco) = Create();
        var target = new FakeInteractable { ApproachPosition = new Vector3(10f, 0f, 0f) };

        service.OrderInteraction(target);
        Assert.IsTrue(service.Intent.HasValue);

        Assert.IsTrue(service.OrderMoveTo(new Vector3(3f, 0f, 3f)));

        Assert.IsFalse(service.Intent.HasValue, "点地应放弃交互意图");
        Assert.AreEqual(InteractionState.Idle, service.State);
        Assert.AreEqual(2, loco.MoveCalls);
        Assert.AreEqual(new Vector3(3f, 0f, 3f), loco.LastRequested);
    }

    [Test]
    public void OrderMoveTo_Unreachable_ReturnsFalse()
    {
        var (service, loco) = Create();
        loco.RejectMoves = true;

        Assert.IsFalse(service.OrderMoveTo(new Vector3(99f, 0f, 99f)));
    }

    [Test]
    public void ClearIntent_ResetsState()
    {
        var (service, _) = Create();
        var target = new FakeInteractable { ApproachPosition = new Vector3(10f, 0f, 0f) };
        service.OrderInteraction(target);

        service.ClearIntent();

        Assert.IsFalse(service.Intent.HasValue);
        Assert.AreEqual(InteractionState.Idle, service.State);
        Assert.AreEqual(string.Empty, service.ExecuteLabel);
    }

    [Test]
    public void Tick_ClearsIntent_WhenTargetIsDestroyed()
    {
        // 用一个真实的 MonoBehaviour 目标，验证 Unity「伪 null」（已销毁但引用非 null）被正确处理
        var go = new GameObject("test_pickup");
        go.transform.position = new Vector3(10f, 0f, 0f);
        var pickup = go.AddComponent<PickupItem>();

        var (service, _) = Create();
        Assert.IsTrue(service.OrderInteraction(pickup));
        Assert.AreEqual(InteractionState.Moving, service.State);

        Object.DestroyImmediate(go);
        service.Tick();

        Assert.IsFalse(service.Intent.HasValue, "目标被销毁后应清理意图");
        Assert.AreEqual(InteractionState.Idle, service.State);
    }

    // ---------------------------------------------------------------- 事件与文案

    [Test]
    public void SetHover_RaisesChanged_OnlyWhenActuallyDifferent()
    {
        var (service, _) = Create();
        var raised = 0;
        service.Changed += () => raised++;

        var ground = new HoverInfo(InteractionKind.Ground, null, null, new Vector3(1f, 0f, 1f), true);
        service.SetHover(ground);
        Assert.AreEqual(1, raised);

        service.SetHover(ground);                       // 相同 → 不再通知
        Assert.AreEqual(1, raised);

        service.SetHover(null);
        Assert.AreEqual(2, raised);
        Assert.IsNull(service.Hover);
    }

    [Test]
    public void Changed_IsRaised_OnStateTransitions()
    {
        var (service, loco) = Create();
        var raised = 0;
        service.Changed += () => raised++;

        var target = new FakeInteractable { ApproachPosition = new Vector3(10f, 0f, 0f) };
        service.OrderInteraction(target);      // Idle → Moving
        Assert.AreEqual(1, raised);

        service.Tick();                        // 仍在 Moving → 不通知
        Assert.AreEqual(1, raised);

        loco.Position = new Vector3(9.5f, 0f, 0f);
        service.Tick();                        // Moving → Ready
        Assert.AreEqual(2, raised);
    }

    [Test]
    public void LabelOf_MapsAllKinds()
    {
        Assert.AreEqual("拾取", InteractionService.LabelOf(InteractionKind.Pickup));
        Assert.AreEqual("对话", InteractionService.LabelOf(InteractionKind.Npc));
        Assert.AreEqual("攻击", InteractionService.LabelOf(InteractionKind.Enemy));
        Assert.AreEqual(string.Empty, InteractionService.LabelOf(InteractionKind.Ground));
        Assert.AreEqual(string.Empty, InteractionService.LabelOf(InteractionKind.None));
    }
}
