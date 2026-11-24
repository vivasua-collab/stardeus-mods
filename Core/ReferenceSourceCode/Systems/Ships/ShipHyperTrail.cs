using System;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public class ShipHyperTrail {
        public readonly NPCShip Ship;
        public Vector2 Location;
        public readonly SpaceSector From;
        public SpaceSector To;
        public SpaceObject SO;
        public long CreatedAt;
        public ShipHyperTrail(NPCShip ship, SpaceSector from) {
            Ship = ship;
            From = from;
        }

        public void UpdateLooks(long ticks, int timeoutTicks) {
            if (SO == null) {
                D.Err("Trying to update trail looks while SO is null! {0}", this);
                return;
            }

            float ticksPassed = (int) (ticks - CreatedAt);
            float life = ticksPassed / timeoutTicks;
            if (UIShowing.Starmap) {
                SO.UI?.VFX?.VFX?.SetFloat(ShaderVars.Life, life);
            }
            SO.Visual?.VFX?.VFX?.SetFloat(ShaderVars.Life, life);
        }

        public SpaceObject GetSOFor(SpaceObject obj) {
            if (SO != null) {
                return SO;
            }
            var so = obj.S.Universe.CreateObject();
            so.Type = SpaceObjectType.Find(SpaceObjectTypeIdH.ShipHyperTrail);
            if (Location.sqrMagnitude <= 0.00001f) {
                Location = SOUtils.RandomLocationInside(obj, Rng.Unseeded);
            }
            so.Location = Location;
            SO = so;
            return so;
        }

        public string Info {
            get {
                Caches.StringBuilder.Clear();
                LoadInfo(Caches.StringBuilder);
                return Caches.StringBuilder.ToString();
            }
        }
        public void LoadInfo(StringBuilder sb) {
            sb.AppendLine(Ship.NameLinked);
            if (To != null) {
                sb.AppendFormat("{0}: {1}\n", T.Location, To.SO.ParentPath());
            } else {
                sb.AppendLine(T.ShipHypertrailHot);
            }
            sb.AppendLine(Units.TicksAgo((int)(G.Ticks - CreatedAt)));
        }

        public override string ToString() {
            return $"ShipHyperTrail[{Ship.NameFull}|{From.Name} -> {To?.Name}]";
        }
    }
}
