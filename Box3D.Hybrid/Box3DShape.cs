using System;
using Unity.Mathematics;
using UnityEngine;

namespace Box3D.Hybrid
{
    /// <summary>Base class for shape components, analogous to Unity's Collider. When the GameObject
    /// (or an ancestor) has a <see cref="Box3DBody"/>, the shape attaches to it — including shapes
    /// on child GameObjects (compound colliders). A shape with no body anywhere above it creates
    /// its own static body, mirroring Unity's "collider without a rigidbody is static".
    /// Adding a shape in the editor auto-adds a <see cref="Box3DBody"/> when the hierarchy has
    /// none (set its type to Static for non-moving geometry).
    //CCX_MOD BEGIN(Support shape mass and density settings)
    /// Material and supported mass settings can be changed at runtime.</summary>
    //CCX_MOD END(Support shape mass and density settings)
    public abstract class Box3DShape : MonoBehaviour
    {
        //CCX_MOD BEGIN(Support shape mass and density settings)
        // Density configuration is now owned by Box3DDynamicBodyShape.
        //CCX_MOD END(Support shape mass and density settings)
        [SerializeField, Range(0f, 1f), Tooltip("Coulomb friction coefficient.")]
        private float Friction = 0.6f;

        [SerializeField, Range(0f, 1f), Tooltip("Bounciness. Only applies above the world's restitution speed threshold (~1 m/s), so gentle settling never bounces.")]
        private float Restitution;

        [SerializeField, Tooltip("Local offset of the shape from the body origin.")]
        private Vector3 Center = Vector3.zero;

        //CCX_MOD BEGIN(Trigger and collision-layer settings)
        [SerializeField, Tooltip("Detect overlaps without a collision response.")]
        private bool IsTrigger;
        [SerializeField, Tooltip("Generate sensor overlap events, including when entering another trigger.")]
        private bool EnableTriggerEvents = true;
        [SerializeField, Tooltip("Replace this shape's mask from the Physics Layer Collision Matrix with Collision Mask.")]
        private bool OverrideCollisionMask;
        [SerializeField, Tooltip("Used only when Override Collision Mask is enabled. Both shapes must accept each other.")]
        private LayerMask CollisionMask = ~0;

        public bool isTrigger
        {
            get => IsTrigger;
            set
            {
                bool previous = IsTrigger;
                IsTrigger = value;
                try { RefreshCollisionSettings(); }
                catch { IsTrigger = previous; throw; }
            }
        }
        public bool enableTriggerEvents
        {
            get => EnableTriggerEvents;
            set { EnableTriggerEvents = value; RefreshCollisionSettings(); }
        }
        /// <summary>Whether collisionMask replaces this shape's mask from the global layer matrix.</summary>
        public bool overrideCollisionMask
        {
            get => OverrideCollisionMask;
            set { OverrideCollisionMask = value; RefreshCollisionSettings(); }
        }
        /// <summary>Custom mask used only while overrideCollisionMask is true.</summary>
        public LayerMask collisionMask
        {
            get => CollisionMask;
            set { CollisionMask = value; RefreshCollisionSettings(); }
        }
        private CollisionFilter BuildCollisionFilter()
        {
            int layer = gameObject.layer;
            var filter = CollisionFilter.Default;
            filter.CategoryBits = 1UL << layer;
            filter.MaskBits = OverrideCollisionMask ? unchecked((uint)CollisionMask.value) : CollisionMaskForLayer(layer);
            return filter;
        }
        /// <summary>Reapply after changing GameObject.layer or the global layer collision matrix.
        /// Call outside World.Step, as with other collision mutations.</summary>
        public void RefreshCollisionSettings()
        {
            if (!_shape.IsValid) return;
            if (_shape.IsSensor() != IsTrigger) RebuildTriggerGeometry();
            _shape.SetFilter(BuildCollisionFilter());
            _shape.EnableSensorEvents(EnableTriggerEvents);
        }
        protected virtual void RebuildTriggerGeometry() => RecreatePrimitiveGeometry();

