using System;
using KL.Utils;
using UnityEngine;

namespace Game.Systems {
    //[Obsolete("Replace with VFX")]
    public sealed class XParticles {
        private string name;
        private ParticleSystem ps;
        public ParticleSystem.Particle[] Particles;

        private int lastParticleCount;
        private bool needsTrailsReenable;
        private ParticleSystemRenderer psr;
        private Material trailMat;
        private bool hasTrails;

        public void ResetTrails() {
            if (hasTrails) {
                lastParticleCount = 0;
            }
        }



        public XParticles(string prefabName, int initialParticles) {
            name = prefabName;
            var prefab = ResourceLibrary.FindPrefab<ParticleSystem>(prefabName);
            D.Ass(prefab != null, "Prefab null for XParticles: {0}", name);
            ps = GameObject.Instantiate(prefab);
            hasTrails = ps.trails.enabled;
            Particles = new ParticleSystem.Particle[initialParticles];
            if (hasTrails) {
                psr = ps.GetComponent<ParticleSystemRenderer>();
                trailMat = psr.trailMaterial;
            }
        }

        public void ShowParticles(int particleCount, bool needTrailReset) {
            if (hasTrails && needTrailReset) {
                lastParticleCount = 0;
            }
            if (particleCount > 0) {
                if (particleCount != lastParticleCount) {
                    ResizeIfNecessary(particleCount);
                    lastParticleCount = particleCount;
                    if (hasTrails) {
                        var tr = ps.trails;
                        tr.enabled = false;
                        needsTrailsReenable = true;
                    }
                } else if (hasTrails && needsTrailsReenable) {
                    needsTrailsReenable = false;
                    var tr = ps.trails;
                    psr.trailMaterial = trailMat;
                    tr.enabled = true;
                }
                ps.SetParticles(Particles, particleCount, 0);
            }
        }

        public void ResizeIfNecessary(int size) {
            if (size > Particles.Length) {
                Array.Resize(ref Particles, Mathf.CeilToInt(size * 1.2f));
                D.Warn("Resized {0} xparticles array to {1}", name, Particles.Length);
            }
        }

        public void Cleanup() {
            if (ps != null) {
                GameObject.Destroy(ps);
                ps = null;
            }
        }
    }

}
