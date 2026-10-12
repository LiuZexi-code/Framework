using System.Collections;
using UnityEngine;

namespace Framework.Bootstrap
{
    public interface ICoroutineRunner
    {
        Coroutine Run(IEnumerator routine);
    }
}