        protected void ReplaceCollisionShape(Shape replacement)
        {
            if (!replacement.IsValid) throw new InvalidOperationException("Could not rebuild collision geometry.");
            IntPtr userData = _shape.UserData;
            bool hitEvents = _shape.AreHitEventsEnabled();
            _shape.Destroy(updateBodyMass: true);
            _shape = replacement;
            //CCX_MOD BEGIN(Update world registration after replacing collision geometry)
            UpdateShapeRegistration();
            //CCX_MOD END(Update world registration after replacing collision geometry)
            _shape.UserData = userData;
            _shape.EnableHitEvents(hitEvents);
            _shape.EnableSensorEvents(EnableTriggerEvents);
        }
        //CCX_MOD END(Trigger and collision-layer settings)

        //CCX_MOD BEGIN(Expose Shape ownership and maintain world component registrations)
        private Box3DWorld _registryWorld;
        private ShapeId _registeredShapeId;
        private Box3DBody _bodyComponent;

        /// <summary>The native Shape; validate IsValid before using it after teardown.</summary>
        public Shape Shape => _shape;
        /// <summary>The native Body, including the private static body used by orphan shapes.</summary>
        public Body Body => _attachedBody;
        /// <summary>The component this shape was attached to, or null for an orphan static shape.</summary>
        public Box3DBody AttachedBodyComponent => _bodyComponent;

        private void UpdateShapeRegistration()
        {
            UnregisterFromWorld();
            if (!_registryWorld || !_shape.IsValid) return;
            _registryWorld.RegisterShape(this);
            _registeredShapeId = _shape.Id;
        }

        internal void UnregisterFromWorld()
        {
            if (_registryWorld && !_registeredShapeId.IsNull)
                _registryWorld.UnregisterShape(_registeredShapeId, this);
            _registeredShapeId = default;
        }
        //CCX_MOD END(Expose Shape ownership and maintain world component registrations)

        private Shape _shape;
        private Body _attachedBody; // the body this shape was created on (rebuilds recreate on it)
        private Body _ownBody; // only set when this shape has no Box3DBody and creates a static one

        // The attach frame from AttachTo, kept so Inspector edits can rebuild geometry in place.
        private float3 _attachPosition;
        private quaternion _attachRotation = quaternion.identity;
        private float3 _attachScale = new float3(1f);

        //CCX_MOD BEGIN(Unity Collider-style local center and geometry refresh)
        /// <summary>Shape center in local space, before Transform scaling.</summary>
        public Vector3 center
        {
            get => Center;
            set
            {
                Vector3 previous = Center;
                Center = value;
                try { RefreshGeometry(); }
                catch { Center = previous; throw; }
            }
        }

        protected void RefreshGeometry()
        {
            if (!_shape.IsValid) return;
            //CCX_MOD BEGIN(Support shape mass and density settings)
            _attachScale = transform.lossyScale;
            //CCX_MOD END(Support shape mass and density settings)
            UpdateLiveGeometry();
            //CCX_MOD BEGIN(Support shape mass and density settings)
            ApplyShapeDensity();
            //CCX_MOD END(Support shape mass and density settings)
            if (_attachedBody.IsValid) _attachedBody.SetAwake(true);
        }

        // Used for primitive shapes whose managed binding has no in-place geometry setter.
        protected void RecreatePrimitiveGeometry()
        {
            IntPtr userData = _shape.UserData;
            bool hitEvents = _shape.AreHitEventsEnabled();
            Shape replacement = CreateShape(_attachedBody, _attachPosition, _attachRotation, _attachScale);
            if (!replacement.IsValid) throw new InvalidOperationException("Could not rebuild collision geometry.");
            _shape.Destroy(updateBodyMass: true);
            _shape = replacement;
            //CCX_MOD BEGIN(Update world registration after replacing collision geometry)
            UpdateShapeRegistration();
            //CCX_MOD END(Update world registration after replacing collision geometry)
            _shape.UserData = userData;
            _shape.EnableHitEvents(hitEvents);
        }
        //CCX_MOD END(Unity Collider-style local center and geometry refresh)

        protected float3 LocalCenter => Center;

        /// <summary>The live native shape (valid between Awake and OnDestroy).</summary>
        protected Shape LiveShape => _shape;

        protected float3 AttachedPosition => _attachPosition;
        protected quaternion AttachedRotation => _attachRotation;
        protected float3 AttachedScale => _attachScale;

        /// <summary>Sets friction, updating the live shape if it exists.</summary>
        public void SetFriction(float value)
        {
            Friction = value;
            if (_shape.IsValid) _shape.SetFriction(value);
        }

        /// <summary>Sets bounciness, updating the live shape if it exists.</summary>
        public void SetRestitution(float value)
        {
            Restitution = value;
            if (_shape.IsValid) _shape.SetRestitution(value);
        }

