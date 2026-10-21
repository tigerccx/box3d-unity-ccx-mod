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
                //CCX_MOD BEGIN(Support shape mass and density settings)
                if (property.name == "m_Script" || property.name == "MassMode" ||
                    property.name == "Mass" || property.name == "Density" || property.name == "OverrideCollisionMask" ||
                //CCX_MOD END(Support shape mass and density settings)
                    property.name == "CollisionMask" || System.Array.IndexOf(GeometryFields, property.name) >= 0)
                    continue;
                EditorGUILayout.PropertyField(property, true);
            }
            //CCX_MOD BEGIN(Support shape mass and density settings)

            DrawMassSettings();
            //CCX_MOD END(Support shape mass and density settings)

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
        //CCX_MOD BEGIN(Support shape mass and density settings)

        public override bool RequiresConstantRepaint() => true;

        private void DrawMassSettings()
        {
            if (!(target is Box3DDynamicBodyShape shape)) return;
            var mode = serializedObject.FindProperty("MassMode");
            EditorGUI.showMixedValue = mode.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var next = (Box3DMassMode)EditorGUILayout.EnumPopup("Mass Mode", shape.massMode);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                bool valid = true;
                foreach (Object item in targets)
                    valid &= Box3DDynamicBodyShape.IsValidVolume(((Box3DDynamicBodyShape)item).CalculateVolume());
                if (valid)
                {
                    Undo.RecordObjects(targets, "Change Shape Mass Mode");
                    foreach (Object item in targets)
                    {
                        ((Box3DDynamicBodyShape)item).massMode = next;
                        EditorUtility.SetDirty(item);
                    }
                }
                serializedObject.Update();
            }
            EditorGUI.showMixedValue = false;
            if (!mode.hasMultipleDifferentValues)
                EditorGUILayout.PropertyField(serializedObject.FindProperty(shape.massMode == Box3DMassMode.Mass ? "Mass" : "Density"));
            // Apply geometry and input edits before showing their derived values.
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            foreach (Object item in targets)
            {
                var current = (Box3DDynamicBodyShape)item;
                float volume = current.CalculateVolume();
                if (!Box3DDynamicBodyShape.IsValidVolume(volume))
                {
                    EditorGUILayout.HelpBox(current.name + ": Shape volume must be finite and greater than zero.", MessageType.Error);
                    continue;
                }
                if (targets.Length > 1) EditorGUILayout.LabelField(current.name);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.FloatField("Calculated Volume", volume);
                    EditorGUILayout.FloatField(current.massMode == Box3DMassMode.Mass ? "Calculated Density" : "Calculated Mass",
                        current.massMode == Box3DMassMode.Mass ? current.CalculatedDensity : current.CalculatedMass);
                }
            }
        }

        //CCX_MOD END(Support shape mass and density settings)
    }
}
//CCX_MOD END(Shared Shape inspector with geometry and collision override foldouts)
