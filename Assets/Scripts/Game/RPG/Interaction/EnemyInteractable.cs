using UnityEngine;

namespace Game.RPG.Interaction
{
    /// <summary>
    /// 敌人：交互 = 攻击（战斗系统在后续阶段接入）。
    /// <para>交互半径默认取「攻击距离」，所以走近到攻击距离内按钮才可用。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyInteractable : Interactable
    {
        [Tooltip("敌人 id（对应敌人定义表；战斗阶段接入）")]
        [SerializeField] private string enemyId = "Enemy_Placeholder";

        public string EnemyId => enemyId;

        public override InteractionKind Kind => InteractionKind.Enemy;

        public override void Interact()
        {
            Debug.Log($"[Enemy] 攻击 {DisplayName}（{enemyId}）—— 战斗结算待后续阶段接入");
        }
    }
}
