using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.Atmo;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using System.Text;
using Game.Input;

namespace Game.Components {
    public sealed class HullIntegrityComp : BaseComponent<HullIntegrityComp>,
            IStatProvider, IDefDescriptionProvider {
        public bool CanHaveFullIntegrity => MaxIntegrity > 0.999f;
        public float Insulation => maxInsulation * integrityPercent;
        public float Airtight => maxAirtight * integrityPercent;
        public float Integrity => MaxIntegrity * integrityPercent;
        private float integrityPercent;
        public float MaxIntegrity;
        private float maxInsulation;
        private float maxAirtight;
        private bool isLeakAdded;
        public int PosIdx;
        public bool IsAirtight => Airtight > 0.99999f;
        public float MaxInsulation => maxInsulation;
        public float MaxAirtight => maxAirtight;
        public float DisplayInsulation => AtmoRes.ForDisplay(Insulation, LayerId == WorldLayer.Floor);
        public float DisplayAirtight => AtmoRes.ForDisplay(Airtight, LayerId == WorldLayer.Floor);
        private BFloat integrityPctB;
        public BFloat IntegrityPercentB => integrityPctB ??= new BFloat(integrityPercent);
        private BFloat insulationB;
        public BFloat InsulationB => insulationB ??= new BFloat(DisplayInsulation);
        private BFloat airtightB;
        public BFloat AirtightB => airtightB ??= new BFloat(DisplayAirtight);

        public ParticlesSys.Data LeakParticles;
        private readonly List<IHullIntegrityProvider> providers = new();

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new HullIntegrityComp());
        }

        protected override void OnConfig() {
            MaxIntegrity = Config.GetFloat(PropertyIdH.MaxIntegrity, 1f);
            // Defaults here are for a floor
            maxInsulation = Config.GetFloat(PropertyIdH.MaxInsulation, Consts.FloorMaxInsulation);
            maxAirtight = Config.GetFloat(PropertyIdH.MaxAirtight, Consts.FloorMaxAirtight);
        }

        public override void OnLateReady(bool wasLoaded) {
            // Grab all components that implement IHullIntegrityProvider
            for (int i = 0; i < entity.Components.Length; i++) {
                IComponent comp = entity.Components[i];
                if (comp is IHullIntegrityProvider provider) {
                    providers.Add(provider);
                }
            }
            if (wasLoaded) {
                PosIdx = Tile.Transform.PosIdx;
                RecalculateIntegrity();
            }
        }

        public override void OnRemove() {
            MaxIntegrity = 0;
            maxInsulation = 0;
            S.Sig.RecalculateAtmosphere.Send(entity.PosIdx);
            if (isLeakAdded) {
                isLeakAdded = false;
                S.Sys.Particles.RemoveLeak(this);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.TilePosChanged) {
                PosIdx = Tile.PosIdx;
                RecalculateIntegrity();
            }
            if (message == MsgIdH.HullIntegrityChanged ||
                    message == MsgIdH.TookDamage ||
                    message == MsgIdH.RepairedSomeDamage ||
                    message == MsgIdH.ConstructionFinished) {
                RecalculateIntegrity();
                S.Sig.RecalculateAtmosphere.Send(entity.PosIdx);
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            PosIdx = pos;
            if (LeakParticles != null) {
                LeakParticles.Position += new Vector2(dx, dy);
                LeakParticles.EmitParams.position += new Vector3(dx, dy, 0);
            }
        }

        public void RecalculateIntegrity() {
            if (Tile.Health.Health <= 0) {
                integrityPercent = 0f;
            }
            integrityPercent = 1f;
            for (int i = 0; i < providers.Count; i++) {
                IHullIntegrityProvider provider = providers[i];
                integrityPercent = Mathf.Min(integrityPercent, provider.Integrity);
            }
            integrityPctB?.Set(integrityPercent);
            airtightB?.Set(DisplayAirtight);
            insulationB?.Set(DisplayInsulation);
            // We won't register frames and angled floors as leaks
            if (!CanHaveFullIntegrity && LayerId == WorldLayer.Floor) {
                return;
            }
            if (PosIdx < 1) {
                PosIdx = Tile.PosIdx;
            }
            var p = PosIdx;
            if (p < 1) {
                D.Err("Trying to calculate integrity too soon! Pos not set");
                return;
            }
            bool hasAtmo = false;
            if (LayerId == WorldLayer.Floor) {
                hasAtmo = HasAtmoAt(p);
            } else if (LayerId == WorldLayer.Walls) {
                hasAtmo = HasAtmoAt(Pos.N(p))
                    || HasAtmoAt(Pos.S(p))
                    || HasAtmoAt(Pos.E(p))
                    || HasAtmoAt(Pos.W(p));
            }
            if (LayerId == WorldLayer.Floor) {
                // A damaged floor loses nothing until it stops sealing its cell against
                // space, and a repaired one stops at once even while the room still holds
                // air - its emit chance never drops to 0 the way a repaired wall's does
                bool leaks = Airtight < AtmoRes.Space;
                if (leaks && hasAtmo && !isLeakAdded) {
                    isLeakAdded = true;
                    S.Sys.Particles.AddLeak(this);
                } else if (!leaks && isLeakAdded) {
                    isLeakAdded = false;
                    S.Sys.Particles.RemoveLeak(this);
                }
            } else if (hasAtmo && !isLeakAdded) {
                if (LayerId == WorldLayer.Walls && Airtight < 0.995f) {
                    isLeakAdded = true;
                    S.Sys.Particles.AddLeak(this);
                }
            } else if (!hasAtmo && isLeakAdded) {
                if (LayerId == WorldLayer.Walls && Airtight > 0.998f) {
                    isLeakAdded = false;
                    S.Sys.Particles.RemoveLeak(this);
                }
            }
        }

        public float ParticleEmitChance => (1f - Airtight) * 0.9f;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasAtmoAt(int p) {
            bool hasAtmo = S.GetO2(p) > 0.01f;
            if (!hasAtmo) {
                hasAtmo = S.GetTemp(p) > 0.01f;
            }

            return hasAtmo;
        }

        public override string ToString() {
            return string.Format(" * HullIntegrity: [Can have full: {0}]",
                CanHaveFullIntegrity);
        }



        public string StatsCategoryName => T.HullIntegrity;
        public int StatsOrder => 50;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
            w.Load(IconId.CShield, T.HullIntegrity, Integrity);
            IntegrityPercentB.Bind(life, (p) => {
                w.SetPercent(p, true);
            });

            var wAirtight = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
            wAirtight.Load(IconId.WOverlayOxygen, T.HullAirtight, DisplayAirtight);
            AirtightB.Bind(life, (p) => {
                wAirtight.SetPercent(p, false);
                wAirtight.Item.SetText2(Units.Percentage(p));
            });

            var wInsulation = UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, block.Container, idx++);
            wInsulation.Load(IconId.CTemperature, T.HullInsulation, DisplayInsulation);
            InsulationB.Bind(life, (p) => {
                wInsulation.SetPercent(p, false);
                wInsulation.Item.SetText2(Units.Percentage(p));
            });
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            return addedAnything;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
            var cfg = def.ComponentConfigFor(ComponentIdH.HullIntegrity);
            // var maxIntegrity = cfg.GetFloat(PropertyIdH.MaxIntegrity, 1f);
            var isFloor = def.LayerId == WorldLayer.Floor;
            var maxInsulation = AtmoRes.ForDisplay(
                cfg.GetFloat(PropertyIdH.MaxInsulation, Consts.FloorMaxInsulation), isFloor);
            var maxAirtight = AtmoRes.ForDisplay(
                cfg.GetFloat(PropertyIdH.MaxAirtight, Consts.FloorMaxAirtight), isFloor);
            sb.Append(GlyphMap.BRHR);
            sb.Append(Texts.SubTitle(T.HullIntegrity));

            // sb.Append(T.HullMax_Integrity);
            // sb.Append(": ");
            // sb.AppendLine(Texts.Accented(Units.Percentage(maxIntegrity)));

            sb.Append(T.HullAirtight);
            sb.Append(": ");
            sb.AppendLine(Texts.Accented(Units.Percentage(maxAirtight)));

            sb.Append(T.HullInsulation);
            sb.Append(": ");
            sb.Append(Texts.Accented(Units.Percentage(maxInsulation)));
        }
    }
}
