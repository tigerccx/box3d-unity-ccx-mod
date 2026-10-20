# Callbacks & threading

## Multithreading

Box3D runs its own internal job scheduler. Opt in per world:

```csharp
WorldDef def = WorldDef.Default;
def.WorkerCount = (uint)Mathf.Max(1, SystemInfo.processorCount / 2); // ≈ physical cores
World world = World.Create(def);
```

Guidance from box3d itself: use performance cores only — hyper-threads add little. Scaling depends
on how many independent **islands** your scene has (see [Performance](performance.md)): thousands
of loose objects scale beautifully; one giant jointed structure barely parallelizes.

`Step` blocks until the step completes — from your code's perspective the API is single-threaded
and there is nothing to synchronize... *except* the callbacks below.

## Collision and material callbacks

Most games never need these — the polled [events](events.md) cover reactions, and filters cover
most collision rules. Callbacks exist for the cases polling can't do: vetoing contacts and custom
material mixing. **They are invoked from worker threads during `Step`** when `WorkerCount > 1`,
so they must be pure functions: no Unity API, no world mutation, no allocation, no locks.

```csharp
// Decide collision for shape pairs that BOTH set ShapeDef.EnableCustomFiltering:
world.SetCustomFilterCallback((shapeA, shapeB) => TeamOf(shapeA) != TeamOf(shapeB));

// Veto individual contacts per step (shape needs ShapeDef.EnablePreSolveEvents).
// Classic use: one-way platforms.
world.SetPreSolveCallback((shapeA, shapeB, point, normal) => normal.y > 0.5f);

// Custom material mixing (defaults: friction = sqrt(a·b), restitution = max(a, b)).
world.SetFrictionCallback((fa, matA, fb, matB) => math.min(fa, fb));
```

Pass `null` to any setter to restore default behavior. Exceptions thrown inside a callback are
caught, logged, and replaced by the engine default (an exception escaping into a native worker
thread would crash the player).

One limitation inherited from the native API: the friction/restitution mixers receive no world
context, so the wrapper keeps a single **global** managed mixer of each kind — the last
registration wins across all worlds. Filter and pre-solve callbacks are properly per-world.

## Pre-integrate callback

Register a `PreIntegratePositionsHandler` with `body.RegisterPreIntegratePositionsHandler(handler)`
(or the same method on `Box3DBody`); pass `null` to unregister.
It runs serially on the solver orchestrator for each registered awake body in every sub-step,
after constraint solving and before position integration.

The handler receives `in Body body`, `in Vector3 deltaPosition`, `in Quaternion deltaRotation`
and `float timeStep` (the sub-step duration). Deltas are read-only changes accumulated by
preceding sub-steps: initially zero translation and identity rotation. Stored body transforms
exclude these deltas. The dispatcher does not write velocity or deltas back.

Read the callback body's velocity, mass, inertia and stored transform directly. To change
velocity, use the [velocity setters](concepts.md#body-velocity-setters) with `wake: false`.
`ApplyLinearImpulse`, `ApplyLinearImpulseToCenter` and `ApplyAngularImpulse` on that body with
`wake: false` are also permitted. Do not access other bodies, use other mutating APIs, or
access Unity scene APIs. Use impulses for an immediate response; force integration has
already run for this sub-step.

Position integration still applies motion locks and speed limits; later constraint relaxation
may change velocity. Large changes can increase contact or joint error. Exceptions are logged;
body changes already applied are not rolled back.

## Logging and asserts

Box3D's internal log and assert output is routed to the Unity console automatically at startup
(`Box3DRuntime.Install`, run via `RuntimeInitializeOnLoadMethod`). Assertion failures from API
misuse (bad defs, stale ids in debug paths) appear as errors with the native file/line.
