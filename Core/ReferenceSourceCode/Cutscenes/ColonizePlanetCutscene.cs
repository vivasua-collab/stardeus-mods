using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Data;
using Game.Data.Space;
using Game.Achievements;
using Game.Utils;
using Game.UI;
using KL.Utils;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data.Factions;
using Game.Procgen.Space;
using Game.Systems.Creatures;
using Game.Systems.Ships;
using System.Linq;
using Game.Audio;
using KL.Clock;
using KL.Randomness;
using Game.CodeGen;
using Game.Systems.Story;

namespace Game.Story.Events {
    public sealed class ColonizePlanetCutscene {
        private readonly GameState S;
        private readonly SpaceObject target;
        private readonly Action after;
        private static int lockHash;

        // The lock is released only where the cutscene ends: leaving the game mid-credits
        // (or a throw in it) kept it, and colonizing in a later game of the session did
        // nothing but log "Already running"
        public static void ResetAfterGame() {
            lockHash = 0;
        }

        public ColonizePlanetCutscene(GameState s, SpaceObject target, Action after = null) {
            if (lockHash != 0) {
                D.Err("Already running colonize planet cutscene!");
                return;
            }
            lockHash = GetHashCode();
            S = s;
            this.target = target;
            this.after = after;
            Execute();
        }

        private void Execute() {
            Coroutines.Start(SettleIn(target), this, "SettleIn", false, false);
        }