        //CCX_MOD BEGIN(Support shape mass and density settings)
        // SetDensity is now defined on Box3DDynamicBodyShape.
        //CCX_MOD END(Support shape mass and density settings)

#if UNITY_EDITOR
        // Runs when the component is added in the editor (and on context-menu Reset). Like
        // RequireComponent, but hierarchy-aware: a compound shape on a child of a body must NOT
        // get its own body (a nested Box3DBody splits the compound), so only orphan shapes get one.
        // For static geometry, set the auto-added body's type to Static.
        private void Reset()
        {
            if (!GetComponentInParent<Box3DBody>(true))
            {
                UnityEditor.Undo.AddComponent<Box3DBody>(gameObject);
            }
        }
#endif

        private void Awake()
        {
            // A body on this GameObject or an ancestor will gather and attach this shape (including
            // as a compound child). Otherwise the shape is an orphan → give it a static body.
            if (GetComponentInParent<Box3DBody>()) return;

            Box3DWorld world = Box3DWorld.Instance;
            BodyDef def = BodyDef.Default; // static by default
            def.Position = transform.position;
            def.Rotation = transform.rotation;
            _ownBody = world.World.CreateBody(def);
            //CCX_MOD BEGIN(Register orphan Shapes with their owning world)
            // AttachTo(_ownBody, float3.zero, quaternion.identity, transform.lossyScale);
            InitializeAttachment(world, _ownBody, float3.zero, quaternion.identity, transform.lossyScale);
            //CCX_MOD END(Register orphan Shapes with their owning world)
        }

        private void OnDestroy()
        {
            //CCX_MOD BEGIN(Remove component registration before Shape destruction)
            UnregisterFromWorld();
            //CCX_MOD END(Remove component registration before Shape destruction)
            if (_ownBody.IsValid)
            {
                // Orphan shape: the self-created static body is ours (destroying it destroys the shape).
                _ownBody.Destroy();
            }
            else if (_shape.IsValid)
            {
                // Body-managed shape destroyed individually (component removed, or its child
                // GameObject destroyed while the body lives): remove the native shape so its
                // collision doesn't linger, and re-derive the body's mass. During whole-body
                // teardown the body's destroy already invalidated the id, so this no-ops there.
                _shape.Destroy(updateBodyMass: true);
            }
            // Geometry (mesh/height-field) is user-owned memory independent of the world — free it
            // even when the world was destroyed first. ReleaseGeometry is idempotent, so the owning
            // Box3DBody's sweep may run before or after this in any teardown order.
            ReleaseGeometry();
        }

        /// <summary>A shape definition seeded from this component's material fields and the
        /// GameObject's Unity collision layer (so Project Settings → Physics → Layer Collision
        /// Matrix is honored).</summary>
        protected ShapeDef BuildDef()
        {
            ShapeDef def = ShapeDef.Default;
            //CCX_MOD BEGIN(Support shape mass and density settings)
            def.Density = ResolveDensity();
            //CCX_MOD END(Support shape mass and density settings)
            def.BaseMaterial.Friction = Friction;
            def.BaseMaterial.Restitution = Restitution;
            //CCX_MOD BEGIN(Apply trigger and layer settings at shape creation)
            def.IsSensor = IsTrigger;
            def.EnableSensorEvents = EnableTriggerEvents;
            def.Filter = BuildCollisionFilter();
            //CCX_MOD END(Apply trigger and layer settings at shape creation)
            return def;
        }

