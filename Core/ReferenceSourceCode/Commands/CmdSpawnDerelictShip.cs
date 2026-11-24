using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;
using System;
using Game.Utils;
using Game.CodeGen;
using Game.Data.Space;
using Game.Procgen;
using Game.Rendering;
using KL.Randomness;
using Game.Systems.Ships;
using Game.Systems.Story;

namespace Game.Commands {
    public class CmdSpawnDerelictShip : AsyncCommand {
        private readonly SpaceObject so;
        private readonly StoryEventDef forcedEvent;
        private readonly string targetName;
        private readonly int seed;
        public override bool TrySameFrame => false;
        public StoryEventData EventData;
        public Action OnComplete;
        public CmdSpawnDerelictShip(SpaceObject so,
                StoryEventDef forcedEvent = null,
                string targetName = null, int seed = 0) : base("spawn_derelict", unique: true) {
            if (so == null) {
                D.ErrSoft("SpaceObject is null in CmdSpawnDerelictShip! Forced event: {0}. Target name: {1}",
                    forcedEvent, targetName);
            }
            this.so = so;
            this.seed = seed;
            this.forcedEvent = forcedEvent;
            this.targetName = targetName ??
                so?.DisplayName ?? T.SpaceObjectShipderelict;
        }
        public override IEnumerator ExecuteAsync() {
            Ready.SetUserInput(false, "Derelict Gen");
            Ready.SetGen(false, "Derelict");
            Ready.SetGame(false, "Derelict");
            S.Clock.SetSpeed(ClockSpeed.Stopped);
            S.UIQuery.ShowStarmap.Ask(false);
            yield return null;
            if (!GameState.IsCurrent(S)) { yield break; }
            var camPos = RenderUtils.CameraPosition;
            var camOrtho = RenderUtils.CameraOrtho;
            The.ScreenBlock.Show();
                The.ScreenBlock.SetCenterText(
                    T.ApproachingTarget_V1.F(targetName), 2f);
            The.ScreenBlock.SetSideText("");
            yield return GenerateDerelict();
            if (!A.FocusOnEvents) {
                // Generation frames the new hull by itself (the shape stage, and any event's
                // AfterGenerate, mods included). The stages can't be gated on the setting,
                // because the same ones build the player's ship in a new game, so the camera is
                // put back here instead. A jump rather than a pan: it happens behind the
                // screen block, so the player never sees it leave.
                S.Sig.CameraJump.Send(camPos, Consts.V2Zero, camOrtho * 2f);
            }
            S.Situation.DerelictsApproached++;
            S.Sig.HideOverlay.Send();
            Ready.SetUserInput(true, "Derelict Gen");
            Ready.SetGen(true, "Derelict");
            Ready.SetGame(true, "Derelict");
            The.ScreenBlock.Hide(2);
            if (so != null) {
                var parent = so.Parent;
                if (so.Ship == null) {
                    D.Err("Derelict ship {0} has no ship!", so);
                    SOUtils.RemoveSO(so);
                } else {
                    S.Sys.NPCShip.RemoveShip(so.Ship);
                }
                if (parent?.Type.IdH == SpaceObjectTypeIdH.DerelictZone) {
                    bool hasDerelict = false;
                    if (parent.HasChildren) {
                        foreach (var child in parent.Children) {
                            // derelict zone was consumed
                            if (child.Ship?.IsDerelict == true) {
                                hasDerelict = true;
                                break;
                            }
                        }
                    }
                    if (!hasDerelict) {
                        parent.SetType(SpaceObjectType.Find(
                            SpaceObjectTypeIdH.SpaceEventConsumed));
                    }
                }
            }
            S.Clock.SetSpeed(ClockSpeed.Normal);
            S.Sig.ShowFanfare.Send(T.EvtDerelictApproachedTitle);
            OnComplete?.Invoke();
        }

        private IProducesDerelictShip derelictProducer;

