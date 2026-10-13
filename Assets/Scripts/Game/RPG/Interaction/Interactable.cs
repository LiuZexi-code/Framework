using UnityEngine;

namespace Game.RPG.Interaction
{
    /// <summary>
    /// 可交互物基类：挂在场景物体上，提供「种类 / 显示名 / 交互半径 / 站位」，
    /// 并把「执行」留给子类。
    /// <para>
    /// 识别方式：物体的碰撞体所在层为 <c>Interactable</c>，且其自身或父级上挂有本组件
    /// （驱动用 <c>GetComponentInParent&lt;Interactable&gt;()</c> 查找，所以碰撞体可以在子物体上）。
    /// </para>
    /// </summary>
    public abstract class Interactable : MonoBehaviour, IInteractable
    {
        [Tooltip("显示名（留空则用物体名）")]
        [SerializeField] private string displayName;

        [Tooltip("交互半径（米）：角色进入该距离即可执行")]
        [SerializeField] private float interactionRange = 1.8f;

        [Tooltip("角色交互时的站位点；留空 = 用自身位置")]
        [SerializeField] private Transform approachPoint;

        public abstract InteractionKind Kind { get; }

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public float InteractionRange => interactionRange;

        public Vector3 ApproachPosition => approachPoint != null ? approachPoint.position : transform.position;

        /// <summary>执行交互。子类实现（A5 阶段先打日志，真正的行为在后续阶段接入）。</summary>
        public abstract void Interact();
    }
}
