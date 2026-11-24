using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.RangeCheck;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Planets {
    public sealed class RemoteDrillSys : GameSystem {
        public const string SysId = "RemoteDrillSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new RemoteDrillSys());
        }

        private readonly List<RemoteDrillComp> drills = new();
        private SpaceObject currentTarget;

        private bool IsBeingDrilled(SpaceObject so) {
            return drills.Count > 0 && currentTarget == so;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoad);
            S.Query.RegisterRemoteDrill.SetResponder(RegisterDrill);
            S.Query.IsBeingDrilled.SetResponder(IsBeingDrilled);
        }

        private void OnLoad(GameState state) {
            S.Clock.OnTick.AddListener(OnTick);
        }

        private bool RegisterDrill(RemoteDrillComp drill, bool add) {
            if (add) {
                if (!drills.AddUniqueInefficient(drill)) {
                    D.Err("Drill already added! {0}", drill);
                }
            } else {
                if (!drills.Remove(drill)) {
                    D.Err("Failed to remove drill: {0}", drill);
                }
            }
            return true;
        }


        private void OnTick(long ticks) {
            using (D.Timer("RunDrills", 0.00001f)) {
                RunDrills(ticks);
            }
        }

        public override void Unload() {
        }

        public string GetName() {
            return Id;
        }

        private void StopDrilling() {
            currentTarget = null;
            for (int i = drills.Count - 1; i >= 0; i--) {
                RemoteDrillComp drill = drills[i];
                drill.StopDrilling();
                drill.HideTooDeepWarning();
                drill.HideOwnedDepositWarning();
            }
        }

        private void RunDrills(long ticks) {
            if (drills.Count == 0) { return; }
            var nearbyObjs = S.Sys.Ship.Nearby;
            if (nearbyObjs.Count == 0) {
                StopDrilling();
                return;
            }
            SpaceObject closest = null;
            float closestDist = float.MaxValue;
            var playerPos = S.Universe.Player.Location;
            for (int i = nearbyObjs.Count - 1; i >= 0; i--) {
                SpaceObject obj = nearbyObjs[i];
                if (obj.Type.IdH == SpaceObjectTypeIdH.Resource) {
                    var dist = Vectors.SqrDistance(playerPos, obj.Location);
                    if (dist < closestDist) {
                        closest = obj;
                        closestDist = dist;
                    }
                }
            }
            if (closest == null) {
                StopDrilling();
                return;
            }
            currentTarget = closest;
            if (!S.Sys.Planets.TryGetResourceForUpdate(
                    closest, out var idx, out var resource, out var mt)) {
                D.Err("Failed retrieving resource: {0}", closest);
                StopDrilling();
                return;
            }
            bool changed = false;
            for (int i = drills.Count - 1; i >= 0; i--) {
                RemoteDrillComp drill = drills[i];
                changed |= drill.TryDrill(mt, ref resource);
            }
            if (changed) {
                S.Sys.Planets.UpdateResource(idx, resource);
                S.Sys.Planets.ApplyMiningConsequence(closest, mt);
            }
        }
    }
}
