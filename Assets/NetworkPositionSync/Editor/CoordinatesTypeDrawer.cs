using UnityEditor;
using UnityEngine;

namespace Mirage.SyncPosition.EditorScripts
{
    [CustomPropertyDrawer(typeof(CoordinatesType))]
    public class CoordinatesTypeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var labelWidth = EditorGUIUtility.labelWidth;
            var labelRect = new Rect(position.x, position.y, labelWidth, position.height);
            EditorGUI.LabelField(labelRect, label);

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // only show RelativeTo if we are on hat setting
            if (property.FindPropertyRelative("Space").enumValueIndex == (int)Coordinates.Relative)
            {
                var spaceRect = new Rect(position.x + labelWidth, position.y, (position.width - labelWidth) / 2, position.height);
                var relativeToRect = new Rect(position.x + labelWidth + ((position.width - labelWidth) / 2), position.y, (position.width - labelWidth) / 2, position.height);

                EditorGUI.PropertyField(spaceRect, property.FindPropertyRelative("Space"), GUIContent.none);
                EditorGUI.PropertyField(relativeToRect, property.FindPropertyRelative("RelativeTo"), GUIContent.none);
            }
            else
            {
                var spaceRect = new Rect(position.x + labelWidth, position.y, position.width - labelWidth, position.height);
                EditorGUI.PropertyField(spaceRect, property.FindPropertyRelative("Space"), GUIContent.none);
            }

            EditorGUI.indentLevel = indent;

            EditorGUI.EndProperty();
        }
    }
}
