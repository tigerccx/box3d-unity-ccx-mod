using Unity.Mathematics;
using UnityEngine;

namespace Box3D.Hybrid
{
    /// <summary>A sphere shape, analogous to Unity's SphereCollider. Non-uniform scale uses the
    /// largest axis (same limitation as Unity).</summary>
    [Icon("Packages/com.suvitruf.box3d/Box3D.Hybrid.Editor/Icons/Box3DSphereShape.png")]
    [AddComponentMenu("Box3D/Shapes/Sphere Shape")]
    public class Box3DSphereShape : Box3DShape
    {
        [SerializeField, Min(0f), Tooltip("Sphere radius in local units.")]
        private float Radius = 0.5f;

        //CCX_MOD BEGIN(Unity Collider-style shape properties)
        public float radius
        {
            get => Radius;
            set
            {
                float previous = Radius;
                Radius = Mathf.Max(0f, value);
                try { RefreshGeometry(); }
                catch { Radius = previous; throw; }
            }
        }
        //CCX_MOD END(Unity Collider-style shape properties)

        protected override Shape CreateShape(Body body, float3 localPosition, quaternion localRotation, float3 scale)
        {
            return body.CreateSphereShape(BuildDef(), BuildSphere(localPosition, localRotation, scale));
        }

        protected override void UpdateLiveGeometry()
        {
            LiveShape.SetSphere(BuildSphere(AttachedPosition, AttachedRotation, AttachedScale));
        }

        private Sphere BuildSphere(float3 localPosition, quaternion localRotation, float3 scale)
        {
            return new Sphere
            {
                Center = ShapeCenter(localPosition, localRotation, scale),
                Radius = Radius * math.cmax(math.abs(scale)),
            };
        }

        //CCX_MOD BEGIN(Draw through the editor selection gizmo dispatcher)
        internal override void DrawSelectedShapeGizmo()
        //CCX_MOD END(Draw through the editor selection gizmo dispatcher)
        {
            SetGizmoFrame();
            float radius = Radius * math.cmax(math.abs((float3)transform.lossyScale));
            Gizmos.DrawWireSphere((Vector3)ScaledCenter, radius);
        }
    }
}
