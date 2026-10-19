using UnityEditor;
using UnityEngine;

namespace Box3D.Hybrid.Editor
{
    /// <summary>Shared inspector for the mesh-sourced shapes: draws the default fields, then warns
    /// if the assigned mesh is missing or not Read/Write enabled (the common runtime-empty trap).</summary>
    //CCX_MOD BEGIN(Use shared Shape inspector)
    public abstract class MeshSourceShapeEditor : Box3DShapeEditor
    //CCX_MOD END(Use shared Shape inspector)
    {
        public override void OnInspectorGUI()
        {
            //CCX_MOD BEGIN(Use grouped Shape properties)
            base.OnInspectorGUI();
            //CCX_MOD END(Use grouped Shape properties)
            //CCX_MOD BEGIN(Show mesh validation messages only for a single selected Shape)

            if (targets.Length != 1) return;
            //CCX_MOD END(Show mesh validation messages only for a single selected Shape)

            var mesh = serializedObject.FindProperty("Mesh").objectReferenceValue as Mesh;
            if (!mesh)
            {
                EditorGUILayout.HelpBox("Assign a mesh for this shape.", MessageType.Info);
            }
            else if (!mesh.isReadable)
            {
                EditorGUILayout.HelpBox(
                    $"'{mesh.name}' is not Read/Write enabled, so the shape will be empty at runtime. " +
                    "Enable Read/Write in the mesh's import settings.", MessageType.Warning);
            }
        }
    }

    [CustomEditor(typeof(Box3DMeshShape))]
    //CCX_MOD BEGIN(Enable multi-object editing for mesh-sourced Shape inspectors)
    [CanEditMultipleObjects]
    //CCX_MOD END(Enable multi-object editing for mesh-sourced Shape inspectors)
    public class Box3DMeshShapeEditor : MeshSourceShapeEditor { }

    [CustomEditor(typeof(Box3DHullShape))]
    //CCX_MOD BEGIN(Enable multi-object editing for mesh-sourced Shape inspectors)
    [CanEditMultipleObjects]
    //CCX_MOD END(Enable multi-object editing for mesh-sourced Shape inspectors)
    public class Box3DHullShapeEditor : MeshSourceShapeEditor { }
}