        // Builds a box3d mask from Unity's layer collision matrix: bit L is set for every layer
        // this layer is allowed to collide with.
        internal static ulong CollisionMaskForLayer(int layer)
        {
            ulong mask = 0;
            for (int other = 0; other < 32; other++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, other)) mask |= 1UL << other;
            }
            return mask;
        }

        //CCX_MOD BEGIN(Attach Shapes through Box3DBody and register their world ownership)
        // Original attachment implementation retained for comparison.
        // /// <summary>Creates the native shape on the given body. <paramref name="localPosition"/> and
        // /// <paramref name="localRotation"/> place the shape relative to the body's frame (identity
        // /// for a shape on the body's own GameObject); <paramref name="scale"/> is the shape
        // /// GameObject's lossy scale, baked into the dimensions.</summary>
        // internal void AttachTo(Body body, float3 localPosition, quaternion localRotation, float3 scale,
        //     IntPtr ownerData = default, bool enableHitEvents = false)
        // {
        //     _attachPosition = localPosition;
        //     _attachRotation = localRotation;
        //     _attachScale = scale;
        //     _attachedBody = body;
        //     _shape = CreateShape(body, localPosition, localRotation, scale);
        //     if (!_shape.IsValid) return;
        //
        //     // The owning Box3DBody's GCHandle rides in shape userData, so a contact event maps its
        //     // ShapeId straight back to the component — the shape-level twin of the body-move channel.
        //     if (ownerData != IntPtr.Zero) _shape.UserData = ownerData;
        //     if (enableHitEvents) _shape.EnableHitEvents(true);
        // }
        //

        /// <summary>Creates the native shape on the given body. <paramref name="localPosition"/> and
        /// <paramref name="localRotation"/> place the shape relative to the body's frame (identity
        /// for a shape on the body's own GameObject); <paramref name="scale"/> is the shape
        /// GameObject's lossy scale, baked into the dimensions.</summary>
        internal void AttachTo(Box3DBody bodyComponent, float3 localPosition, quaternion localRotation, float3 scale,
            IntPtr ownerData = default, bool enableHitEvents = false)
        {
            if (!bodyComponent) throw new ArgumentNullException(nameof(bodyComponent));
            if (!bodyComponent.Body.IsValid)
                throw new ArgumentException("Cannot attach a Shape to an invalid Body.", nameof(bodyComponent));
            InitializeAttachment(bodyComponent.GetWorld(), bodyComponent.Body, localPosition, localRotation, scale,
                ownerData, enableHitEvents, bodyComponent);
        }

        // Orphan shapes own a private native static body and have no Box3DBody component.
        private void InitializeAttachment(Box3DWorld registryWorld, Body body, float3 localPosition, quaternion localRotation, float3 scale,
            IntPtr ownerData = default, bool enableHitEvents = false, Box3DBody bodyComponent = null)
        {
            _attachPosition = localPosition;
            _attachRotation = localRotation;
            _attachScale = scale;
            _attachedBody = body;
            UnregisterFromWorld();
            _registryWorld = registryWorld;
            _bodyComponent = bodyComponent;
            _shape = CreateShape(body, localPosition, localRotation, scale);
            UpdateShapeRegistration();
            if (!_shape.IsValid) return;

            // The owning Box3DBody's GCHandle rides in shape userData, so a contact event maps its
            // ShapeId straight back to the component — the shape-level twin of the body-move channel.
            if (ownerData != IntPtr.Zero) _shape.UserData = ownerData;
            if (enableHitEvents) _shape.EnableHitEvents(true);
        }
        //CCX_MOD END(Attach Shapes through Box3DBody and register their world ownership)

#if UNITY_EDITOR
        //CCX_MOD BEGIN(Support shape mass and density settings)
        protected virtual void OnValidate()
        //CCX_MOD END(Support shape mass and density settings)
        {
            // Push Inspector edits to the live shape during play. SetDensity with updateBodyMass
            // re-derives mass from the current geometry, so size edits update mass too.
            if (!Application.isPlaying || !_shape.IsValid) return;
            //CCX_MOD BEGIN(Apply Inspector trigger and layer edits)
            RefreshCollisionSettings();
            //CCX_MOD END(Apply Inspector trigger and layer edits)
            _shape.SetFriction(Friction);
            _shape.SetRestitution(Restitution);
            //CCX_MOD BEGIN(Support shape mass and density settings)
            _attachScale = transform.lossyScale;
            //CCX_MOD END(Support shape mass and density settings)
            UpdateLiveGeometry();
            //CCX_MOD BEGIN(Support shape mass and density settings)
            ApplyShapeDensity();
            //CCX_MOD END(Support shape mass and density settings)
        }
