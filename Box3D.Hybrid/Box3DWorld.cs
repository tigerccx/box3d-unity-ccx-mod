using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Box3D.Hybrid
{
    //CCX_MOD BEGIN(Step after script FixedUpdate)
    /// <summary>Scene-level owner of a Box3D simulation world. Steps after all script FixedUpdates, pushes
    /// kinematic bodies before the step and syncs moved bodies back after. Auto-created on demand
    /// (like Unity's single physics world); place one explicitly to tune gravity, sub-steps, or
    /// worker count.</summary>
    //CCX_MOD END(Step after script FixedUpdate)
    [Icon("Packages/com.suvitruf.box3d/Box3D.Hybrid.Editor/Icons/Box3DWorld.png")]
    [AddComponentMenu("Box3D/World")]
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class Box3DWorld : MonoBehaviour
    {
        [SerializeField, Tooltip("Gravity vector applied to every dynamic body.")]
        private Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        [SerializeField, Min(1), Tooltip("Solver sub-steps per step. Higher = stiffer, slower.")]
        private int SubStepCount = 4;

        [SerializeField, Min(0), Tooltip("Box3D worker threads. 0 = auto (about half the logical cores).")]
        private int WorkerCount = 0;

        [SerializeField, Tooltip("Overlay physics debug geometry in the Scene view each frame (None = off). " +
            "Enable Contacts / Islands / Forces / Bounds to see the solver's view of the world. " +
            "For the Game view, turn on its Gizmos toggle.")]
        private DebugDrawFlags DebugDraw = DebugDrawFlags.None;

        [SerializeField, Min(1f), Tooltip("Half-size of the box around the origin that debug drawing covers.")]
        private float DebugDrawRadius = 200f;

        // Only kinematic bodies need per-frame attention (they follow their Transform). Dynamic
        // bodies sync back through move events — which report only bodies that actually moved — so
        // they never appear here. Bodies map back to their component through a GCHandle in userData,
        // so no all-bodies list is kept.
        private readonly List<Box3DBody> _kinematicBodies = new List<Box3DBody>();

        private World _world;
        private Body _anchor;
        private static Box3DWorld _instance;

        /// <summary>The underlying Box3D world (valid after this component is enabled).</summary>
        public World World => _world;

        /// <summary>When true, the world stops stepping (bodies stay put). The visual replayer sets this
        /// so live physics doesn't fight the replayed transforms.</summary>
        public bool Paused { get; set; }

        /// <summary>The configured gravity vector (readable without a live world — the rope's
        /// editor preview settles under the same gravity the simulation will use).</summary>
        public Vector3 GravityVector => Gravity;

        /// <summary>A shared static body at the origin, used as the fixed endpoint for joints whose
        /// connected body is null (like Unity's null connectedBody = attach to the world).</summary>
        public Body WorldAnchor
        {
            get
            {
                if (!_anchor.IsValid)
                {
                    EnsureCreated();
                    _anchor = _world.CreateBody(BodyDef.Default); // static at origin
                }
                return _anchor;
            }
        }

        /// <summary>The active world component, creating one if the scene has none.</summary>
        public static Box3DWorld Instance
        {
            get
            {
                if (!_instance)
                {
                    _instance = FindAnyObjectByType<Box3DWorld>();
                    if (!_instance)
                    {
                        _instance = new GameObject("Box3D World").AddComponent<Box3DWorld>();
                    }
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance && _instance != this)
            {
                Debug.LogWarning("[Box3D] Multiple Box3DWorld components — only the first is used.", this);
            }
            _instance = this;
            EnsureCreated();
        }

        private void EnsureCreated()
        {
            if (_world.IsValid) return;

            WorldDef def = WorldDef.Default;
            def.Gravity = Gravity;
            def.WorkerCount = (uint)(WorkerCount > 0 ? WorkerCount : Mathf.Max(1, SystemInfo.processorCount / 2));
            _world = World.Create(def);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Push Inspector edits to the live world during play. SubStepCount and DebugDraw are
            // read every frame anyway; WorkerCount is baked at world creation.
            if (!Application.isPlaying || !_world.IsValid) return;
            _world.SetGravity(Gravity);
        }
#endif

        //CCX_MOD BEGIN(Submit queued Rigidbody-style moves before stepping)
        private readonly List<Box3DBody> _pendingMoves = new List<Box3DBody>();

        internal void QueueMove(Box3DBody body)
        {
            if (!_pendingMoves.Contains(body)) _pendingMoves.Add(body);
        }

        private void SubmitPendingMoves(float deltaTime)
        {
            foreach (var body in _pendingMoves)
            {
                if (body) body.SubmitMove(deltaTime);
            }
            _pendingMoves.Clear();
        }
        //CCX_MOD END(Submit queued Rigidbody-style moves before stepping)

        internal void AddKinematic(Box3DBody body)
        {
            if (!_kinematicBodies.Contains(body)) _kinematicBodies.Add(body);
        }

        internal void RemoveKinematic(Box3DBody body)
        {
            _kinematicBodies.Remove(body);
        }

        //CCX_MOD BEGIN(Step from the custom fixed player-loop stage)
        private void StepPhysics()
        //CCX_MOD END(Step from the custom fixed player-loop stage)
        {
            if (Paused || !_world.IsValid) return;

            float deltaTime = Time.fixedDeltaTime;
            for (int i = 0; i < _kinematicBodies.Count; i++)
            {
                Box3DBody body = _kinematicBodies[i];
                if (body) body.PushKinematic(deltaTime);
            }

            //CCX_MOD BEGIN(Apply explicit moves after Transform-following targets)
            SubmitPendingMoves(deltaTime);
            //CCX_MOD END(Apply explicit moves after Transform-following targets)
            _world.Step(deltaTime, SubStepCount);

            foreach (BodyMoveEvent moveEvent in _world.GetBodyMoveEvents())
            {
                if (moveEvent.UserData == IntPtr.Zero) continue;
                if (GCHandle.FromIntPtr(moveEvent.UserData).Target is Box3DBody body)
                {
                    body.ApplyMoveEvent(moveEvent.Transform);
                }
            }

            // Impacts, delivered after the move sync so receivers see settled transforms. The
            // native normal points from shape A to shape B: it is the impact direction into B,
            // its negation the impact direction into A.
            foreach (ContactHitEvent hitEvent in _world.GetContactEvents().Hit)
            {
                Box3DBody bodyA = BodyFromShape(hitEvent.ShapeIdA);
                Box3DBody bodyB = BodyFromShape(hitEvent.ShapeIdB);
                if (bodyA && bodyA.WantsHits)
                {
                    bodyA.DispatchHit(new Box3DHit { Point = (Vector3)hitEvent.Point, Direction = -hitEvent.Normal,
                        ApproachSpeed = hitEvent.ApproachSpeed, OtherBody = bodyB });
                }
                if (bodyB && bodyB.WantsHits)
                {
                    bodyB.DispatchHit(new Box3DHit { Point = (Vector3)hitEvent.Point, Direction = hitEvent.Normal,
                        ApproachSpeed = hitEvent.ApproachSpeed, OtherBody = bodyA });
                }
            }
        }

        // Maps a hit event's shape back to its component through the body GCHandle in shape userData
        // (zero for shapes that created their own static body, or preview shapes).
        private static Box3DBody BodyFromShape(ShapeId shapeId)
        {
            var shape = new Shape { Id = shapeId };
            if (!shape.IsValid) return null;
            IntPtr userData = shape.UserData;
            return userData == IntPtr.Zero ? null : GCHandle.FromIntPtr(userData).Target as Box3DBody;
        }

        private void LateUpdate()
        {
            // Debug overlay: box3d emits its geometry through the debug-draw bridge as Scene-view lines.
            // Drawn after the step + transform sync so it reflects the current pose.
            if (DebugDraw != DebugDrawFlags.None && _world.IsValid)
            {
                _world.DrawDebug(DebugDraw, DebugDrawRadius);
            }
        }

        // Matches the world component's purple icon so the arrow reads as "the world's gravity".
        private static readonly Color GravityGizmoColor = new Color(0.75f, 0.53f, 0.92f, 0.95f);

        private void OnDrawGizmosSelected()
        {
            float magnitude = Gravity.magnitude;
            if (magnitude < 1e-4f) return; // zero gravity — nothing to point at

            Vector3 origin = transform.position;
            Vector3 dir = Gravity / magnitude;
            // Shaft length tracks strength (1 g ≈ 1.5 m), clamped so extreme values stay readable.
            float length = Mathf.Clamp(1.5f * magnitude / 9.81f, 0.4f, 4f);
            Vector3 tip = origin + dir * length;

            // A basis perpendicular to the arrow for the head fins (gravity is usually straight
            // down, where Vector3.up is degenerate — fall back to right).
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();
            Vector3 side2 = Vector3.Cross(dir, side);

            Gizmos.color = GravityGizmoColor;
            Gizmos.DrawLine(origin, tip);
            float head = Mathf.Min(0.3f * length, 0.4f);
            foreach (Vector3 fin in new[] { side, -side, side2, -side2 })
            {
                Gizmos.DrawLine(tip, tip - dir * head + fin * (head * 0.5f));
            }
        }

        //CCX_MOD BEGIN(Run Box3D after all script FixedUpdates)
        // Identifies the custom stage in the PlayerLoop and Profiler.
        private struct Box3DPhysicsUpdate { }
        private static readonly List<Box3DWorld> ActiveWorlds = new List<Box3DWorld>();
        private static Box3DWorld[] _steppingWorlds = Array.Empty<Box3DWorld>();

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            RegisterForPhysicsUpdate(this);
        }

        private void OnDisable()
        {
            ActiveWorlds.Remove(this);
            _steppingWorlds = ActiveWorlds.ToArray();
        }

        private static void RegisterForPhysicsUpdate(Box3DWorld world)
        {
            if (ActiveWorlds.Contains(world)) return;
            InstallPhysicsUpdate();
            ActiveWorlds.Add(world);
            _steppingWorlds = ActiveWorlds.ToArray();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPhysicsUpdate()
        {
            RemovePhysicsUpdate();
            ActiveWorlds.Clear();
            _steppingWorlds = Array.Empty<Box3DWorld>();
            Application.quitting -= RemovePhysicsUpdate;
            Application.quitting += RemovePhysicsUpdate;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= RemovePhysicsUpdate;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RemovePhysicsUpdate;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializePhysicsUpdate()
        {
            InstallPhysicsUpdate();
            // With scene reload disabled, existing components may not receive OnEnable again.
            foreach (var world in FindObjectsByType<Box3DWorld>(FindObjectsSortMode.None))
            {
                if (world.isActiveAndEnabled) RegisterForPhysicsUpdate(world);
            }
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) RemovePhysicsUpdate();
        }
#endif

        private static void RunPhysicsUpdate()
        {
            if (!Application.isPlaying) return;
            // Registration changes during callbacks affect the next tick, without per-tick allocations.
            var worlds = _steppingWorlds;
            foreach (var world in worlds)
            {
                if (!world || !world.isActiveAndEnabled) continue;
                try { world.StepPhysics(); }
                catch (Exception exception) { Debug.LogException(exception, world); }
            }
        }

        private static void InstallPhysicsUpdate()
        {
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            RemovePhysicsUpdateNode(ref loop);
            if (!InsertPhysicsUpdateNode(ref loop))
                throw new InvalidOperationException("Box3D could not find ScriptRunBehaviourFixedUpdate in the current PlayerLoop.");
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool InsertPhysicsUpdateNode(ref UnityEngine.LowLevel.PlayerLoopSystem parent)
        {
            var children = parent.subSystemList;
            if (children == null) return false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate))
                {
                    var expanded = new UnityEngine.LowLevel.PlayerLoopSystem[children.Length + 1];
                    Array.Copy(children, 0, expanded, 0, i + 1);
                    expanded[i + 1] = new UnityEngine.LowLevel.PlayerLoopSystem
                    {
                        type = typeof(Box3DPhysicsUpdate),
                        updateDelegate = RunPhysicsUpdate,
                    };
                    Array.Copy(children, i + 1, expanded, i + 2, children.Length - i - 1);
                    parent.subSystemList = expanded;
                    return true;
                }
                if (InsertPhysicsUpdateNode(ref children[i])) return true;
            }
            return false;
        }

        private static void RemovePhysicsUpdate()
        {
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            if (RemovePhysicsUpdateNode(ref loop)) UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool RemovePhysicsUpdateNode(ref UnityEngine.LowLevel.PlayerLoopSystem parent)
        {
            var children = parent.subSystemList;
            if (children == null) return false;
            bool changed = false;
            var kept = new List<UnityEngine.LowLevel.PlayerLoopSystem>(children.Length);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == typeof(Box3DPhysicsUpdate))
                {
                    changed = true;
                    continue;
                }
                changed |= RemovePhysicsUpdateNode(ref children[i]);
                kept.Add(children[i]);
            }
            if (changed) parent.subSystemList = kept.ToArray();
            return changed;
        }
        //CCX_MOD END(Run Box3D after all script FixedUpdates)

        private void OnDestroy()
        {
            //CCX_MOD BEGIN(Clear world-owned Shape component registrations on destruction)
            _shapeRegistry.Clear();
            //CCX_MOD BEGIN(Release reusable query results on world destruction)
            ClearReusableQueryBuffers();
            //CCX_MOD END(Release reusable query results on world destruction)
            //CCX_MOD END(Clear world-owned Shape component registrations on destruction)
            if (_world.IsValid) _world.Destroy();
            if (_instance == this) _instance = null;
        }

        //CCX_MOD BEGIN(Map native Shape handles to components within the owning world)
        private readonly Dictionary<ShapeId, Box3DShape> _shapeRegistry = new Dictionary<ShapeId, Box3DShape>();

        /// <summary>Resolves a live component-owned Shape in this world. Native-only and detached
        /// editor-preview shapes have no registered component and return false. Use on the main thread.</summary>
        public bool TryGetShape(ShapeId shapeId, out Box3DShape shape)
        {
            shape = null;
            if (!_world.IsValid || !_shapeRegistry.TryGetValue(shapeId, out Box3DShape registered)) return false;
            if (!registered || !registered.Shape.Id.Equals(shapeId) || !registered.Shape.IsValid)
            {
                _shapeRegistry.Remove(shapeId);
                return false;
            }
            shape = registered;
            return true;
        }

        /// <summary>Resolves the packed Shape handle returned by Query_ overlap and cast methods.</summary>
        public bool TryGetShape(ulong shapeId, out Box3DShape shape)
            => TryGetShape(ShapeId.FromUInt64(shapeId), out shape);

        internal void RegisterShape(Box3DShape shape)
        {
            if (!shape || !shape.Shape.IsValid) return;
            if (!_world.IsValid || !shape.Shape.GetWorld().Equals(_world.Id))
                throw new ArgumentException("Cannot register a Shape from another world.", nameof(shape));
            _shapeRegistry[shape.Shape.Id] = shape;
        }

        internal void UnregisterShape(ShapeId shapeId, Box3DShape shape)
        {
            if (_shapeRegistry.TryGetValue(shapeId, out Box3DShape registered) && ReferenceEquals(registered, shape))
                _shapeRegistry.Remove(shapeId);
        }
        //CCX_MOD END(Map native Shape handles to components within the owning world)

        //CCX_MOD BEGIN(Expose World queries with Unity geometry and layer masks)
        /// <summary>A Box3D cast hit with Unity vectors and its registered Shape component.
        /// shape is null for native-only shapes; shapeId retains the native ShapeId.ToUInt64() handle.
        /// Casts are unordered and stop when the supplied buffer fills. Empty buffers still test for a hit;
        /// the returned hits slice is empty when no entries can be stored, even if the query returns true.</summary>
        public struct QueryHit
        {
            public Box3DShape shape;
            public ulong shapeId;
            public Vector3 point;
            public Vector3 normal;
            public float fraction;
            public float distance;
            public ulong userMaterialId;
            public int triangleIndex;
            public int childIndex;
        }

        /// <summary>Finds shapes whose broadphase bounds overlap bounds (not an exact shape test).
        /// Returns the number of entries written; native-only shapes appear as null entries.
        /// A full buffer can mean truncated results.
        /// queryLayer selects the querying object's layer; -1 accepts every target mask.</summary>
        public int Query_OverlapAABB(Bounds bounds, LayerMask layerMask, Span<Box3DShape> overlapShapes, int queryLayer = -1)
            => Query_OverlapAABB(bounds, layerMask, overlapShapes, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public int Query_OverlapAABB(Bounds bounds, LayerMask layerMask, Span<Box3DShape> overlapShapes,
            out Vector2Int treeStats, int queryLayer = -1)
        {
            QueryFilter filter = BuildQueryFilter(layerMask, queryLayer);
            treeStats = default;
            if (overlapShapes.IsEmpty) return 0;
            ShapeId[] rented = null;
            Span<ShapeId> nativeResults = overlapShapes.Length <= 128
                ? stackalloc ShapeId[overlapShapes.Length]
                : (rented = System.Buffers.ArrayPool<ShapeId>.Shared.Rent(overlapShapes.Length)).AsSpan(0, overlapShapes.Length);
            try
            {
                var aabb = new B3Aabb { LowerBound = bounds.min, UpperBound = bounds.max };
                int count = _world.OverlapAABB(aabb, filter, nativeResults, out TreeStats stats);
                treeStats = new Vector2Int(stats.NodeVisits, stats.LeafVisits);
                for (int i = 0; i < count; i++)
                {
                    TryGetShape(nativeResults[i], out Box3DShape component);
                    overlapShapes[i] = component;
                }
                return count;
            }
            finally
            {
                if (rented != null) System.Buffers.ArrayPool<ShapeId>.Shared.Return(rented);
            }
        }

        /// <summary>Overlaps a convex proxy translated by origin. Points are offsets from origin:
        /// one point plus radius forms a sphere, two a capsule, otherwise a convex point cloud.
        /// Native-only shapes appear as null entries; the returned count includes those entries.</summary>
        public int Query_OverlapShape(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            LayerMask layerMask, Span<Box3DShape> overlapShapes, int queryLayer = -1)
            => Query_OverlapShape(origin, proxyPoints, proxyRadius, layerMask, overlapShapes, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public int Query_OverlapShape(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            LayerMask layerMask, Span<Box3DShape> overlapShapes, out Vector2Int treeStats, int queryLayer = -1)
        {
            QueryFilter filter = BuildQueryFilter(layerMask, queryLayer);
            ValidateQueryProxy(proxyPoints, proxyRadius);
            treeStats = default;
            if (overlapShapes.IsEmpty) return 0;
            ShapeId[] rentedResults = null;
            Unity.Mathematics.float3[] rentedPoints = null;
            Span<ShapeId> nativeResults = overlapShapes.Length <= 128
                ? stackalloc ShapeId[overlapShapes.Length]
                : (rentedResults = System.Buffers.ArrayPool<ShapeId>.Shared.Rent(overlapShapes.Length)).AsSpan(0, overlapShapes.Length);
            try
            {
                Span<Unity.Mathematics.float3> points = proxyPoints.Length <= 128
                    ? stackalloc Unity.Mathematics.float3[proxyPoints.Length]
                    : (rentedPoints = System.Buffers.ArrayPool<Unity.Mathematics.float3>.Shared.Rent(proxyPoints.Length)).AsSpan(0, proxyPoints.Length);
                for (int i = 0; i < points.Length; i++) points[i] = proxyPoints[i];
                int count = _world.OverlapShape(origin, points, proxyRadius, filter, nativeResults, out TreeStats stats);
                treeStats = new Vector2Int(stats.NodeVisits, stats.LeafVisits);
                for (int i = 0; i < count; i++)
                {
                    TryGetShape(nativeResults[i], out Box3DShape component);
                    overlapShapes[i] = component;
                }
                return count;
            }
            finally
            {
                if (rentedPoints != null) System.Buffers.ArrayPool<Unity.Mathematics.float3>.Shared.Return(rentedPoints);
                if (rentedResults != null) System.Buffers.ArrayPool<ShapeId>.Shared.Return(rentedResults);
            }
        }

        /// <summary>Returns true if a hit is detected. hits is the valid, unordered slice of buffer;
        /// it aliases caller-owned memory without an additional copy. translation is the full displacement.</summary>
        public bool Query_CastRay(Vector3 origin, Vector3 translation, LayerMask layerMask,
            Span<QueryHit> buffer, out Span<QueryHit> hits, int queryLayer = -1)
            => Query_CastRay(origin, translation, layerMask, buffer, out hits, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public bool Query_CastRay(Vector3 origin, Vector3 translation, LayerMask layerMask,
            Span<QueryHit> buffer, out Span<QueryHit> hits, out Vector2Int treeStats, int queryLayer = -1)
        {
            QueryFilter filter = BuildQueryFilter(layerMask, queryLayer);
            treeStats = default;
            hits = buffer.Slice(0, 0);
            int capacity = Math.Max(1, buffer.Length);
            RayHit[] rented = null;
            Span<RayHit> nativeHits = capacity <= 128
                ? stackalloc RayHit[capacity]
                : (rented = System.Buffers.ArrayPool<RayHit>.Shared.Rent(capacity)).AsSpan(0, capacity);
            try
            {
                int count = _world.CastRay(origin, translation, filter, nativeHits, out TreeStats stats);
                treeStats = new Vector2Int(stats.NodeVisits, stats.LeafVisits);
                int hitCount = Math.Min(count, buffer.Length);
                CopyQueryHits(nativeHits.Slice(0, hitCount), buffer, translation.magnitude);
                hits = buffer.Slice(0, hitCount);
                return count > 0;
            }
            finally
            {
                if (rented != null) System.Buffers.ArrayPool<RayHit>.Shared.Return(rented);
            }
        }

        /// <summary>Tests whether the ray hits anything. translation is the complete displacement.
        /// Uses a single stack-allocated native hit without allocating an output array.</summary>
        public bool Query_CastRay(Vector3 origin, Vector3 translation, LayerMask layerMask, int queryLayer = -1)
        {
            QueryFilter filter = BuildQueryFilter(layerMask, queryLayer);
            Span<RayHit> nativeHits = stackalloc RayHit[1];
            return _world.CastRay(origin, translation, filter, nativeHits) > 0;
        }

        /// <summary>Returns true if a hit is detected. hits is the valid, unordered slice of buffer;
        /// it aliases caller-owned memory without an additional copy. translation is the full displacement.</summary>
        public bool Query_CastShape(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            Vector3 translation, LayerMask layerMask,
            Span<QueryHit> buffer, out Span<QueryHit> hits, int queryLayer = -1)
            => Query_CastShape(origin, proxyPoints, proxyRadius, translation, layerMask, buffer, out hits, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public bool Query_CastShape(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            Vector3 translation, LayerMask layerMask,
            Span<QueryHit> buffer, out Span<QueryHit> hits, out Vector2Int treeStats, int queryLayer = -1)
        {
            bool detected = CastShapeIntoBuffer(origin, proxyPoints, proxyRadius, translation,
                layerMask, buffer, out int hitCount, out treeStats, queryLayer);
            hits = buffer.Slice(0, hitCount);
            return detected;
        }

        /// <summary>Tests whether a convex proxy sweep hits anything, without returning hit details.</summary>
        public bool Query_CastShape(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            Vector3 translation, LayerMask layerMask, int queryLayer = -1)
            => CastShapeIntoBuffer(origin, proxyPoints, proxyRadius, translation, layerMask,
                Span<QueryHit>.Empty, out _, out _, queryLayer);

        private bool CastShapeIntoBuffer(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            Vector3 translation, LayerMask layerMask,
            Span<QueryHit> buffer, out int hitCount, out Vector2Int treeStats, int queryLayer = -1)
        {
            QueryFilter filter = BuildQueryFilter(layerMask, queryLayer);
            ValidateQueryProxy(proxyPoints, proxyRadius);
            treeStats = default;
            hitCount = 0;
            int capacity = Math.Max(1, buffer.Length);
            RayHit[] rentedHits = null;
            Unity.Mathematics.float3[] rentedPoints = null;
            Span<RayHit> nativeHits = capacity <= 128
                ? stackalloc RayHit[capacity]
                : (rentedHits = System.Buffers.ArrayPool<RayHit>.Shared.Rent(capacity)).AsSpan(0, capacity);
            try
            {
                Span<Unity.Mathematics.float3> points = proxyPoints.Length <= 128
                    ? stackalloc Unity.Mathematics.float3[proxyPoints.Length]
                    : (rentedPoints = System.Buffers.ArrayPool<Unity.Mathematics.float3>.Shared.Rent(proxyPoints.Length)).AsSpan(0, proxyPoints.Length);
                for (int i = 0; i < points.Length; i++) points[i] = proxyPoints[i];
                int count = _world.CastShape(origin, points, proxyRadius, translation, filter, nativeHits, out TreeStats stats);
                treeStats = new Vector2Int(stats.NodeVisits, stats.LeafVisits);
                hitCount = Math.Min(count, buffer.Length);
                CopyQueryHits(nativeHits.Slice(0, hitCount), buffer, translation.magnitude);
                return count > 0;
            }
            finally
            {
                if (rentedPoints != null) System.Buffers.ArrayPool<Unity.Mathematics.float3>.Shared.Return(rentedPoints);
                if (rentedHits != null) System.Buffers.ArrayPool<RayHit>.Shared.Return(rentedHits);
            }
        }

        private QueryFilter BuildQueryFilter(LayerMask layerMask, int queryLayer)
        {
            if (!_world.IsValid) throw new InvalidOperationException("The Box3D world has not been created or has been destroyed.");
            if (queryLayer < -1 || queryLayer > 31) throw new ArgumentOutOfRangeException(nameof(queryLayer));
            var filter = QueryFilter.Default;
            filter.MaskBits = unchecked((uint)layerMask.value);
            filter.CategoryBits = queryLayer >= 0 ? 1UL << queryLayer : uint.MaxValue;
            return filter;
        }

        private static void ValidateQueryProxy(ReadOnlySpan<Vector3> points, float radius)
        {
            if (points.IsEmpty) throw new ArgumentException("A query proxy needs at least one point.", nameof(points));
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));
        }

        private void CopyQueryHits(ReadOnlySpan<RayHit> source, Span<QueryHit> destination, float castLength)
        {
            for (int i = 0; i < source.Length; i++)
            {
                RayHit hit = source[i];
                TryGetShape(hit.ShapeId, out Box3DShape component);
                destination[i] = new QueryHit
                {
                    shape = component,
                    shapeId = hit.ShapeId.ToUInt64(),
                    point = hit.Point,
                    normal = hit.Normal,
                    fraction = hit.Fraction,
                    distance = hit.Fraction * castLength,
                    userMaterialId = hit.UserMaterialId,
                    triangleIndex = hit.TriangleIndex,
                    childIndex = hit.ChildIndex,
                };
            }
        }

        /// <summary>Returns true if a hit is detected. hits is the valid, unordered slice of buffer;
        /// it aliases caller-owned memory without an additional copy. translation is the full displacement.</summary>
        public bool Query_CastSphere(Vector3 origin, float radius, Vector3 translation,
            LayerMask layerMask,
            Span<QueryHit> buffer, out Span<QueryHit> hits, int queryLayer = -1)
            => Query_CastSphere(origin, radius, translation, layerMask, buffer, out hits, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public bool Query_CastSphere(Vector3 origin, float radius, Vector3 translation,
            LayerMask layerMask,
            Span<QueryHit> buffer, out Span<QueryHit> hits, out Vector2Int treeStats, int queryLayer = -1)
        {
            ReadOnlySpan<Vector3> points = stackalloc Vector3[] { Vector3.zero };
            bool detected = CastShapeIntoBuffer(origin, points, radius, translation, layerMask,
                buffer, out int hitCount, out treeStats, queryLayer);
            hits = buffer.Slice(0, hitCount);
            return detected;
        }

        /// <summary>Tests whether a sphere sweep hits anything, without returning hit details.</summary>
        public bool Query_CastSphere(Vector3 origin, float radius, Vector3 translation,
            LayerMask layerMask, int queryLayer = -1)
        {
            ReadOnlySpan<Vector3> points = stackalloc Vector3[] { Vector3.zero };
            return Query_CastShape(origin, points, radius, translation, layerMask, queryLayer);
        }
        //CCX_MOD END(Expose World queries with Unity geometry and layer masks)
        //CCX_MOD BEGIN(Reuse world-owned arrays for component query results)
        private QueryHit[] _reusableQueryHits = Array.Empty<QueryHit>();
        private Box3DShape[] _reusableOverlapShapes = Array.Empty<Box3DShape>();
        private int _reusableQueryHitCount;
        private int _reusableOverlapShapeCount;

        // These views borrow world-owned memory. Consume them before the next buffered query of
        // the same kind (all Casts share one array; all Overlaps share another). Use caller-buffer
        // overloads when results must coexist. maxHits limits collection; a full view may be truncated.
        /// <summary>Collects ray hits into a reusable world-owned array. No result-array allocation
        /// after capacity is sufficient. The returned view is overwritten by the next buffered Cast.</summary>
        public bool Query_CastRay(Vector3 origin, Vector3 translation, LayerMask layerMask,
            out Span<QueryHit> hits, int queryLayer = -1, int maxHits = 32)
        {
            PrepareReusableCastBuffer(maxHits);
            bool detected = Query_CastRay(origin, translation, layerMask,
                _reusableQueryHits.AsSpan(0, maxHits), out hits, queryLayer);
            _reusableQueryHitCount = hits.Length;
            return detected;
        }

        /// <summary>Collects convex sweep hits into world-owned storage, overwritten by the next buffered Cast.</summary>
        public bool Query_CastShape(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            Vector3 translation, LayerMask layerMask, out Span<QueryHit> hits,
            int queryLayer = -1, int maxHits = 32)
        {
            PrepareReusableCastBuffer(maxHits);
            bool detected = CastShapeIntoBuffer(origin, proxyPoints, proxyRadius, translation, layerMask,
                _reusableQueryHits.AsSpan(0, maxHits), out int count, out _, queryLayer);
            hits = _reusableQueryHits.AsSpan(0, count);
            _reusableQueryHitCount = count;
            return detected;
        }

        /// <summary>Collects sphere sweep hits into world-owned storage, overwritten by the next buffered Cast.</summary>
        public bool Query_CastSphere(Vector3 origin, float radius, Vector3 translation, LayerMask layerMask,
            out Span<QueryHit> hits, int queryLayer = -1, int maxHits = 32)
        {
            PrepareReusableCastBuffer(maxHits);
            bool detected = Query_CastSphere(origin, radius, translation, layerMask,
                _reusableQueryHits.AsSpan(0, maxHits), out hits, queryLayer);
            _reusableQueryHitCount = hits.Length;
            return detected;
        }

        /// <summary>Collects overlapping components into world-owned storage, overwritten by the next
        /// buffered Overlap. Returns the view length; native-only shapes appear as null entries.</summary>
        public int Query_OverlapAABB(Bounds bounds, LayerMask layerMask, out Span<Box3DShape> overlapShapes,
            int queryLayer = -1, int maxHits = 32)
        {
            PrepareReusableOverlapBuffer(maxHits);
            int count = Query_OverlapAABB(bounds, layerMask, _reusableOverlapShapes.AsSpan(0, maxHits), queryLayer);
            overlapShapes = _reusableOverlapShapes.AsSpan(0, count);
            _reusableOverlapShapeCount = count;
            return count;
        }

        /// <summary>Collects overlapping components into world-owned storage, overwritten by the next
        /// buffered Overlap. Returns the view length; native-only shapes appear as null entries.</summary>
        public int Query_OverlapShape(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            LayerMask layerMask, out Span<Box3DShape> overlapShapes, int queryLayer = -1, int maxHits = 32)
        {
            PrepareReusableOverlapBuffer(maxHits);
            int count = Query_OverlapShape(origin, proxyPoints, proxyRadius, layerMask,
                _reusableOverlapShapes.AsSpan(0, maxHits), queryLayer);
            overlapShapes = _reusableOverlapShapes.AsSpan(0, count);
            _reusableOverlapShapeCount = count;
            return count;
        }

        private void PrepareReusableCastBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (_reusableQueryHits.Length < capacity)
                _reusableQueryHits = new QueryHit[Math.Max(capacity, _reusableQueryHits.Length * 2)];
            else
                Array.Clear(_reusableQueryHits, 0, _reusableQueryHitCount);
            // Clear previous component references even when the next query has fewer hits or throws.
            _reusableQueryHitCount = 0;
        }

        private void PrepareReusableOverlapBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (_reusableOverlapShapes.Length < capacity)
                _reusableOverlapShapes = new Box3DShape[Math.Max(capacity, _reusableOverlapShapes.Length * 2)];
            else
                Array.Clear(_reusableOverlapShapes, 0, _reusableOverlapShapeCount);
            _reusableOverlapShapeCount = 0;
        }

        private void ClearReusableQueryBuffers()
        {
            Array.Clear(_reusableQueryHits, 0, _reusableQueryHits.Length);
            Array.Clear(_reusableOverlapShapes, 0, _reusableOverlapShapes.Length);
            _reusableQueryHits = Array.Empty<QueryHit>();
            _reusableOverlapShapes = Array.Empty<Box3DShape>();
            _reusableQueryHitCount = _reusableOverlapShapeCount = 0;
        }
        //CCX_MOD END(Reuse world-owned arrays for component query results)

        //CCX_MOD BEGIN(Expose nearest-hit ray and convex sweep queries with Unity results)
        /// <summary>Returns the closest ray hit without allocating or overwriting shared query buffers.
        /// translation is the complete displacement, not a normalized direction. hit is default on a miss.</summary>
        public bool Query_CastRayClosest(Vector3 origin, Vector3 translation, LayerMask layerMask,
            out QueryHit hit, int queryLayer = -1)
            => Query_CastRayClosest(origin, translation, layerMask, out hit, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public bool Query_CastRayClosest(Vector3 origin, Vector3 translation, LayerMask layerMask,
            out QueryHit hit, out Vector2Int treeStats, int queryLayer = -1)
        {
            RayResult result = _world.CastRayClosest(origin, translation, BuildQueryFilter(layerMask, queryLayer));
            treeStats = new Vector2Int(result.NodeVisits, result.LeafVisits);
            hit = result.Hit ? ToClosestQueryHit(result, translation.magnitude) : default;
            return result.Hit;
        }

        /// <summary>Returns the closest convex sweep hit without an all-hit buffer. Proxy points are
        /// offsets from origin; translation is the complete displacement. hit is default on a miss.</summary>
        public bool Query_CastShapeClosest(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            Vector3 translation, LayerMask layerMask, out QueryHit hit, int queryLayer = -1)
            => Query_CastShapeClosest(origin, proxyPoints, proxyRadius, translation, layerMask, out hit, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public bool Query_CastShapeClosest(Vector3 origin, ReadOnlySpan<Vector3> proxyPoints, float proxyRadius,
            Vector3 translation, LayerMask layerMask, out QueryHit hit, out Vector2Int treeStats, int queryLayer = -1)
        {
            QueryFilter filter = BuildQueryFilter(layerMask, queryLayer);
            ValidateQueryProxy(proxyPoints, proxyRadius);
            Unity.Mathematics.float3[] rentedPoints = null;
            Span<Unity.Mathematics.float3> points = proxyPoints.Length <= 128
                ? stackalloc Unity.Mathematics.float3[proxyPoints.Length]
                : (rentedPoints = System.Buffers.ArrayPool<Unity.Mathematics.float3>.Shared.Rent(proxyPoints.Length)).AsSpan(0, proxyPoints.Length);
            try
            {
                for (int i = 0; i < points.Length; i++) points[i] = proxyPoints[i];
                RayResult result = _world.CastShapeClosest(origin, points, proxyRadius, translation, filter);
                treeStats = new Vector2Int(result.NodeVisits, result.LeafVisits);
                hit = result.Hit ? ToClosestQueryHit(result, translation.magnitude) : default;
                return result.Hit;
            }
            finally
            {
                if (rentedPoints != null) System.Buffers.ArrayPool<Unity.Mathematics.float3>.Shared.Return(rentedPoints);
            }
        }

        /// <summary>Returns the closest sphere sweep hit without an all-hit buffer. translation is
        /// the complete displacement. Initial overlaps follow native cast semantics.</summary>
        public bool Query_CastSphereClosest(Vector3 origin, float radius, Vector3 translation, LayerMask layerMask,
            out QueryHit hit, int queryLayer = -1)
            => Query_CastSphereClosest(origin, radius, translation, layerMask, out hit, out _, queryLayer);

        /// <summary>Also reports broadphase work: treeStats.x = node visits, y = leaf visits.</summary>
        public bool Query_CastSphereClosest(Vector3 origin, float radius, Vector3 translation, LayerMask layerMask,
            out QueryHit hit, out Vector2Int treeStats, int queryLayer = -1)
        {
            ReadOnlySpan<Vector3> points = stackalloc Vector3[] { Vector3.zero };
            return Query_CastShapeClosest(origin, points, radius, translation, layerMask, out hit, out treeStats, queryLayer);
        }

        private QueryHit ToClosestQueryHit(RayResult result, float castLength)
        {
            TryGetShape(result.ShapeId, out Box3DShape component);
            return new QueryHit
            {
                shape = component,
                shapeId = result.ShapeId.ToUInt64(),
                point = (Vector3)result.Point,
                normal = result.Normal,
                fraction = result.Fraction,
                distance = result.Fraction * castLength,
                userMaterialId = result.UserMaterialId,
                triangleIndex = result.TriangleIndex,
                childIndex = result.ChildIndex,
            };
        }
        //CCX_MOD END(Expose nearest-hit ray and convex sweep queries with Unity results)
    }
}
