using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Box3D.Hybrid
{
    /// <summary>Motion type of a <see cref="Box3DBody"/>, mirroring Unity's static collider /
    /// kinematic / dynamic Rigidbody distinction.</summary>
    public enum Box3DBodyType
    {
        Static,
        Kinematic,
        Dynamic,
    }

    /// <summary>A physics body, analogous to Unity's Rigidbody. The native body is created once
    /// (Awake) and destroyed once (OnDestroy); enabling/disabling toggles it in the simulation
    /// without recreating it. Dynamic bodies drive their Transform (synced from body-move events);
    /// kinematic bodies follow it. To move a body from code use <see cref="Position"/> /
    /// <see cref="Rotation"/> (like Unity's Rigidbody.position), not transform.position directly.</summary>
    [Icon("Packages/com.suvitruf.box3d/Box3D.Hybrid.Editor/Icons/Box3DBody.png")]
    [AddComponentMenu("Box3D/Body")]
    [DisallowMultipleComponent]
    //CCX_MOD BEGIN(Initialize bodies after the world and before default-order scripts)
    [DefaultExecutionOrder(-90)]
    //CCX_MOD END(Initialize bodies after the world and before default-order scripts)
    public class Box3DBody : MonoBehaviour
    {
        [SerializeField, Tooltip("Static: never moves. Kinematic: moved by its Transform. Dynamic: moved by the solver.")]
        private Box3DBodyType Type = Box3DBodyType.Dynamic;

        [SerializeField, Min(0f), Tooltip("Linear velocity damping.")]
        private float LinearDamping;

        [SerializeField, Min(0f), Tooltip("Angular velocity damping.")]
        private float AngularDamping = 0.05f;

        [SerializeField, Tooltip("Start awake, or asleep until disturbed.")]
        private bool StartAwake = true;

        [SerializeField, Tooltip("Allow fast rotation (e.g. spinning wheels) without box3d clamping angular velocity.")]
        //CCX_MOD BEGIN(Rename field to allow Body API forwarding)
        [UnityEngine.Serialization.FormerlySerializedAs("AllowFastRotation")]
        private bool _allowFastRotation;
        //CCX_MOD END(Rename field to allow Body API forwarding)

        //CCX_MOD BEGIN(Support shape mass and density settings)
        [SerializeField] private bool OverrideMass;
        [SerializeField, Min(0f)] private float MassOverride = 1f;

        //CCX_MOD END(Support shape mass and density settings)
        private Box3DWorld _world;
        private Body _body;
        private Box3DShape[] _shapes;
        private IBox3DHitReceiver[] _hitReceivers;
        // A handle to this component lives in the native body's userData, so a body-move event
        // dereferences straight back here — no managed-side lookup list.
        private GCHandle _handle;

        /// <summary>The underlying Box3D body (valid between Awake and OnDestroy).</summary>
        public Body Body => _body;

        /// <summary>Motion type. Setting it at runtime re-types the live body (like toggling
        /// Rigidbody.isKinematic).</summary>
        public Box3DBodyType BodyType
        {
            get => Type;
            set
            {
                if (Type == value) return;
                bool wasKinematic = Type == Box3DBodyType.Kinematic;
                Type = value;
                if (!_body.IsValid) return;

                _body.SetType(ToNative(value));
                //CCX_MOD BEGIN(Support shape mass and density settings)
                ApplyMassOverride();
                //CCX_MOD END(Support shape mass and density settings)
                bool isKinematic = value == Box3DBodyType.Kinematic;
                if (isKinematic && !wasKinematic) _world.AddKinematic(this);
                else if (!isKinematic && wasKinematic) _world.RemoveKinematic(this);
            }
        }

        /// <summary>World position. Setting it teleports the body (and wakes a dynamic one) — the
        /// correct way to move a body from code.</summary>
        public Vector3 Position
        {
            get => transform.position;
            set => Warp(value, transform.rotation);
        }

        /// <summary>World rotation. Setting it teleports the body (and wakes a dynamic one).</summary>
        public Quaternion Rotation
        {
            get => transform.rotation;
            set => Warp(transform.position, value);
        }

        /// <summary>Enables fast rotation (spinning wheels). Set before the body is created (Awake).</summary>
        public void SetAllowFastRotation(bool value)
        {
            //CCX_MOD BEGIN(Rename field to allow Body API forwarding)
            _allowFastRotation = value;
            //CCX_MOD END(Rename field to allow Body API forwarding)
        }

        // Serialized damping, readable without a live body — the editor physics simulation builds
        // its preview bodies with the same damping the runtime will use.
        internal float LinearDampingValue => LinearDamping;
        internal float AngularDampingValue => AngularDamping;

        private void Awake()
        {
            _world = Box3DWorld.Instance;

            BodyDef def = BodyDef.Default;
            def.Type = ToNative(Type);
            def.Position = transform.position;
            def.Rotation = transform.rotation;
            def.LinearDamping = LinearDamping;
            def.AngularDamping = AngularDamping;
            def.IsAwake = StartAwake;
            def.IsEnabled = isActiveAndEnabled;
            //CCX_MOD BEGIN(Rename field to allow Body API forwarding)
            def.AllowFastRotation = _allowFastRotation;
            //CCX_MOD END(Rename field to allow Body API forwarding)

            _handle = GCHandle.Alloc(this);
            def.UserData = GCHandle.ToIntPtr(_handle);
            _body = _world.World.CreateBody(def);
            _body.SetName(gameObject.name); // recorded — lets the visual replayer map replay bodies to scene objects

            var shapes = new System.Collections.Generic.List<Box3DShape>();
            GatherShapes(transform, shapes, isRoot: true);
            _shapes = shapes.ToArray();

            var receivers = new System.Collections.Generic.List<IBox3DHitReceiver>();
            GatherHitReceivers(transform, receivers, isRoot: true);
            _hitReceivers = receivers.ToArray();

            quaternion bodyInverse = math.inverse((quaternion)transform.rotation);
            IntPtr ownerData = GCHandle.ToIntPtr(_handle);
            foreach (Box3DShape shape in _shapes)
            {
                Transform shapeTransform = shape.transform;
                float3 localPosition = transform.InverseTransformPoint(shapeTransform.position);
                quaternion localRotation = math.mul(bodyInverse, shapeTransform.rotation);
                //CCX_MOD BEGIN(Register compound Shapes with their world and Body component)
                // shape.AttachTo(_body, localPosition, localRotation, shapeTransform.lossyScale,
                //     ownerData, _hitReceivers.Length > 0);
                shape.AttachTo(this, localPosition, localRotation, shapeTransform.lossyScale,
                    ownerData, _hitReceivers.Length > 0);
                //CCX_MOD END(Register compound Shapes with their world and Body component)
            }

            //CCX_MOD BEGIN(Support shape mass and density settings)
            ApplyMassOverride();
            //CCX_MOD END(Support shape mass and density settings)
            if (Type == Box3DBodyType.Kinematic) _world.AddKinematic(this);
            transform.hasChanged = false;
        }

        private void OnEnable()
        {
            if (!_body.IsValid) return;
            _body.SetTransform(transform.position, transform.rotation);
            _body.Enable();
            transform.hasChanged = false;
        }

        private void OnDisable()
        {
            if (_body.IsValid) _body.Disable();
        }

        private void OnDestroy()
        {
            if (Type == Box3DBodyType.Kinematic && _world) _world.RemoveKinematic(this);
            //CCX_MOD BEGIN(Unregister compound Shapes before destroying their Body)
            if (_shapes != null)
            {
                foreach (Box3DShape shape in _shapes)
                {
                    if (shape) shape.UnregisterFromWorld();
                }
            }
            //CCX_MOD END(Unregister compound Shapes before destroying their Body)
            if (_body.IsValid) _body.Destroy();
            // Free referenced geometry (meshes) only after the body — and its shapes — are gone.
            if (_shapes != null)
            {
                foreach (Box3DShape shape in _shapes)
                {
                    if (shape) shape.ReleaseGeometry();
                }
            }
            if (_handle.IsAllocated) _handle.Free();
        }

        // Collects shape components on this GameObject and descendants, stopping at any nested
        // Box3DBody (that subtree belongs to the other body). Unity's compound-collider gathering.
        // Internal so the editor physics simulation builds its preview bodies from the same rules.
        internal static void GatherShapes(Transform node, System.Collections.Generic.List<Box3DShape> result, bool isRoot)
        {
            if (!isRoot && node.GetComponent<Box3DBody>()) return;

            node.GetComponents(TempShapes);
            result.AddRange(TempShapes);

            for (int i = 0; i < node.childCount; i++)
            {
                GatherShapes(node.GetChild(i), result, isRoot: false);
            }
        }

        private static readonly System.Collections.Generic.List<Box3DShape> TempShapes =
            new System.Collections.Generic.List<Box3DShape>();

        // Hit receivers follow the same subtree rule as shapes: a nested Box3DBody owns the
        // receivers (and shapes) below it.
        private static void GatherHitReceivers(Transform node,
            System.Collections.Generic.List<IBox3DHitReceiver> result, bool isRoot)
        {
            if (!isRoot && node.GetComponent<Box3DBody>()) return;

            node.GetComponents(TempReceivers);
            result.AddRange(TempReceivers);

            for (int i = 0; i < node.childCount; i++)
            {
                GatherHitReceivers(node.GetChild(i), result, isRoot: false);
            }
        }

        private static readonly System.Collections.Generic.List<IBox3DHitReceiver> TempReceivers =
            new System.Collections.Generic.List<IBox3DHitReceiver>();

        /// <summary>True when this body has hit receivers (its shapes opted into hit events).</summary>
        internal bool WantsHits => _hitReceivers != null && _hitReceivers.Length > 0;

        /// <summary>Called by the world after each step for every hit event on this body's shapes.</summary>
        internal void DispatchHit(in Box3DHit hit)
        {
            foreach (IBox3DHitReceiver receiver in _hitReceivers)
            {
                if (receiver is Behaviour behaviour && (!behaviour || !behaviour.isActiveAndEnabled)) continue;
                receiver.OnBox3DHit(hit);
            }
        }

        /// <summary>Called by the world (kinematic list only) before each step.</summary>
        internal void PushKinematic(float deltaTime)
        {
            if (!isActiveAndEnabled || !_body.IsValid) return;

            //CCX_MOD BEGIN(Explicit movement overrides Transform following)
            if (_hasMovePosition || _hasMoveRotation) return;
            //CCX_MOD END(Explicit movement overrides Transform following)
            var target = new B3Transform { Position = transform.position, Rotation = transform.rotation };
            _body.SetTargetTransform(target, deltaTime, wake: true);
        }

        /// <summary>Called by the world after each step to write a body-move event to the Transform.</summary>
        internal void ApplyMoveEvent(B3WorldTransform moved)
        {
            // (Vector3) narrows in double mode — the component layer is render-space; large-world
            // users pair BOX3D_DOUBLE with a floating-origin strategy (see the docs).
            transform.SetPositionAndRotation((Vector3)moved.Position, moved.Rotation);
            transform.hasChanged = false; // our own write must not read back as a user move
        }

        //CCX_MOD BEGIN(Queue Rigidbody-style movement for the next physics step)
        private bool _hasMovePosition;
        private bool _hasMoveRotation;
        private Vector3 _movePosition;
        private Quaternion _moveRotation;
        private bool _moveWake;

        /// <summary>Moves towards a world position during the next physics step.</summary>
        public void MovePosition(Vector3 position, bool wake = true)
        {
            if (!_body.IsValid || !isActiveAndEnabled) return;
            _movePosition = position;
            _hasMovePosition = true;
            _moveWake = wake;
            _world.QueueMove(this);
        }

        /// <summary>Rotates towards a world rotation during the next physics step.</summary>
        public void MoveRotation(Quaternion rotation, bool wake = true)
        {
            if (!_body.IsValid || !isActiveAndEnabled) return;
            _moveRotation = rotation;
            _hasMoveRotation = true;
            _moveWake = wake;
            _world.QueueMove(this);
        }

        /// <summary>Moves and rotates during the next physics step. The last wake argument wins.</summary>
        public void Move(Vector3 position, Quaternion rotation, bool wake = true)
        {
            MovePosition(position, wake);
            MoveRotation(rotation, wake);
        }

        internal void SubmitMove(float deltaTime)
        {
            if (!_hasMovePosition && !_hasMoveRotation) return;
            bool hasPosition = _hasMovePosition;
            bool hasRotation = _hasMoveRotation;
            _hasMovePosition = _hasMoveRotation = false;
            if (!_body.IsValid || !isActiveAndEnabled) return;
            // Unspecified axes keep the normal Transform-following target for kinematic bodies.
            Vector3 targetPosition = hasPosition ? _movePosition :
                (Type == Box3DBodyType.Kinematic ? transform.position : GetPosition());
            Quaternion targetRotation = hasRotation ? _moveRotation :
                (Type == Box3DBodyType.Kinematic ? transform.rotation : GetRotation());
            // SetTargetTransform replaces both velocities. Preserve motion on channels
            // not explicitly targeted so MoveRotation does not cancel AddForce, and
            // MovePosition does not cancel torque on dynamic bodies.
            bool preserveLinear = Type == Box3DBodyType.Dynamic && !hasPosition;
            bool preserveAngular = Type == Box3DBodyType.Dynamic && !hasRotation;
            Vector3 previousLinearVelocity = preserveLinear ? GetLinearVelocity() : default;
            Vector3 previousAngularVelocity = preserveAngular ? GetAngularVelocity() : default;
            SetTargetTransform(new Pose(targetPosition, targetRotation), deltaTime, _moveWake);
            if (preserveLinear) SetLinearVelocity(previousLinearVelocity);
            if (preserveAngular) SetAngularVelocity(previousAngularVelocity);
        }
        //CCX_MOD END(Queue Rigidbody-style movement for the next physics step)

        private void Warp(Vector3 position, Quaternion rotation)
        {
            //CCX_MOD BEGIN(Cancel pending movement when teleporting)
            _hasMovePosition = _hasMoveRotation = false;
            //CCX_MOD END(Cancel pending movement when teleporting)
            transform.SetPositionAndRotation(position, rotation);
            if (_body.IsValid)
            {
                _body.SetTransform(position, rotation);
                if (Type == Box3DBodyType.Dynamic) _body.SetAwake(true);
            }
            transform.hasChanged = false;
        }

#if UNITY_EDITOR
        private void Update()
        {
            // Editor authoring convenience: pick up Scene-view Transform drags during play for
            // non-kinematic bodies (kinematic ones already follow the Transform). Cheap bool check,
            // editor-only — shipped builds never poll.
            if (!Application.isPlaying || Type == Box3DBodyType.Kinematic || !_body.IsValid) return;
            if (!transform.hasChanged) return;

            transform.hasChanged = false;
            _body.SetTransform(transform.position, transform.rotation);
            if (Type == Box3DBodyType.Dynamic) _body.SetAwake(true);
        }
#endif

#if UNITY_EDITOR
        private void OnValidate()
        {
            // The field already holds the new value here, so push it straight to the body and
            // refresh kinematic-list membership (Add/Remove are idempotent).
            if (!Application.isPlaying || !_body.IsValid) return;
            _body.SetType(ToNative(Type));
            _body.SetLinearDamping(LinearDamping);
            _body.SetAngularDamping(AngularDamping);
            //CCX_MOD BEGIN(Support shape mass and density settings)
            if (OverrideMass) ApplyMassOverride();
            else _body.ApplyMassFromShapes();
            //CCX_MOD END(Support shape mass and density settings)
            if (Type == Box3DBodyType.Kinematic) _world.AddKinematic(this);
            else _world.RemoveKinematic(this);
        }
#endif

        private static Box3D.BodyType ToNative(Box3DBodyType type)
        {
            switch (type)
            {
                case Box3DBodyType.Static: return Box3D.BodyType.Static;
                case Box3DBodyType.Kinematic: return Box3D.BodyType.Kinematic;
                default: return Box3D.BodyType.Dynamic;
            }
        }

        //CCX_MOD BEGIN(Add pre-integrate callback registration utility function)
        public void RegisterPreIntegratePositionsHandler(PreIntegratePositionsHandler handler)
        {
            _body.RegisterPreIntegratePositionsHandler(handler);
        }
        //CCX_MOD END(Add pre-integrate callback registration utility function)

        //CCX_MOD BEGIN(Forward Body generated API)
        public Box3DBodyType GetBodyType() => BodyType;
        public void SetType(Box3DBodyType type) => BodyType = type;
        public Vector3 GetPosition() => (Vector3)_body.GetPosition();
        public Quaternion GetRotation() => _body.GetRotation();
        public Pose GetTransform() => new Pose(GetPosition(), GetRotation());
        public void SetTransform(Vector3 position, Quaternion rotation) => Warp(position, rotation);
        public Vector3 GetLocalPoint(Vector3 worldPoint) => _body.GetLocalPoint(worldPoint);
        public Vector3 GetWorldPoint(Vector3 localPoint) => (Vector3)_body.GetWorldPoint(localPoint);
        public Vector3 GetLocalVector(Vector3 worldVector) => _body.GetLocalVector(worldVector);
        public Vector3 GetWorldVector(Vector3 localVector) => _body.GetWorldVector(localVector);
        public Vector3 GetLinearVelocity() => _body.GetLinearVelocity();
        public Vector3 GetAngularVelocity() => _body.GetAngularVelocity();
        public void SetLinearVelocity(Vector3 linearVelocity, bool wake = true) => _body.SetLinearVelocity(linearVelocity, wake);
        public void SetAngularVelocity(Vector3 angularVelocity, bool wake = true) => _body.SetAngularVelocity(angularVelocity, wake);
        public void SetTargetTransform(Pose target, float timeStep, bool wake = true) => _body.SetTargetTransform(new B3WorldTransform { Position = target.position, Rotation = target.rotation }, timeStep, wake);
        public Vector3 GetLocalPointVelocity(Vector3 localPoint) => _body.GetLocalPointVelocity(localPoint);
        public Vector3 GetWorldPointVelocity(Vector3 worldPoint) => _body.GetWorldPointVelocity(worldPoint);
        public void ApplyForce(Vector3 force, Vector3 point, bool wake = true) => _body.ApplyForce(force, point, wake);
        public void ApplyForceToCenter(Vector3 force, bool wake = true) => _body.ApplyForceToCenter(force, wake);
        public void ApplyTorque(Vector3 torque, bool wake = true) => _body.ApplyTorque(torque, wake);
        public void ApplyLinearImpulse(Vector3 impulse, Vector3 point, bool wake = true) => _body.ApplyLinearImpulse(impulse, point, wake);
        public void ApplyLinearImpulseToCenter(Vector3 impulse, bool wake = true) => _body.ApplyLinearImpulseToCenter(impulse, wake);
        public void ApplyAngularImpulse(Vector3 impulse, bool wake = true) => _body.ApplyAngularImpulse(impulse, wake);
        public float GetMass() => _body.GetMass();
        public Matrix4x4 GetLocalRotationalInertia() => ToUnityMatrix(_body.GetLocalRotationalInertia());
        public float GetInverseMass() => _body.GetInverseMass();
        public Matrix4x4 GetWorldInverseRotationalInertia() => ToUnityMatrix(_body.GetWorldInverseRotationalInertia());
        public Vector3 GetLocalCenter() => _body.GetLocalCenter();
        public Vector3 GetWorldCenter() => (Vector3)_body.GetWorldCenter();
        public void SetMassData(float mass, Vector3 center, Matrix4x4 inertia) => _body.SetMassData(new MassData { Mass = mass, Center = center, Inertia = ToNativeMatrix(inertia) });
        public (float mass, Vector3 center, Matrix4x4 inertia) GetMassData()
        {
            var data = _body.GetMassData();
            return (data.Mass, (Vector3)data.Center, ToUnityMatrix(data.Inertia));
        }
        public void ApplyMassFromShapes() => _body.ApplyMassFromShapes();
        public void SetLinearDamping(float linearDamping) { LinearDamping = linearDamping; _body.SetLinearDamping(linearDamping); }
        public float GetLinearDamping() => _body.GetLinearDamping();
        public void SetAngularDamping(float angularDamping) { AngularDamping = angularDamping; _body.SetAngularDamping(angularDamping); }
        public float GetAngularDamping() => _body.GetAngularDamping();
        public void SetGravityScale(float gravityScale) => _body.SetGravityScale(gravityScale);
        public float GetGravityScale() => _body.GetGravityScale();
        public void SetAwake(bool awake) => _body.SetAwake(awake);
        public void EnableSleep(bool enableSleep) => _body.EnableSleep(enableSleep);
        public bool IsSleepEnabled() => _body.IsSleepEnabled();
        public void SetSleepThreshold(float sleepThreshold) => _body.SetSleepThreshold(sleepThreshold);
        public float GetSleepThreshold() => _body.GetSleepThreshold();
        public void Disable() => _body.Disable();
        public void Enable() => _body.Enable();
        public void SetMotionLocks(RigidbodyConstraints locks) => constraints = locks;
        public RigidbodyConstraints GetMotionLocks() => constraints;
        public void SetBullet(bool flag) => _body.SetBullet(flag);
        public bool IsBullet() => _body.IsBullet();
        public void AllowFastRotation(bool flag) => _body.AllowFastRotation(flag);
        public bool IsFastRotationAllowed() => _body.IsFastRotationAllowed();
        public void EnableContactRecycling(bool flag) => _body.EnableContactRecycling(flag);
        public bool IsContactRecyclingEnabled() => _body.IsContactRecyclingEnabled();
        public void EnableHitEvents(bool flag) => _body.EnableHitEvents(flag);
        public Box3DWorld GetWorld() => _world;
        public int GetContactCapacity() => _body.GetContactCapacity();
        public Bounds ComputeAABB()
        {
            var aabb = _body.ComputeAABB();
            var bounds = new Bounds();
            bounds.SetMinMax(aabb.LowerBound, aabb.UpperBound);
            return bounds;
        }

        // Rigidbody-compatible names. Box3D-specific controls remain available above.
        public Vector3 position { get => GetPosition(); set => SetTransform(value, GetRotation()); }
        public Quaternion rotation { get => GetRotation(); set => SetTransform(GetPosition(), value); }
        public Vector3 linearVelocity { get => GetLinearVelocity(); set => SetLinearVelocity(value); }
        public Vector3 velocity { get => linearVelocity; set => linearVelocity = value; }
        public Vector3 angularVelocity { get => GetAngularVelocity(); set => SetAngularVelocity(value); }
        public float mass
        {
            get => GetMass();
            set { var data = _body.GetMassData(); data.Mass = value; _body.SetMassData(data); }
        }
        public Vector3 centerOfMass
        {
            get => GetLocalCenter();
            set { var data = _body.GetMassData(); data.Center = value; _body.SetMassData(data); }
        }
        public Vector3 worldCenterOfMass => GetWorldCenter();
        public float drag { get => GetLinearDamping(); set => SetLinearDamping(value); }
        public float angularDrag { get => GetAngularDamping(); set => SetAngularDamping(value); }
        public float linearDamping { get => GetLinearDamping(); set => SetLinearDamping(value); }
        public float angularDamping { get => GetAngularDamping(); set => SetAngularDamping(value); }
        public float sleepThreshold { get => GetSleepThreshold(); set => SetSleepThreshold(value); }
        public bool useGravity { get => GetGravityScale() != 0f; set => SetGravityScale(value ? 1f : 0f); }
        public bool isKinematic
        {
            get => BodyType == Box3DBodyType.Kinematic;
            set => BodyType = value ? Box3DBodyType.Kinematic : Box3DBodyType.Dynamic;
        }
        public RigidbodyConstraints constraints
        {
            get
            {
                var locks = _body.GetMotionLocks();
                var result = RigidbodyConstraints.None;
                if (locks.LinearX) result |= RigidbodyConstraints.FreezePositionX;
                if (locks.LinearY) result |= RigidbodyConstraints.FreezePositionY;
                if (locks.LinearZ) result |= RigidbodyConstraints.FreezePositionZ;
                if (locks.AngularX) result |= RigidbodyConstraints.FreezeRotationX;
                if (locks.AngularY) result |= RigidbodyConstraints.FreezeRotationY;
                if (locks.AngularZ) result |= RigidbodyConstraints.FreezeRotationZ;
                return result;
            }
            set => _body.SetMotionLocks(new MotionLocks
            {
                LinearX = (value & RigidbodyConstraints.FreezePositionX) != 0,
                LinearY = (value & RigidbodyConstraints.FreezePositionY) != 0,
                LinearZ = (value & RigidbodyConstraints.FreezePositionZ) != 0,
                AngularX = (value & RigidbodyConstraints.FreezeRotationX) != 0,
                AngularY = (value & RigidbodyConstraints.FreezeRotationY) != 0,
                AngularZ = (value & RigidbodyConstraints.FreezeRotationZ) != 0,
            });
        }
        public bool IsSleeping() => !_body.IsAwake;
        public void Sleep() => SetAwake(false);
        public void WakeUp() => SetAwake(true);
        public void ResetCenterOfMass()
        {
            var previous = _body.GetMassData();
            _body.ApplyMassFromShapes();
            var computed = _body.GetMassData();
            previous.Center = computed.Center;
            _body.SetMassData(previous);
        }
        public void ResetInertiaTensor()
        {
            var previous = _body.GetMassData();
            _body.ApplyMassFromShapes();
            var computed = _body.GetMassData();
            previous.Inertia = computed.Inertia;
            _body.SetMassData(previous);
        }
        public Vector3 GetPointVelocity(Vector3 worldPoint) => GetWorldPointVelocity(worldPoint);
        public Vector3 GetRelativePointVelocity(Vector3 relativePoint) => GetLocalPointVelocity(relativePoint);

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force, bool wake = true)
        {
            switch (mode)
            {
                case ForceMode.Force: _body.ApplyForceToCenter(force, wake); break;
                case ForceMode.Acceleration: _body.ApplyForceToCenter(force * GetMass(), wake); break;
                case ForceMode.Impulse: _body.ApplyLinearImpulseToCenter(force, wake); break;
                case ForceMode.VelocityChange: _body.ApplyLinearImpulseToCenter(force * GetMass(), wake); break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force, bool wake = true)
        {
            switch (mode)
            {
                case ForceMode.Force: _body.ApplyForce(force, position, wake); break;
                case ForceMode.Acceleration: _body.ApplyForce(force * GetMass(), position, wake); break;
                case ForceMode.Impulse: _body.ApplyLinearImpulse(force, position, wake); break;
                case ForceMode.VelocityChange: _body.ApplyLinearImpulse(force * GetMass(), position, wake); break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force, bool wake = true)
        {
            switch (mode)
            {
                case ForceMode.Force: _body.ApplyTorque(torque, wake); break;
                case ForceMode.Acceleration: _body.ApplyTorque(ToWorldInertia(torque), wake); break;
                case ForceMode.Impulse: _body.ApplyAngularImpulse(torque, wake); break;
                case ForceMode.VelocityChange: _body.ApplyAngularImpulse(ToWorldInertia(torque), wake); break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        public void AddRelativeForce(Vector3 force, ForceMode mode = ForceMode.Force, bool wake = true) => AddForce(GetRotation() * force, mode, wake);
        public void AddRelativeTorque(Vector3 torque, ForceMode mode = ForceMode.Force, bool wake = true) => AddTorque(GetRotation() * torque, mode, wake);
        public void AddForce(float x, float y, float z, ForceMode mode = ForceMode.Force, bool wake = true) => AddForce(new Vector3(x, y, z), mode, wake);
        public void AddTorque(float x, float y, float z, ForceMode mode = ForceMode.Force, bool wake = true) => AddTorque(new Vector3(x, y, z), mode, wake);
        public void AddRelativeForce(float x, float y, float z, ForceMode mode = ForceMode.Force, bool wake = true) => AddRelativeForce(new Vector3(x, y, z), mode, wake);
        public void AddRelativeTorque(float x, float y, float z, ForceMode mode = ForceMode.Force, bool wake = true) => AddRelativeTorque(new Vector3(x, y, z), mode, wake);

        private Vector3 ToWorldInertia(Vector3 angularAcceleration)
        {
            Quaternion orientation = GetRotation();
            return orientation * GetLocalRotationalInertia().MultiplyVector(Quaternion.Inverse(orientation) * angularAcceleration);
        }
        // The upper-left 3x3 stores the inertia tensor; the homogeneous element is one.
        private static Matrix4x4 ToUnityMatrix(B3Matrix3 value) => new Matrix4x4(
            new Vector4(value.Cx.x, value.Cx.y, value.Cx.z, 0f),
            new Vector4(value.Cy.x, value.Cy.y, value.Cy.z, 0f),
            new Vector4(value.Cz.x, value.Cz.y, value.Cz.z, 0f),
            new Vector4(0f, 0f, 0f, 1f));
        private static B3Matrix3 ToNativeMatrix(Matrix4x4 value) => new B3Matrix3
        {
            Cx = new float3(value.m00, value.m10, value.m20),
            Cy = new float3(value.m01, value.m11, value.m21),
            Cz = new float3(value.m02, value.m12, value.m22),
        };
        //CCX_MOD END(Forward Body generated API)
        //CCX_MOD BEGIN(Support shape mass and density settings)
        public float CalculatedMass
        {
            get
            {
                if (Type != Box3DBodyType.Dynamic) return 0f;
                if (OverrideMass) return MassOverride;
                var shapes = new System.Collections.Generic.List<Box3DShape>();
                GatherShapes(transform, shapes, true);
                float total = 0f;
                foreach (var shape in shapes)
                    if (shape is Box3DDynamicBodyShape dynamicShape &&
                        Box3DDynamicBodyShape.IsValidVolume(dynamicShape.CalculateVolume()))
                        total += dynamicShape.CalculatedMass;
                return total;
            }
        }

        internal void ApplyMassOverride()
        {
            if (!OverrideMass || !_body.IsValid || Type != Box3DBodyType.Dynamic) return;
            var data = _body.GetMassData();
            data.Mass = MassOverride;
            _body.SetMassData(data);
        }

        //CCX_MOD END(Support shape mass and density settings)
    }
}