#endif

        /// <summary>Pushes edited geometry to the live native shape where the engine supports
        /// in-place replacement (sphere and capsule). Other shapes keep their creation geometry.</summary>
        protected virtual void UpdateLiveGeometry() { }

        /// <summary>The body this shape was created on (valid alongside <see cref="LiveShape"/>).</summary>
        protected Body AttachedBody => _attachedBody;

        /// <summary>Rebuilds the live collision from deformed vertices in this GameObject's local
        /// space — the deformable's Update Collision mode. Hull and mesh shapes support it; other
        /// shapes return false and keep their creation geometry. The replacement is created first
        /// so a failed rebuild leaves the old collision in place; userData and event opt-ins carry
        /// over, and body mass is re-derived.</summary>
        internal bool TryRebuildGeometry(Vector3[] vertices, int[] triangles)
        {
            if (!_shape.IsValid || !_attachedBody.IsValid || vertices == null) return false;

            IntPtr userData = _shape.UserData;
            bool hitEvents = _shape.AreHitEventsEnabled();

            Shape rebuilt = CreateRebuiltShape(vertices, triangles);
            if (!rebuilt.IsValid) return false;

            _shape.Destroy(updateBodyMass: true); // re-derives mass over remaining + rebuilt shapes
            ReleaseRebuiltGeometry();
            _shape = rebuilt;
            //CCX_MOD BEGIN(Update world registration after rebuilding collision geometry)
            UpdateShapeRegistration();
            //CCX_MOD END(Update world registration after rebuilding collision geometry)
            if (userData != IntPtr.Zero) _shape.UserData = userData;
            if (hitEvents) _shape.EnableHitEvents(true);
            //CCX_MOD BEGIN(Support shape mass and density settings)
            ApplyShapeDensity();
            //CCX_MOD END(Support shape mass and density settings)
            return true;
        }

        /// <summary>Shape-specific rebuild: create the replacement native shape from the deformed
        /// vertices (and triangle topology, for mesh shapes), or default when unsupported. Runs
        /// while the old shape still exists — geometry it references must stay alive until
        /// <see cref="ReleaseRebuiltGeometry"/>.</summary>
        internal virtual Shape CreateRebuiltShape(Vector3[] vertices, int[] triangles) => default;

        /// <summary>Frees geometry the pre-rebuild shape referenced, once that shape is destroyed.</summary>
        internal virtual void ReleaseRebuiltGeometry() { }

#if UNITY_EDITOR
        /// <summary>Creates this shape on a body in a throwaway preview world (rope editor
        /// preview), leaving component state alone. The body must already sit at this shape's
        /// transform pose.</summary>
        internal Shape CreateDetachedShape(Body body)
        {
            return CreateShape(body, float3.zero, quaternion.identity, transform.lossyScale);
        }

        /// <summary>Compound variant for preview bodies that own several shapes (editor physics
        /// simulation): places the shape at a local frame within the body instead of assuming the
        /// body sits at the shape's own transform.</summary>
        internal Shape CreateDetachedShape(Body body, float3 localPosition, quaternion localRotation)
        {
            return CreateShape(body, localPosition, localRotation, transform.lossyScale);
        }

        /// <summary>Frees native geometry a detached preview shape allocated (mesh shapes).
        /// Called after the preview world is destroyed.</summary>
        internal virtual void ReleaseDetachedGeometry() { }
#endif

        protected abstract Shape CreateShape(Body body, float3 localPosition, quaternion localRotation, float3 scale);

        /// <summary>Frees any native geometry this shape owns. Called after the body — and its
        /// shapes — are destroyed, so referenced mesh/heightfield data outlives its shape.</summary>
        internal virtual void ReleaseGeometry() { }

        /// <summary>The shape's local center offset, rotated and offset into the body frame.</summary>
        protected float3 ShapeCenter(float3 localPosition, quaternion localRotation, float3 scale)
        {
            return localPosition + math.mul(localRotation, LocalCenter * scale);
        }

        // The color Unity uses for collider gizmos, so these read as familiar.
        private static readonly Color GizmoColor = new Color(0.5f, 0.9f, 0.6f, 0.9f);

        /// <summary>Sets the gizmo color and a position+rotation frame at the shape's own Transform
        /// (which is where the physics shape ends up, self or compound child).</summary>
        protected void SetGizmoFrame()
        {
            Gizmos.color = GizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        }

        //CCX_MOD BEGIN(Shared selected-shape drawing entry point)
        internal virtual void DrawSelectedShapeGizmo() { }
        //CCX_MOD END(Shared selected-shape drawing entry point)

        protected float3 ScaledCenter => (float3)Center * (float3)transform.lossyScale;
        //CCX_MOD BEGIN(Support shape mass and density settings)

        protected virtual float ResolveDensity() => 0f;

        protected virtual void ApplyShapeDensity() { }

        //CCX_MOD END(Support shape mass and density settings)
    }
}
