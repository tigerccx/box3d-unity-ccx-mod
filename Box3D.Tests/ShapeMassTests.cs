using Box3D.Hybrid;
using NUnit.Framework;
using UnityEngine;

//CCX_MOD BEGIN(Support shape mass and density settings)
namespace Box3D.Tests
{
    public class ShapeMassTests
    {
        [Test]
        public void SphereModeSwitchPreservesMassAndScaleRespectsAuthority()
        {
            var go = new GameObject("mass test");
            try
            {
                var shape = go.AddComponent<Box3DSphereShape>();
                shape.radius = 1f;
                shape.SetDensity(3f);
                float original = shape.CalculatedMass;
                shape.massMode = Box3DMassMode.Mass;
                Assert.That(shape.CalculatedMass, Is.EqualTo(original).Within(0.0001f));
                go.transform.localScale = new Vector3(-2f, 1f, 1f);
                Assert.That(shape.CalculatedMass, Is.EqualTo(original));
                Assert.That(shape.CalculatedDensity, Is.EqualTo(3f / 8f).Within(0.0001f));
                shape.massMode = Box3DMassMode.Density;
                go.transform.localScale = Vector3.one;
                Assert.That(shape.CalculatedMass, Is.EqualTo(original / 8f).Within(0.0001f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CapsuleVolumeUsesScaledRadiusAndClampedSegment()
        {
            var go = new GameObject("capsule mass test");
            try
            {
                var shape = go.AddComponent<Box3DCapsuleShape>();
                shape.radius = 0.5f;
                shape.height = 2f;
                go.transform.localScale = new Vector3(2f, 3f, -4f);
                Assert.That(shape.CalculateVolume(), Is.EqualTo(Mathf.PI * 4f * (2f + 8f / 3f)).Within(0.0001f));
                shape.height = 0f;
                Assert.That(shape.CalculateVolume(), Is.EqualTo(32f * Mathf.PI / 3f).Within(0.0001f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void InvalidVolumeRejectsModeSwitchAndDoesNotDivide()
        {
            var go = new GameObject("zero mass test");
            try
            {
                var shape = go.AddComponent<Box3DBoxShape>();
                shape.size = Vector3.zero;
                shape.SetMass(5f);
                Assert.That(shape.CalculatedDensity, Is.Zero);
                Assert.Throws<System.InvalidOperationException>(() => shape.massMode = Box3DMassMode.Density);
                Assert.That(shape.massMode, Is.EqualTo(Box3DMassMode.Mass));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HullManagedVolumeMatchesNativeMassAfterVertexReduction()
        {
            var points = new Unity.Mathematics.float3[30];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * 2f * Mathf.PI / points.Length;
                points[i] = new Unity.Mathematics.float3(Mathf.Cos(angle) * 2f + 10f,
                    (i % 3) - 1f, Mathf.Sin(angle) * 3f - 5f);
            }
            var hull = Hull.Create(points, 8);
            var world = World.Create(WorldDef.Default);
            try
            {
                var bodyDef = BodyDef.Default;
                bodyDef.Type = BodyType.Dynamic;
                var body = world.CreateBody(bodyDef);
                var def = ShapeDef.Default;
                def.Density = 7f;
                var shape = body.CreateHullShape(def, hull);
                Assert.That(hull.CalculateVolume() * 7f,
                    Is.EqualTo(shape.ComputeMassData().Mass).Within(0.001f));
            }
            finally { world.Destroy(); hull.Destroy(); }
        }
    }
}
//CCX_MOD END(Support shape mass and density settings)
