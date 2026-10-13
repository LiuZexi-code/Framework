using System.Collections;
using System.Collections.Generic;
using Game.RPG.Players;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEngine.TestTools;

/// <summary>
/// <see cref="PlayerMotor"/> 与 <see cref="ClickMarkerView"/> 的 **PlayMode** 回归测试。
/// <para>
/// 为什么要 PlayMode：移动依赖运行期的 NavMesh + <c>NavMeshAgent</c>（EditMode 下代理不会真正寻路）。
/// </para>
/// <para>
/// 🔴 **两条必须遵守的约束**（都是实测踩出来的）：
/// <list type="number">
/// <item>
/// 框架的 <c>FrameworkStartEntry</c>（<c>RuntimeInitializeOnLoadMethod</c>）在 PlayMode 测试里**同样会启动**，
/// 并把场景切到入口场景（RPGScene）→ **普通场景物体全部被销毁**。所以测试里创建的一切都必须
/// <c>DontDestroyOnLoad</c>，否则会随机出现 <c>MissingReferenceException</c>。
/// </item>
/// <item>
/// 烘焙必须用 <c>CollectObjects.Children</c>（只收自己的子物体）。用 <c>All</c> 会去收集当前场景里
/// 静态批处理产生的 <c>Combined Mesh (root: scene)</c>，而它**不允许读访问** → 直接报
/// <c>RuntimeNavMeshBuilder: ... does not allow read access</c>（且被 TestRunner 当成未处理错误而失败）。
/// </item>
/// </list>
/// </para>
/// <para>
/// 另：绕障/不可达这类**与相机朝向无关**的行为，直接调 <see cref="PlayerMotor.TryMoveTo"/> 或
/// <c>NavMeshAgent.CalculatePath</c> 断言，**不要绕屏幕坐标**（绕屏幕点会引入视口/相机朝向依赖）。
/// </para>
/// </summary>
public class PlayerMotorPlayModeTests
{
    /// <summary>整个测试场地远离原点，避免和当前打开场景的地面/障碍重叠。</summary>
    private static readonly Vector3 ArenaOrigin = new Vector3(500f, 0f, 500f);

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        // 用 DestroyImmediate：这些对象是 DDOL 的，必须**在本帧内**清干净，
        // 否则下一个用例会和上一个用例残留的场地/代理互相干扰。
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null)
                Object.DestroyImmediate(_spawned[i]);

        _spawned.Clear();
    }

    private GameObject Track(GameObject go)
    {
        Object.DontDestroyOnLoad(go);   // ★ 必须：框架启动会切场景，非 DDOL 物体会被销毁
        _spawned.Add(go);
        return go;
    }

    private static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

    private static float PathLength(NavMeshPath path)
    {
        var corners = path.corners;
        var total = 0f;
        for (int i = 1; i < corners.Length; i++)
            total += Vector3.Distance(corners[i - 1], corners[i]);
        return total;
    }

    private static IEnumerator WaitForAgent(PlayerMotor motor, float timeoutSeconds = 10f)
    {
        var elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            if (motor != null && motor.Agent != null && motor.Agent.isOnNavMesh) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// 搭场地：40×40 地面 + carving 障碍（挡在 z 轴负方向），**在运行时烘焙 NavMesh**，
    /// 返回站在场地原点的 <see cref="PlayerMotor"/>。
    /// </summary>
    private PlayerMotor BuildArena()
    {
        var root = Track(new GameObject("[TestArena]"));

        var ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        ground.transform.SetParent(root.transform);
        ground.transform.position = ArenaOrigin + new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(40f, 1f, 40f);

        // 障碍：x∈[-3,3]、z∈[-10.5,-7.5]（相对场地原点）—— 正好挡住 (0,0) → (0,-15)
        var obstacle = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        obstacle.transform.SetParent(root.transform);
        obstacle.transform.position = ArenaOrigin + new Vector3(0f, 0.75f, -9f);
        obstacle.transform.localScale = new Vector3(6f, 1.5f, 3f);
        var carving = obstacle.AddComponent<NavMeshObstacle>();
        carving.carving = true;

        // 烘焙面挂在 root 上，只收 root 的子物体（地面 + 障碍）
        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.ignoreNavMeshObstacle = false;
        surface.BuildNavMesh();

        var playerGo = Track(new GameObject("[TestPlayer]"));
        playerGo.transform.position = ArenaOrigin;
        var agent = playerGo.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.8f;
        agent.baseOffset = 1f;

        return playerGo.AddComponent<PlayerMotor>();
    }

    [UnityTest]
    public IEnumerator Arena_IsReady_AgentOnNavMesh()
    {
        var motor = BuildArena();
        yield return WaitForAgent(motor);

        Assert.IsTrue(motor.Agent.isOnNavMesh, "代理应已贴在 NavMesh 上（否则后面的用例全无意义）");
        Assert.IsFalse(motor.HasDestination, "初始不应有目标点");
    }

    [UnityTest]
    public IEnumerator TryMoveTo_UnreachablePoint_ReturnsFalse()
    {
        var motor = BuildArena();
        yield return WaitForAgent(motor);

        // 远离地面（场地只有 40×40）→ NavMesh.SamplePosition 失败
        Assert.IsFalse(motor.TryMoveTo(ArenaOrigin + new Vector3(300f, 0f, 300f)), "场地外应判定为不可达");
        Assert.IsFalse(motor.HasDestination, "不可达时不应记录目标点");
    }

    [UnityTest]
    public IEnumerator TryMoveTo_ClearPath_WalksAndArrives()
    {
        var motor = BuildArena();
        yield return WaitForAgent(motor);

        var target = ArenaOrigin + new Vector3(12f, 0f, 0f);   // 无障碍方向
        Assert.IsTrue(motor.TryMoveTo(target), "无障碍方向应接受指令");
        Assert.IsTrue(motor.HasDestination);

        var elapsed = 0f;
        while (motor.HasDestination && elapsed < 10f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        Assert.IsFalse(motor.HasDestination, "应在超时前到达并清空目标");
        Assert.Less(Vector3.Distance(Flat(motor.transform.position), Flat(motor.Destination)), 1f,
            "到达后应停在目标点附近");
    }

    [UnityTest]
    public IEnumerator CalculatePath_BlockedByObstacle_TakesDetour()
    {
        var motor = BuildArena();
        yield return WaitForAgent(motor);

        var origin = Flat(motor.transform.position);
        var target = ArenaOrigin + new Vector3(0f, 0f, -15f);
        var straightDistance = Vector3.Distance(origin, Flat(target));

        var path = new NavMeshPath();
        Assert.IsTrue(motor.Agent.CalculatePath(target, path), "应能算出到目标点的路径");
        Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);

        var length = PathLength(path);
        Assert.GreaterOrEqual(path.corners.Length, 3,
            $"直线被 carving 障碍挡住时应产生折点（实际 {path.corners.Length} 个）");
        Assert.Greater(length, straightDistance + 1f,
            $"绕行路径({length:F1}m)应明显长于直线距离({straightDistance:F1}m)");

        // 且路径上不应有点落在障碍内部（障碍矩形 x∈[-3,3]、z∈[-10.5,-7.5]，相对 ArenaOrigin）
        for (int i = 0; i < path.corners.Length; i++)
        {
            var local = path.corners[i] - ArenaOrigin;
            var insideObstacle = Mathf.Abs(local.x) < 3f && local.z < -7.5f && local.z > -10.5f;
            Assert.IsFalse(insideObstacle, $"路径折点 {local} 落在障碍内部");
        }
    }

    [UnityTest]
    public IEnumerator Stop_ClearsDestination()
    {
        var motor = BuildArena();
        yield return WaitForAgent(motor);

        Assert.IsTrue(motor.TryMoveTo(ArenaOrigin + new Vector3(0f, 0f, 12f)), "应接受指令");
        Assert.IsTrue(motor.HasDestination);

        motor.Stop();

        Assert.IsFalse(motor.HasDestination, "Stop 后应清空目标");
    }

    [UnityTest]
    public IEnumerator TeleportTo_MovesOntoNavMesh()
    {
        var motor = BuildArena();
        yield return WaitForAgent(motor);

        Assert.IsTrue(motor.TeleportTo(ArenaOrigin + new Vector3(5f, 0f, 5f)));
        Assert.Less(Vector3.Distance(Flat(motor.transform.position), ArenaOrigin + new Vector3(5f, 0f, 5f)), 0.5f);
    }

    [UnityTest]
    public IEnumerator ClickMarkerView_Play_ShowsThenHidesItself()
    {
        var marker = Track(new GameObject("[TestMarker]"));
        marker.transform.localScale = new Vector3(1.2f, 0.02f, 1.2f);
        var view = marker.AddComponent<ClickMarkerView>();
        marker.SetActive(false);

        var worldPosition = ArenaOrigin + new Vector3(3f, 0f, 4f);
        view.Play(worldPosition);

        Assert.IsTrue(marker.activeSelf, "Play 后标记应显示");
        Assert.Less(Vector3.Distance(marker.transform.position, worldPosition + Vector3.up * 0.02f), 0.01f,
            "标记应出现在落点上");

        var elapsed = 0f;
        while (marker != null && marker.activeSelf && elapsed < 5f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        Assert.IsNotNull(marker, "标记对象不应在动画期间被销毁");
        Assert.IsFalse(marker.activeSelf, "动画播完后应自行隐藏（复用设计：不能一直可见）");
    }
}
