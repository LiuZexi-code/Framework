using System.Collections;
using TMPro;
using UnityEngine;

namespace Game.UI.Tools
{
    /// <summary>省略号动画：周期性地在文本后追加 0..maxDots 个点。</summary>
    public class LoadingDots : MonoBehaviour
    {
        [Header("文本引用")]
        public TMP_Text loadingText;

        [Header("显示设置")]
        public string baseText = "Loading";
        public int maxDots = 3;
        public float interval = 0.4f;

        private string[] dotCache;
        private Coroutine loopCoroutine;

        void Awake()
        {
            if (loadingText == null)
                loadingText = GetComponent<TMP_Text>();

            // 缓存 "", ".", "..", "..."，避免频繁 new string
            dotCache = new string[maxDots + 1];
            for (int i = 0; i <= maxDots; i++)
            {
                dotCache[i] = new string('.', i);
            }
        }

        void OnEnable()
        {
            loopCoroutine = StartCoroutine(LoopDots());
        }

        void OnDisable()
        {
            if (loopCoroutine != null)
            {
                StopCoroutine(loopCoroutine);
                loopCoroutine = null;
            }
        }

        IEnumerator LoopDots()
        {
            int dots = 0;

            while (true)
            {
                loadingText.text = baseText + dotCache[dots];

                dots++;
                if (dots > maxDots)
                    dots = 0;

                yield return new WaitForSeconds(interval);
            }
        }
    }
}