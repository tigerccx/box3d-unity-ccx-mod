//CCX_MOD BEGIN(Shared Shape inspector with geometry and collision override foldouts)
using UnityEditor;
using UnityEngine;

namespace Box3D.Hybrid.Editor
{
    [CustomEditor(typeof(Box3DShape), true)]
    [CanEditMultipleObjects]
    public class Box3DShapeEditor : UnityEditor.Editor
    {
        private static readonly string[] GeometryFields =
        {
            "Center", "Size", "Radius", "Height", "Direction", "Mesh", "MaxVertices",
            "Terrain", "SampleStride", "ApplyHoles",
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var script = serializedObject.FindProperty("m_Script");
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(script);

            // Keep material, trigger and any future non-geometry fields visible as normal.
            var property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name == "m_Script" || property.name == "OverrideCollisionMask" ||
                    property.name == "CollisionMask" || System.Array.IndexOf(GeometryFields, property.name) >= 0)
                    continue;
                EditorGUILayout.PropertyField(property, true);
            }

            if (DrawFoldout("Shape"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (string name in GeometryFields)
                    {
                        var field = serializedObject.FindProperty(name);
                        if (field != null) EditorGUILayout.PropertyField(field, true);
                    }
                }
            }

            if (DrawFoldout("Collision Layer Override"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    var enabled = serializedObject.FindProperty("OverrideCollisionMask");
                    EditorGUILayout.PropertyField(enabled);
                    using (new EditorGUI.DisabledScope(!enabled.boolValue || enabled.hasMultipleDifferentValues))
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("CollisionMask"));
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private bool DrawFoldout(string title)
        {
            string key = "Box3D.ShapeInspector." + target.GetType().FullName + "." + title;
            bool expanded = SessionState.GetBool(key, true);
            bool next = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
            if (next != expanded) SessionState.SetBool(key, next);
            return next;
        }
    }
}
//CCX_MOD END(Shared Shape inspector with geometry and collision override foldouts)
