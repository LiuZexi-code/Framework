using UnityEngine;

namespace Game.RPG.Interaction
{
    /// <summary>
    /// NPC：交互后应开启对话（对话系统在后续阶段接入）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcInteractable : Interactable
    {
        [Tooltip("对话图 id（对应对话配置；对话阶段接入）")]
        [SerializeField] private string dialogueId = "Dialog_Placeholder";

        public string DialogueId => dialogueId;

        public override InteractionKind Kind => InteractionKind.Npc;

        public override void Interact()
        {
            Debug.Log($"[Npc] 与 {DisplayName} 对话（{dialogueId}）—— 对话系统待后续阶段接入");
        }
    }
}
