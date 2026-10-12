using System;
using System.Collections.Generic;
using Framework.Abstractions.Events;
using Framework.Abstractions.UI;
using Framework.Adapters.UI;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;


namespace Framework.UI
{
    /// <summary>
    /// View 基类：负责订阅管理、显隐、生命周期。
    /// 子类只关心“订阅什么事件”和“怎么显示”，切勿在钩子里再次调用自身（否则递归）。
    /// </summary>
    public abstract class UIPanelBase : MonoBehaviour, IUIPanel,IUIPanelLifecycle
    {
        protected IEventBus EventBus { get; private set; }
        protected ILogger Logger { get; private set; }

        private readonly List<IEventSubscription> _subscriptions = new();
        private bool _initialized;

        public bool IsVisible => gameObject.activeSelf;

        // ---- 显式实现：只有通过 IUIPanelLifecycle 引用才能调到 ----
        void IUIPanelLifecycle.Create(IEventBus bus, ILogger logger) => OnCreate(bus, logger);
        void IUIPanelLifecycle.Show()   { if (!gameObject.activeSelf) gameObject.SetActive(true);  OnShow(); }
        void IUIPanelLifecycle.Hide()   { if (gameObject.activeSelf)  gameObject.SetActive(false); OnHide(); }
        void IUIPanelLifecycle.Destroy()=> OnDestroyPanel();

        // ---- 子类只 override 这些钩子（基类为空实现）----
        protected virtual void OnCreate(IEventBus eventBus, ILogger logger)
        {
            if (_initialized) return;
            _initialized = true;

            EventBus = eventBus;
            Logger = logger;
        }

        protected virtual void OnShow() { }

        protected virtual void OnHide() { }

        protected virtual void OnDestroyPanel()
        {
            foreach (var sub in _subscriptions)
                sub?.Dispose();
            _subscriptions.Clear();
        }

        /// <summary>订阅事件，并在面板销毁时自动回收（子类优先用这个）。</summary>
        protected IEventSubscription Subscribe<T>(Action<T> handler)
        {
            if (EventBus == null || handler == null) return null;
            var sub = EventBus.Subscribe(handler);
            _subscriptions.Add(sub);
            return sub;
        }
    }
}