        private IEnumerator RemoveWealth() {
            var storages = S.Sys.Inventory.Storages;
            for (int i = storages.Count - 1; i >= 0; i--) {
                var s = storages.SafeGet(i);
                if (s == null) { continue; }
                try {
                    if (!s.Entity.IsActive) { continue; }
                    if (!s.IsPublic || s.IsEmpty) { continue; }
                    if (s is UnstoredMatComp pile) {
                        if (s.Entity.Carrier != null) { continue; }
                        var av = pile.Available(pile.Type);
                        if (av == 0) { continue; }
                        var toTake = Mathf.RoundToInt(av * S.Rng.Range(0.4f, 0.6f));
                        if (toTake == 0) { continue; }
                        pile.Take(pile.Type, null, toTake, null);
                    } else {
                        if (s.Contents != null) {
                            foreach (var mat in s.Contents.ToArray()) {
                                var av = mat.Value;
                                if (av == 0) { continue; }
                                var toTake = Mathf.RoundToInt(av * S.Rng.Range(0.4f, 0.6f));
                                if (toTake == 0) { continue; }
                                s.Take(mat.Key, null, toTake, null);
                            }
                        }
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failure while removing wealth from storage: {0}", s);
                }
                if (!GameTimer.HasAvailableFrameBudget()) {
                    yield return null;
                    if (!GameState.IsCurrent(S)) { yield break; }
                }
            }
            S.Wallet.TryPay(S.Wallet.Amount * S.Rng.Range(0.4f, 0.6f), null);
        }

        private IEnumerator SettleIn(SpaceObject target) {
            yield return Ready.WaitForSaveFull(this);
            Sounds.PlayMusic(SoundtrackMood.Majestic, true);

            Ready.SetSaveIO(false, "Settling in");
            S.UIQuery.ToggleGameUI.Ask(false, false);
            var ticks = S.Clock.Ticks;
            S.Clock.ForceStop(ForceStopReason.Cutscene);
            S.Sig.HideOverlay.Send();
            S.Sys.Terraform.KnownTerraformable.Remove(target);
            The.ScreenBlock.SetCenterText("", 0);
            The.ScreenBlock.Show(2);
            The.ScreenBlock.SetCenterText(T.SettlingIn_V1.F(target.DisplayName), 2f);
            ColonistFate.LoadAll();
            var arrays = S.Components.GenFind<StasisArrayComp>().ToArray();
            yield return RemoveWealth();
            foreach (var array in arrays) {
                ColonistFate.ForStasisArray(array);
                array.IsSentToColony = true;
                S.CmdQ.Enqueue(new CmdRemoveTile(array.Tile));
            }
            var remained = 0;
            foreach (var b in S.Beings.Alive.ToArray()) {
                if (!b.Data.EffectiveFaction.IsPlayer) { continue; }
                if (ColonistFate.ForBeing(b.Data)) {
                    remained++;
                } else {
                    BeingUtils.RemoveFromShip(S, b.Data, BeingState.InColony);
                }
            }
            var urns = new List<UrnNameComp>();
            ColonistFate.ForUrns(S, urns);
            TakeAshesDown(S, urns);

            ConvertPlanetToColonized(target);

#if PRODUCTION_BUILD
            yield return Consts.Wait2Sec;
#else
            if (!The.TestsAreRunning) {
                yield return Consts.Wait2Sec;
            }
#endif
            yield return The.ScreenBlock.HideCenterText(S, 1f, 2f);
            S.Sig.SettleIn.Send(target);

            yield return EndingCutscene.RollCredits(S, ColonistFate.Story(S, target.DisplayName));
            The.ScreenBlock.Hide(2f);
            S.UIQuery.ToggleGameUI.Ask(true, false);
            var date = S.Clock.ToDateTime(Consts.TickDurationInGameTime);
            var afterSettle = T.AfterSettle_V3.F(
                target.StarmapLink, Texts.DayFrom(date), remained);
            var data = StoryEventData.CreateCustom(IconId.CEnterSpaceObject,
                    T.SettledIn_V1.F(target.StarmapLink), afterSettle);
            S.Sig.AddCustomStoryEvent.Send(data, true);
            The.Dialog.ShowSimple(IconId.CComputing, afterSettle);

            foreach (var fate in ColonistFate.All) {
                fate.ClearAffected();
            }
            Ready.SetSaveIO(true, "Settling in");
            S.Clock.ForceResume(ForceStopReason.Cutscene);
            S.CmdQ.Enqueue(new CmdSetGameSpeed(ClockSpeed.Normal));
            target.Flags |= SOFlags.Settled;
            The.AchievementTracker.Unlock(AchievementColonize.AchievementId);
            after?.Invoke();
            lockHash = 0;
        }

        // The credits say these ashes went down to the new world (STA-1640), so they leave
        // the ship. The urns themselves stay behind, empty.
        internal static void TakeAshesDown(GameState S, List<UrnNameComp> urns) {
            foreach (var urn in urns) {
                if (!urn.Entity.TryGetComponent<ObjStorageComp>(out var storage)) { continue; }
                storage.ForEachStored((obj) => S.Query.DestroyStoredObj.Ask(obj));
                urn.CheckState();
            }
        }

        private void ConvertPlanetToColonized(SpaceObject target) {
            target.SetFaction(FactionDef.Player);
            List<Vector2> childLocations = new();
            if (target.HasChildren) {
                foreach (var child in target.Children) {
                    if (child.Type.CanBeOwned) {
                        child.SetFaction(target.Faction);
                    }
                    childLocations.Add(child.Location);
                }
            }

            var points = new Vector2[10];

            for (int i = 0; i < 10; i++) {
                points[i] = Rng.UInsideUnitCircle() * 5f;
            }

            var spacePortPos = GetBestPoint(points, childLocations);
            D.Warn("Space port pos: {0}", spacePortPos);

            var spacePort = S.Universe.CreateObject();
            target.AddChild(spacePort);
            spacePort.Location = spacePortPos;
            GenSpaceObjectSectorPlanet.TurnIntoPort(target, spacePort);

            var planetGen = GenUniverse.FindGenerator(target.Type.Id);
            var ssPoints = planetGen.GenerateSpiralPoints(S.Universe, target,
                1, GenSpaceObject.SpaceStationMinDist * 0.8f, Rng.Unseeded);

            var stationPoint = ssPoints[0];
            var tpl = new NPCShip();
            tpl.Class = ShipClassDef.FastGet(ShipClassIdH.SpaceStation);
            tpl.Faction = target.Faction;
            var station = S.Query.SpawnShipTplAt.Ask(target, tpl);
            station.TeleportToSectorLocation(stationPoint);

            S.UIQuery.RebuildStarmap.Ask(true, false);
        }

        private Vector2 GetBestPoint(Vector2[] options, List<Vector2> existing) {
            if (Arrays.IsEmpty(existing)) {
                return options[0];
            }

            // find option that is furthest away from existing: the one whose nearest existing
            // object is the farthest. The max over all pairs picked whichever option was far
            // from any single object, even one sitting right on top of another.
            float maxToClosest = -1f;
            Vector2 best = options[0];

            for (int i = 0; i < options.Length; i++) {
                var toClosest = float.MaxValue;
                foreach (var p in existing) {
                    var sqd = Vectors.SqrDistance(options[i], p);
                    if (sqd < toClosest) {
                        toClosest = sqd;
                    }
                }
                if (toClosest > maxToClosest) {
                    best = options[i];
                    maxToClosest = toClosest;
                }
            }

            return best;
        }
    }
}