        private IEnumerator GenerateDerelict() {
            int eventSeed = seed;
            if (eventSeed == 0) {
                if (so != null) {
                    eventSeed = Hashes.Quick(so.Seed, Primes.Prime028);
                } else {
                    D.Err("CmdSpawnDerelictShip has no seed and no SO!");
                    eventSeed = Rng.Unseeded.Int;
                }
            }
            StoryEventData data = null;
            // get a story event
            if (forcedEvent != null) {
                D.Warn("Forcing derelict event: {0}", forcedEvent.Id);
                data = StoryEventData.CreateFor(forcedEvent);
                data.Faction = so?.Faction;
                data.Seed = eventSeed;
                if (!S.Query.StartStoryEvent.Ask(data)) {
                    data = null;
                    D.Err("Failed to start a forced story event: {0}", data);
                }
            }
            if (data == null) {
                // A derelict that resulted from a ship just being defeated in combat always
                // gets its own guaranteed crew remnants (see
                // BaseDerelictShipStoryEvent.AddCombatCrewRemnantsStage) - events that bring
                // their own survivors would clash with that, so exclude them here.
                var isFreshKill = so?.Ship != null
                    && (so.Ship.State & ShipStateFlags.SpawnDevices) == ShipStateFlags.SpawnDevices;
                var hook = StoryEventHook.Create(StoryEventFlags.DerelictShip)
                    .Instant()
                    .WithEventFilter((def) => !isFreshKill
                        || (def.Flags & StoryEventFlags.DerelictSpawnsSurvivors) == StoryEventFlags.None);
                hook.WithBeforeStart((d) => {
                    d.Seed = eventSeed;
                    d.TargetSO = so;
                });
                data = S.Query.AddStoryEventHook.Ask(hook);
                data.Faction = so.Faction;
                if (!StoryEventData.IsOK(data)) {
                    D.Err("Failed starting gen derelict hook: {0} | {1}",
                        hook, data);
                }
            }
            AfterPick(data);
            if (derelictProducer == null) {
                yield break;
            }
            var rng = new Rng(eventSeed);
            ShipFactory factory = null;
            try {
                factory = derelictProducer.CreateShipFactory(S, rng.Fork(), so?.Ship);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed creating ship factory for: {0} | {1}",
                    data, derelictProducer);
            }
            if (factory == null) {
                D.Err("Fallback to generic derelict");
                derelictProducer = new GenericDerelictShipStoryEvent();
                factory = derelictProducer.CreateShipFactory(S, rng.Fork(), so?.Ship);
            }
            DestroyAnythingIn(factory.ConfinementArea);
            if (hasDestroyedSomethingInGenArea) {
                yield return Ready.WaitForCmdQ(this);
            }

            // only needed to decide what the derelict added
            HashSet<int> existingObjIds = null;
            if (Tunables.AutoForbidDerelictResources) {
                existingObjIds = new HashSet<int>();
                foreach (var obj in S.Objs.All) {
                    if (obj.IsActive) {
                        existingObjIds.Add(obj.Id);
                    }
                }
            }

            yield return factory.Build(S);
            if (!factory.IsSuccess) {
                D.Err("Failed building a derelict ship: {0}", factory);
            }
            try {
                derelictProducer.AfterGenerate(S, factory, rng);
                factory.ResetStages();
            } catch (Exception ex) {
                D.LogEx(ex, "Failure in AfterGenerate of derelict factory for: {0} | {1}",
                    data, derelictProducer);
            }
            if (existingObjIds != null) {
                ForbidHaulingOnGeneratedObjs(existingObjIds);
            }
            S.Sig.DerelictShipApproached.Send(data, factory);
        }

        private void ForbidHaulingOnGeneratedObjs(HashSet<int> existingObjIds) {
            foreach (var obj in S.Objs.All) {
                if (!obj.IsActive || existingObjIds.Contains(obj.Id)) {
                    continue;
                }
                if (EntityUtils.IsWithinMargin(obj.PosIdx, false, false)) {
                    continue;
                }
                if (obj.Pile != null && !obj.Pile.TreatAsObj) {
                    obj.Pile.IsForbidden = true;
                } else if (obj.StorableObj != null) {
                    obj.StorableObj.IsForbidden = true;
                }
            }
        }

        private bool hasDestroyedSomethingInGenArea;

        private void DestroyAnythingIn(PosRect rect) {
            rect.ForEach(DestroyFloor);
        }

        private void DestroyFloor(int p) {
            var f = EntityUtils.FloorAt(p);
            if (f != null) {
                hasDestroyedSomethingInGenArea = true;
                new CmdRemoveTile(f).Execute(S);
            }
        }

        private void AfterPick(StoryEventData data) {
            EventData = data;
            if (!StoryEventData.IsOK(data)) {
                D.Err("Can't produce a derelict ship from data: '{0}'", data);
                return;
            }
            if (data.Def.Event is IProducesDerelictShip prod) {
                derelictProducer = prod;
            } else {
                D.Err("Found DerelictShip event that does not implement IDerelictEvent! {0}",
                    data);
                derelictProducer = null;
            }
        }
    }
}
