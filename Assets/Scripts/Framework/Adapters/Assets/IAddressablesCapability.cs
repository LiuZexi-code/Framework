using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Assets;
using UnityEngine;

namespace Framework.Adapters.Assets
{
    public interface IAddressablesCapability
    {
        /// <summary>
        /// 通过lable批量加载
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="lable">资源标签</param>
        /// <param name="onCompleted">全部资源加载完成时调用</param>
        /// <param name="callback">单个资源加载完成时调用</param>
        void LoadAsyncByLable<T>(string lable,Action<IAssetsHandle<T>> onCompleted, Action<T> callback = null) where T : class;
    }
}
