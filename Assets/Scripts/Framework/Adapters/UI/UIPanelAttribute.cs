using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.UI
{
    [AttributeUsage(AttributeTargets.Class)]
    public class UIPanelAttribute : Attribute
    {
        public string Key;
        public UIPanelAttribute(string key) => Key = key;
    }
}
