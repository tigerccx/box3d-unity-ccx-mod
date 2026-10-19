using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Box3D.Hybrid.Editor
{
    /// <summary>Scene-view editing for the sphere shape: drag the radius like a Unity collider.
    /// Handles are drawn in a position+rotation frame with the lossy scale baked into dimensions,
    /// matching how the physics shape is built.</summary>
    [CustomEditor(typeof(Box3DSphereShape))]
    //CCX_MOD BEGIN(Enable multi-object editing for Shape inspectors)
    [CanEditMultipleObjects]
    //CCX_MOD END(Enable multi-object editing for Shape inspectors)
    //CCX_MOD BEGIN(Use shared Shape inspector)
    public class Box3DSphereShapeEditor : Box3DShapeEditor
    //CCX_MOD END(Use shared Shape inspector)
    {
        private readonly SphereBoundsHandle _handle = new SphereBoundsHandle();
        // Cached once — OnSceneGUI runs per scene-view event, and FindProperty re-walks the path.
        //CCX_MOD BEGIN(Cache serialized object and multi-selection state for Shape handles)
        private SerializedObject _serializedObject;
        private bool _editingMultipleObjects;
        //CCX_MOD END(Cache serialized object and multi-selection state for Shape handles)
        private SerializedProperty _radius;
        private SerializedProperty _center;

        private void OnEnable()
        {
            //CCX_MOD BEGIN(Initialize cached serialized properties for Shape handles)
            _serializedObject = serializedObject;
            _editingMultipleObjects = _serializedObject.isEditingMultipleObjects;
            _radius = _serializedObject.FindProperty("Radius");
            _center = _serializedObject.FindProperty("Center");
            //CCX_MOD END(Initialize cached serialized properties for Shape handles)
        }

        private void OnSceneGUI()
        {
            //CCX_MOD BEGIN(Skip Shape handles during multi-object editing)
            if (_editingMultipleObjects) return;

            //CCX_MOD END(Skip Shape handles during multi-object editing)
            Transform t = ((Box3DSphereShape)target).transform;
            SerializedProperty radius = _radius;
            SerializedProperty center = _center;

            float scale = Mathf.Max(0.0001f, ShapeHandleMath.MaxAbs(t.lossyScale));
            using (new Handles.DrawingScope(Matrix4x4.TRS(t.position, t.rotation, Vector3.one)))
            {
                _handle.center = Vector3.Scale(center.vector3Value, t.lossyScale);
                _handle.radius = radius.floatValue * scale;

                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Edit Sphere Shape");
                    radius.floatValue = _handle.radius / scale;
                    center.vector3Value = ShapeHandleMath.Unscale(_handle.center, t.lossyScale);
                    //CCX_MOD BEGIN(Apply Shape handle edits through the cached serialized object)
                    _serializedObject.ApplyModifiedProperties();
                    //CCX_MOD END(Apply Shape handle edits through the cached serialized object)
                }
            }
        }
    }

    /// <summary>Scene-view editing for the box shape: drag the faces to resize.</summary>
    [CustomEditor(typeof(Box3DBoxShape))]
    //CCX_MOD BEGIN(Enable multi-object editing for Shape inspectors)
    [CanEditMultipleObjects]
    //CCX_MOD END(Enable multi-object editing for Shape inspectors)
    //CCX_MOD BEGIN(Use shared Shape inspector)
    public class Box3DBoxShapeEditor : Box3DShapeEditor
    //CCX_MOD END(Use shared Shape inspector)
    {
        private readonly BoxBoundsHandle _handle = new BoxBoundsHandle();
        // Cached once — OnSceneGUI runs per scene-view event, and FindProperty re-walks the path.
        //CCX_MOD BEGIN(Cache serialized object and multi-selection state for Shape handles)
        private SerializedObject _serializedObject;
        private bool _editingMultipleObjects;
        //CCX_MOD END(Cache serialized object and multi-selection state for Shape handles)
        private SerializedProperty _size;
        private SerializedProperty _center;

        private void OnEnable()
        {
            //CCX_MOD BEGIN(Initialize cached serialized properties for Shape handles)
            _serializedObject = serializedObject;
            _editingMultipleObjects = _serializedObject.isEditingMultipleObjects;
            _size = _serializedObject.FindProperty("Size");
            _center = _serializedObject.FindProperty("Center");
            //CCX_MOD END(Initialize cached serialized properties for Shape handles)
        }

        private void OnSceneGUI()
        {
            //CCX_MOD BEGIN(Skip Shape handles during multi-object editing)
            if (_editingMultipleObjects) return;

            //CCX_MOD END(Skip Shape handles during multi-object editing)
            Transform t = ((Box3DBoxShape)target).transform;
            SerializedProperty size = _size;
            SerializedProperty center = _center;

            Vector3 scale = ShapeHandleMath.Abs(t.lossyScale);
            using (new Handles.DrawingScope(Matrix4x4.TRS(t.position, t.rotation, Vector3.one)))
            {
                _handle.center = Vector3.Scale(center.vector3Value, t.lossyScale);
                _handle.size = Vector3.Scale(size.vector3Value, scale);

                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Edit Box Shape");
                    size.vector3Value = ShapeHandleMath.Unscale(_handle.size, scale);
                    center.vector3Value = ShapeHandleMath.Unscale(_handle.center, t.lossyScale);
                    //CCX_MOD BEGIN(Apply Shape handle edits through the cached serialized object)
                    _serializedObject.ApplyModifiedProperties();
                    //CCX_MOD END(Apply Shape handle edits through the cached serialized object)
                }
            }
        }
    }

    /// <summary>Scene-view editing for the capsule shape: drag radius and height. Radius scales by
    /// the perpendicular axes and height by the capsule axis, matching the physics shape.</summary>
    [CustomEditor(typeof(Box3DCapsuleShape))]
    //CCX_MOD BEGIN(Enable multi-object editing for Shape inspectors)
    [CanEditMultipleObjects]
    //CCX_MOD END(Enable multi-object editing for Shape inspectors)
    //CCX_MOD BEGIN(Use shared Shape inspector)
    public class Box3DCapsuleShapeEditor : Box3DShapeEditor
    //CCX_MOD END(Use shared Shape inspector)
    {
        private readonly CapsuleBoundsHandle _handle = new CapsuleBoundsHandle();
        // Cached once — OnSceneGUI runs per scene-view event, and FindProperty re-walks the path.
        //CCX_MOD BEGIN(Cache serialized object and multi-selection state for Shape handles)
        private SerializedObject _serializedObject;
        private bool _editingMultipleObjects;
        //CCX_MOD END(Cache serialized object and multi-selection state for Shape handles)
        private SerializedProperty _radius;
        private SerializedProperty _height;
        private SerializedProperty _center;
        private SerializedProperty _direction;

        private void OnEnable()
        {
            //CCX_MOD BEGIN(Initialize cached serialized properties for Shape handles)
            _serializedObject = serializedObject;
            _editingMultipleObjects = _serializedObject.isEditingMultipleObjects;
            _radius = _serializedObject.FindProperty("Radius");
            _height = _serializedObject.FindProperty("Height");
            _center = _serializedObject.FindProperty("Center");
            _direction = _serializedObject.FindProperty("Direction");
            //CCX_MOD END(Initialize cached serialized properties for Shape handles)
        }

        private void OnSceneGUI()
        {
            //CCX_MOD BEGIN(Skip Shape handles during multi-object editing)
            if (_editingMultipleObjects) return;

            //CCX_MOD END(Skip Shape handles during multi-object editing)
            Transform t = ((Box3DCapsuleShape)target).transform;
            SerializedProperty radius = _radius;
            SerializedProperty height = _height;
            SerializedProperty center = _center;
            int direction = _direction.enumValueIndex; // 0=X, 1=Y, 2=Z

            Vector3 absScale = ShapeHandleMath.Abs(t.lossyScale);
            float axisScale = Mathf.Max(0.0001f, absScale[direction]);
            float radialScale = Mathf.Max(0.0001f, ShapeHandleMath.MaxExcept(absScale, direction));

            _handle.heightAxis = (CapsuleBoundsHandle.HeightAxis)direction;
            using (new Handles.DrawingScope(Matrix4x4.TRS(t.position, t.rotation, Vector3.one)))
            {
                _handle.center = Vector3.Scale(center.vector3Value, t.lossyScale);
                _handle.radius = radius.floatValue * radialScale;
                _handle.height = height.floatValue * axisScale;

                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Edit Capsule Shape");
                    radius.floatValue = _handle.radius / radialScale;
                    height.floatValue = _handle.height / axisScale;
                    center.vector3Value = ShapeHandleMath.Unscale(_handle.center, t.lossyScale);
                    //CCX_MOD BEGIN(Apply Shape handle edits through the cached serialized object)
                    _serializedObject.ApplyModifiedProperties();
                    //CCX_MOD END(Apply Shape handle edits through the cached serialized object)
                }
            }
        }
    }

    //CCX_MOD BEGIN(Draw selected Shape outlines independently of inspector foldouts)
    internal static class ShapeSelectionGizmos
    {
        [DrawGizmo(GizmoType.InSelectionHierarchy)]
        private static void DrawSelectedShape(Box3DShape shape, GizmoType gizmoType)
        {
            if (!shape) return;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            try
            {
                shape.DrawSelectedShapeGizmo();
            }
            finally
            {
                Gizmos.matrix = previousMatrix;
                Gizmos.color = previousColor;
            }
        }
    }
    //CCX_MOD END(Draw selected Shape outlines independently of inspector foldouts)

    internal static class ShapeHandleMath
    {
        public static float MaxAbs(Vector3 v)
        {
            return Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        public static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        // The largest component of v excluding the given axis index.
        public static float MaxExcept(Vector3 v, int axis)
        {
            float max = 0f;
            for (int i = 0; i < 3; i++)
            {
                if (i != axis) max = Mathf.Max(max, v[i]);
            }
            return max;
        }

        // Divides component-wise, treating a zero scale as 1 to avoid NaN.
        public static Vector3 Unscale(Vector3 v, Vector3 scale)
        {
            return new Vector3(
                Mathf.Approximately(scale.x, 0f) ? v.x : v.x / scale.x,
                Mathf.Approximately(scale.y, 0f) ? v.y : v.y / scale.y,
                Mathf.Approximately(scale.z, 0f) ? v.z : v.z / scale.z);
        }
    }
}
