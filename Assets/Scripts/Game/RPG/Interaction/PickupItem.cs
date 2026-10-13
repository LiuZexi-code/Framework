using UnityEngine;

namespace Game.RPG.Interaction
{
    /// <summary>
    /// 可拾取物品。
    /// <para>
    /// ⚠️ A5 阶段只打通「识别 → 走近 → 按钮 → 执行」链路，<see cref="Interact"/> **刻意不改任何状态**
    /// （不销毁、不入包）—— 真正的拾取要等「物品 / 背包」阶段接入，否则就是假行为。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PickupItem : Interactable
    {
        [Tooltip("物品 id（对应物品定义表；背包阶段接入）")]
        [SerializeField] private string itemId = "Item_Placeholder";

        [Tooltip("数量")]
        [SerializeField] private int count = 1;

        public string ItemId => itemId;

        public int Count => count;

        public override InteractionKind Kind => InteractionKind.Pickup;

        public override void Interact()
        {
            Debug.Log($"[Pickup] 拾取 {DisplayName}（{itemId} x{count}）—— 入包逻辑待「物品/背包」阶段接入");
        }
    }
}
