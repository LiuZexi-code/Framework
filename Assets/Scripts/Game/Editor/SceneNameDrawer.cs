using Framework.Abstractions.Scenes;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// <see cref="SceneNameAttribute"/> 的绘制器：给场景名字段加一个「▼」下拉（可选已知场景）。
    /// <para>
    /// 目的：运行时程序集不能引用 <c>UnityEditor.SceneAsset</c>，所以字段类型必须是 <c>string</c>；
    /// 编辑器体验靠这个绘制器补齐 —— 值仍然写回字符串。未登记的取值会显示为浅红以便一眼看出。
    /// </para>
    /// </summary>
    [CustomPropertyDrawer(typeof(SceneNameAttribute))]
    public sealed class SceneNameDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 18f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var known = SceneNameUtility.GetKnownSceneNames();
            bool unregistered = !string.IsNullOrEmpty(property.stringValue) && !known.Contains(property.stringValue);

            var fieldRect = new Rect(position.x, position.y, position.width - ButtonWidth - 2f, position.height);
            var buttonRect = new Rect(position.xMax - ButtonWidth, position.y, ButtonWidth, position.height);

            var previousColor = GUI.color;
            if (unregistered) GUI.color = new Color(1f, 0.65f, 0.65f);
            EditorGUI.PropertyField(fieldRect, property, label);
            GUI.color = previousColor;

            if (EditorGUI.DropdownButton(buttonRect, new GUIContent("▼"), FocusType.Keyboard))
            {
                var menu = new GenericMenu();
                if (known.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent("（Build Settings / Addressables 中暂无场景）"));
                }
                else
                {
                    foreach (var name in known)
                    {
                        var captured = name;
                        menu.AddItem(new GUIContent(captured), captured == property.stringValue, () =>
                        {
                            property.stringValue = captured;
                            property.serializedObject.ApplyModifiedProperties();
                        });
                    }
                }
                menu.DropDown(buttonRect);
            }

            EditorGUI.EndProperty();
        }
    }
}
