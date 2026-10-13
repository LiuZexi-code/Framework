using Game.RPG.Cursors;
using Game.RPG.Interaction;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 软件光标「悬停信息 → 图标状态」映射规则的单元测试（A6）。
/// <para>
/// 这 6 个状态是需求规格第 9 条的明确要求，且判定里有**优先级**（不可达优先于具体类型），
/// 属于「容易写反、写反了只有肉眼看才发现的」逻辑 → 专门固化在这里。
/// </para>
/// <para><see cref="CursorIconResolver"/> 是纯函数（不碰 Unity 对象、不读输入），所以可以完整断言。</para>
/// </summary>
public class CursorIconResolverTests
{
    private static HoverInfo Hover(InteractionKind kind, bool reachable)
        => new HoverInfo(kind, "测试目标", null, Vector3.zero, reachable);

    [Test]
    public void Resolve_NullHover_IsDefault()
    {
        Assert.AreEqual(CursorIcon.Default, CursorIconResolver.Resolve(null));
    }

    [Test]
    public void Resolve_KindNone_IsDefault()
    {
        // 驱动在「悬停 UI / 没打中任何东西」时用 Kind=None 表达
        Assert.AreEqual(CursorIcon.Default, CursorIconResolver.Resolve(Hover(InteractionKind.None, true)));
    }

    [Test]
    public void Resolve_Ground_IsGround()
    {
        Assert.AreEqual(CursorIcon.Ground, CursorIconResolver.Resolve(Hover(InteractionKind.Ground, true)));
    }

    [Test]
    public void Resolve_Pickup_IsPickup()
    {
        Assert.AreEqual(CursorIcon.Pickup, CursorIconResolver.Resolve(Hover(InteractionKind.Pickup, true)));
    }

    [Test]
    public void Resolve_Npc_IsNpc()
    {
        Assert.AreEqual(CursorIcon.Npc, CursorIconResolver.Resolve(Hover(InteractionKind.Npc, true)));
    }

    [Test]
    public void Resolve_Enemy_IsEnemy()
    {
        Assert.AreEqual(CursorIcon.Enemy, CursorIconResolver.Resolve(Hover(InteractionKind.Enemy, true)));
    }

    [Test]
    public void Resolve_UnreachableEnemy_IsUnreachable_NotEnemy()
    {
        // 优先级：走不过去时不该显示「攻击」图标误导玩家
        Assert.AreEqual(CursorIcon.Unreachable, CursorIconResolver.Resolve(Hover(InteractionKind.Enemy, false)));
    }

    [Test]
    public void Resolve_UnreachablePickup_IsUnreachable_NotPickup()
    {
        Assert.AreEqual(CursorIcon.Unreachable, CursorIconResolver.Resolve(Hover(InteractionKind.Pickup, false)));
    }

    [Test]
    public void Resolve_UnreachableGround_IsUnreachable_NotGround()
    {
        Assert.AreEqual(CursorIcon.Unreachable, CursorIconResolver.Resolve(Hover(InteractionKind.Ground, false)));
    }

    [Test]
    public void Settings_Get_MapsEveryState()
    {
        // 6 个槽位的映射必须一一对应（配错槽位时 UI 会显示错误的图标）
        var made = new System.Collections.Generic.List<Object>();
        var so = ScriptableObject.CreateInstance<RpgCursorSettingsSO>();
        made.Add(so);

        try
        {
            so.defaultIcon     = MakeSprite("d", made);
            so.groundIcon      = MakeSprite("g", made);
            so.pickupIcon      = MakeSprite("p", made);
            so.npcIcon         = MakeSprite("n", made);
            so.enemyIcon       = MakeSprite("e", made);
            so.unreachableIcon = MakeSprite("u", made);

            Assert.AreSame(so.defaultIcon,     so.Get(CursorIcon.Default));
            Assert.AreSame(so.groundIcon,      so.Get(CursorIcon.Ground));
            Assert.AreSame(so.pickupIcon,      so.Get(CursorIcon.Pickup));
            Assert.AreSame(so.npcIcon,         so.Get(CursorIcon.Npc));
            Assert.AreSame(so.enemyIcon,       so.Get(CursorIcon.Enemy));
            Assert.AreSame(so.unreachableIcon, so.Get(CursorIcon.Unreachable));
        }
        finally
        {
            for (int i = 0; i < made.Count; i++) Object.DestroyImmediate(made[i]);
        }
    }

    private static Sprite MakeSprite(string name, System.Collections.Generic.List<Object> track)
    {
        var tex = new Texture2D(2, 2);
        track.Add(tex);

        var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        sprite.name = name;
        track.Add(sprite);
        return sprite;
    }
}
