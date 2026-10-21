using Unity.Mathematics;
using UnityEngine;

namespace Box3D.Hybrid
{
    /// <summary>A box shape, analogous to Unity's BoxCollider.</summary>
    [Icon("Packages/com.suvitruf.box3d/Box3D.Hybrid.Editor/Icons/Box3DBoxShape.png")]
    [AddComponentMenu("Box3D/Shapes/Box Shape")]
    //CCX_MOD BEGIN(Support shape mass and density settings)
    public class Box3DBoxShape : Box3DDynamicBodyShape
    //CCX_MOD END(Support shape mass and density settings)
    {
        [SerializeField, Tooltip("Full box size in local units (like Unity's BoxCollider.size).")]
        private Vector3 Size = Vector3.one;

        //CCX_MOD BEGIN(Unity Collider-style shape properties)
        public Vector3 size
        {
            get => Size;
            set
            {
                Vector3 previous = Size;
                Size = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
                try { RefreshGeometry(); }
                catch { Size = previous; throw; }
            }
        }
        protected override void UpdateLiveGeometry() => RecreatePrimitiveGeometry();
        //CCX_MOD END(Unity Collider-style shape properties)

        protected override Shape CreateShape(Body body, float3 localPosition, quaternion localRotation, float3 scale)
        {
            float3 halfExtents = (float3)Size * 0.5f * math.abs(scale);
            var frame = new B3Transform
            {
                Position = ShapeCenter(localPosition, localRotation, scale),
                Rotation = localRotation,
            };
            BoxHull hull = BoxHull.CreateTransformed(halfExtents.x, halfExtents.y, halfExtents.z, frame);
            return body.CreateHullShape(BuildDef(), in hull);
        }

        //CCX_MOD BEGIN(Draw through the editor selection gizmo dispatcher)
        internal override void DrawSelectedShapeGizmo()
        //CCX_MOD END(Draw through the editor selection gizmo dispatcher)
        {
            SetGizmoFrame();
            float3 size = (float3)Size * math.abs((float3)transform.lossyScale);
            Gizmos.DrawWireCube((Vector3)ScaledCenter, (Vector3)size);
        }
        //CCX_MOD BEGIN(Support shape mass and density settings)

        public override float CalculateVolume()
        {
            float3 dimensions = math.abs((float3)Size * VolumeScale);
            return dimensions.x * dimensions.y * dimensions.z;
        }

        //CCX_MOD END(Support shape mass and density settings)
    }
}
