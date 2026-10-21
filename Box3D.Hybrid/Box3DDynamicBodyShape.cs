using System;
using Unity.Mathematics;
using UnityEngine;

//CCX_MOD BEGIN(Support shape mass and density settings)
namespace Box3D.Hybrid
{
    public enum Box3DMassMode { Density, Mass }

    /// <summary>Mass settings for sphere, capsule and convex hull collision geometry.</summary>
    public abstract class Box3DDynamicBodyShape : Box3DShape
    {
        [SerializeField] private Box3DMassMode MassMode;
        // Keep the original serialized name and default to preserve existing scenes.
        [SerializeField, Min(0f)] private float Density = 1000f;
        [SerializeField, Min(0f)] private float Mass = 1f;

        public Box3DMassMode massMode
        {
            get => MassMode;
            set
            {
                if (value == MassMode) return;
                if (value != Box3DMassMode.Density && value != Box3DMassMode.Mass)
                    throw new ArgumentOutOfRangeException(nameof(value));
                float volume = CalculateVolume();
                if (!IsValidVolume(volume)) throw new InvalidOperationException("Shape volume must be finite and positive.");
                if (value == Box3DMassMode.Mass) Mass = CalculatedMass;
                else Density = CalculatedDensity;
                MassMode = value;
                ApplyShapeDensity();
            }
        }

        public abstract float CalculateVolume();
        protected float3 VolumeScale => transform.lossyScale;
        public static bool IsValidVolume(float volume) => volume > 0f && !float.IsInfinity(volume) && !float.IsNaN(volume);
        public float CalculatedDensity
        {
            get
            {
                if (MassMode == Box3DMassMode.Density) return Density;
                float volume = CalculateVolume();
                return IsValidVolume(volume) ? Mass / volume : 0f;
            }
        }
        public float CalculatedMass => MassMode == Box3DMassMode.Mass ? Mass : Density * CalculateVolume();

        public void SetMass(float value)
        {
            ValidateValue(value);
            Mass = value;
            MassMode = Box3DMassMode.Mass;
            ApplyShapeDensity();
        }

        public void SetDensity(float value)
        {
            ValidateValue(value);
            Density = value;
            MassMode = Box3DMassMode.Density;
            ApplyShapeDensity();
        }

        private static void ValidateValue(float value)
        {
            if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Mass and density must be finite and non-negative.");
        }

        protected override float ResolveDensity()
        {
            float volume = CalculateVolume();
            if (!IsValidVolume(volume))
            {
                Debug.LogError("[Box3D] Shape volume must be finite and positive before calculating mass/density.", this);
                return 0f;
            }
            float density = MassMode == Box3DMassMode.Density ? Density : Mass / volume;
            if (density < 0f || float.IsNaN(density) || float.IsInfinity(density))
            {
                Debug.LogError("[Box3D] Calculated density must be finite and non-negative.", this);
                return 0f;
            }
            return density;
        }

        protected override void ApplyShapeDensity()
        {
            if (!LiveShape.IsValid) return;
            LiveShape.SetDensity(ResolveDensity(), updateBodyMass: true);
            if (AttachedBodyComponent) AttachedBodyComponent.ApplyMassOverride();
            if (Body.IsValid) Body.SetAwake(true);
        }
        private void LateUpdate()
        {
            if (LiveShape.IsValid && !math.all(AttachedScale == (float3)transform.lossyScale))
                RefreshGeometry();
        }

        // TODO: managed inertia tensor calculation and visualization.
    }
}
//CCX_MOD END(Support shape mass and density settings)
