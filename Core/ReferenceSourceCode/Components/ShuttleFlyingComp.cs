using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;

namespace Game.Components {
    public sealed class ShuttleFlyingComp : BaseComponent<ShuttleFlyingComp>,
            ITickProgressListener {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShuttleFlyingComp());
        }

        private float maxHeight;
        private float height;
        private float heightOffset;
        private Vector2 destination;
        private Vector2 startPos;
        private int shuttleId;
        public bool WillLand;
        private ShuttleComp shuttle;
        private ShuttleComp Shuttle {
            get {
                if (shuttleId == 0) { return null; }
                if (shuttle == null) {
                    LoadShuttle();
                }
                return shuttle;
            }
        }
        private float maxFlightSpeed;
        private float flightSpeed;

        private void LoadShuttle() {
            shuttle = S.Components.Find<ShuttleComp>(shuttleId);
            if (shuttle?.Entity.IsActive != true) {
                D.Err("Null or inactive shuttle by id: {0}!", shuttleId);
                shuttleId = 0;
            } else {
                shuttle.FlyingShuttle = this;
            }
        }

        // Called by ShuttleComp.UpdatePosIdx when the pad this shuttle is flying to/from
        // gets relocated by a winch or rotation, so the flight target moves with it.
        public void OnPadMoved(int dx, int dy) {
            var shift = new Vector2(dx, dy);
            destination += shift;
            startPos += shift;
        }

        public void Launch(ShuttleComp shuttle, Vector2 dest,
                bool willLand, bool permanent, float heightOffset) {
            maxHeight = Rng.URange(14f, 16f);
            maxFlightSpeed = Rng.URange(19f, 20f);
            height = willLand ? maxHeight : 0f;
            this.heightOffset = heightOffset;
            destination = dest;
            this.shuttle = shuttle;
            shuttleId = shuttle.EntityId;
            this.WillLand = willLand;
            flightSpeed = willLand ? maxFlightSpeed : 0f;
            startPos = Obj.Transform.Position;
            if (!permanent) {
                shuttle.FlyingShuttle = this;
            }
        }

        private int impactExplosionLevel;

        protected override void OnConfig() {
            height = 0.5f;
            impactExplosionLevel = Config.GetInt(PropertyIdH.ImpactExplosionLevel, 5);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("ShuttleId", shuttleId);
            data.SetFloat("MaxHeight", maxHeight);
            data.SetFloat("MaxSpeed", maxFlightSpeed);
            data.SetFloat("Height", height);
            data.SetFloat("HeightOffset", heightOffset);
            data.SetVector2("Destination", destination);
            data.SetVector2("StartPos", startPos);
            data.SetBool("WillLand", WillLand);
            data.SetVector2("GraphicSize", Obj.Graphics.Size);
            data.SetString("Graphic", Obj.Graphics.OverrideGraphicId);
        }

        protected override void OnLoad(ComponentData data) {
            shuttleId = data.GetInt("ShuttleId", 0);
            maxHeight = data.GetFloat("MaxHeight", maxHeight);
            maxFlightSpeed = data.GetFloat("MaxSpeed", maxFlightSpeed);
            height = data.GetFloat("Height", height);
            heightOffset = data.GetFloat("HeightOffset", heightOffset);
            destination = data.GetVector2("Destination", Consts.V2Zero);
            startPos = data.GetVector2("StartPos", Consts.V2Zero);
            WillLand = data.GetBool("WillLand", WillLand);
        }

        public override void OnLateReady(bool wasLoaded) {
            Obj.Movable.IsForbidden = true;
            Obj.Transform.SkipMoveLoose = true;
            if (wasLoaded) {
                Obj.Graphics.OverrideGraphic(Data.GetString("Graphic",
                    Obj.Graphics.GraphicId));
                Obj.Graphics.SetSize(Data.GetVector2("GraphicSize",
                    Obj.Graphics.Size));
                if (Ready.Game) {
                    SetShadowHeight();
                } else {
                    Ready.WhenGame(this, "SetShadowHeight", SetShadowHeight);
                }
            }
        }

        private void SetShadowHeight() {
            Obj.Graphics.SetShadowHeight(height);
        }

        public override void OnRemove() {
            if (Shuttle != null) {
                Shuttle.KillDustFX();
                Shuttle.FlyingShuttle = null;
            }
        }

        private bool hasCheckedOutdoors;
        private bool needToBreakThrough;
        private HashSet<int> needToBreakThroughArea;

        private int lastPos;

        private void CheckOutdoors() {
            hasCheckedOutdoors = true;
            if (Shuttle?.Tile == null) { return; }
            if (Shuttle.Tile.Outdoors.IsOutdoors) {
                return;
            }
            var pos = Shuttle.Tile.PosIdx;
            if (Pos.IsOOB(pos)) { return; }
            var area = S.Sys.Areas.AreaAt(pos);
            if (!Arrays.IsEmpty(area)) {
                needToBreakThroughArea = new HashSet<int>(area);
                needToBreakThrough = true;
            } else {
                needToBreakThrough = false;
            }
        }

        private bool isPendingRemoval;

        public void TickProgress(float tickProgress) {
            if (isPendingRemoval || !Obj.IsActive) { return; }
            if (shuttle == null && shuttleId != 0) {
                LoadShuttle();
            }
            if (!hasCheckedOutdoors) {
                CheckOutdoors();
            }
            var shuttlePos = Obj.Graphics.Position;
            var tpf = S.Clock.TicksPerUpdate;
            shuttlePos = Vector2.MoveTowards(shuttlePos, destination, flightSpeed * tpf);
            if (!EntityUtils.IsWithinBounds(shuttlePos) ||
                    Vectors.SqrDistance(shuttlePos, destination) < 0.001f) {
                if (WillLand) { Shuttle.Show(); }
                isPendingRemoval = true;
                S.Clock.OnNextUpdate(() => S.Objs.Destroy(Obj, true));
                return;
            } else {
                Obj.Transform.TeleportTo(shuttlePos);
                Obj.Graphics.TeleportTo(shuttlePos);
                if (needToBreakThrough) {
                    var pos = Obj.PosIdx;
                    if (pos != lastPos) {
                        lastPos = pos;
                        var isInArea = needToBreakThroughArea.Contains(pos);
                        if (WillLand && isInArea || !WillLand && !isInArea) {
                            needToBreakThrough = false;
                            // last check
                            Shuttle.Tile.Outdoors.UpdateOutdoorsState(G.Ticks);
                            if (!Shuttle.Tile.Outdoors.IsOutdoors) {
                                // let's fuck shit up properly
                                Explosion.Create(S, impactExplosionLevel,
                                    position: shuttlePos);
                                // EntityUtils.CreateExplosion(shuttlePos, 5f, 500f, false);
                            }
                        }
                    }
                }
            }

            var distToDest = Vectors.Distance(shuttlePos, destination);
            float distLerp;
            var maxShadowDist = 15;
            var travelDist = Vectors.Distance(startPos, destination);
            float distFromOrigin;
            if (WillLand) {
                distLerp = Mathf.InverseLerp(0, travelDist, distToDest);
                distFromOrigin = Vectors.Distance(destination, shuttlePos);
            } else {
                distLerp = Mathf.InverseLerp(travelDist, 0, distToDest);
                distFromOrigin = Vectors.Distance(startPos, shuttlePos);
            }
            if (distFromOrigin < 1) {
                Shuttle?.ShowDustFX();
            } else {
                Shuttle?.KillDustFX();
            }
            height = Mathf.Lerp(0, maxHeight, distFromOrigin * 2f / travelDist);
            flightSpeed = 3f + distLerp * maxFlightSpeed;
            //height = 0.1f + shadowLerp * maxHeight;
            height = Mathf.Clamp(height, 0, maxShadowDist);
            // TODO fluctuate the height?
            Obj.Graphics.SetShadowHeight(height + heightOffset);
        }

        public override string ToString() {
            return $" * Flying Shuttle";
        }
    }
}
