using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Combat;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Achievements;
using Game.Systems.Items;
using Game.Systems.Notify;

namespace Game.Systems.AI.Combat {
    public sealed class AttackActionFacehug : AttackAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AttackActionFacehug());
        }
        public const string ActId = "Facehug";
        private const float AttachChance = 0.3f;
        private AttackActionFacehug() : base(ActId, () => T.Eating) { }

        public override void OnCharge(GameState s, AttackMethod method,
                CombatUnit unit, IAttackable target) {
            var p = target.AttackablePosition;
            if (method.ChargeTicks == 0) {
                // if (s.Sys.Path.IsCrowdedAt(unit.PosIdx, 1)) {
                //     unit.TryShuffle(s, target);
                // }
            } else if (method.ChargeTicks >= method.EffectiveFullChargeTicks - 1) {
                if (unit.Agent.Being.Graphics.PunchAtWithCallback(
                        p - unit.Position,
                        0.7f, 0.2f, 0.3f, () => {
                            if (Rng.UChance(0.1f)) {
                                Sounds.PlayRandomAt(p,
                                    unit.Agent.BeingData.Species.PainSounds, true);
                            }

                            // try to facehug
                            if (target is CombatUnit cu) {
                                if (cu.Agent.BeingData.Species.IsHumanoid) {
                                    // Only an actual crawler body can complete the attach:
                                    // DoFacehug hides the attacker and CrawlerFacehugComp
                                    // respawns it as a Crawler. Anyone else wearing a grafted
                                    // crawler maw still knocks hats and helmets off, and then
                                    // always fails - which is the fun part.
                                    var isCrawler = unit.Agent.BeingData.Species.IdH
                                        == SpeciesIdH.Crawler;
                                    TryFacehug(s, unit, cu, isCrawler ? AttachChance : 0f);
                                }
                            }
                        })) {
                    if (Rng.UChance(0.1f)) {
                        // we started the attack
                        var attackSounds = method.AttackSounds ?? unit.Data.Species.AttackSounds;
                        Sounds.PlayRandomAt(unit.Position, attackSounds, true);
                    }
                }
            }
            FacePosition(unit, p);
        }

        public static bool TryFacehug(GameState s, CombatUnit crawler, CombatUnit prey,
                float chance = AttachChance) {
            if (crawler.Data.HasStateFlag(BeingState.Dead)) {
                D.Warn("Dead crawler trying to attach itself! {0}", crawler);
                return false;
            }
            var equipment = prey.Agent.BeingData.Inventory?.Equipment;
            if (equipment == null) {
                D.Err("No equipment on humanoid! {0}", prey);
                return false;
            }
            if (equipment.TryGetValue(InventorySlotIdH.Helmet, out var helmet)) {
                if (helmet.Item != null) {
                    // try to know off the helmet
                    if (Rng.UChance(0.5f)) {
                        // D.Warn("Removing the helmet!");
                        helmet.RemoveItem();
                    // } else {
                    //     D.Warn("Chance to remove helm failed");
                    }
                    return false;
                }
            }
            if (equipment.TryGetValue(InventorySlotIdH.Hat, out var hat)) {
                if (hat.Item != null) {
                    // try to know off the hat
                    if (Rng.UChance(0.8f)) {
                        // D.Warn("Removing the hat!");
                        hat.RemoveItem();
                    // } else {
                    //     D.Warn("Chance to remove hat failed");
                    }
                    return false;
                }
            } else {
                D.Err("Cannot attach, no hat slot on humanoid species: {0}", prey);
                return false;
            }
            if (!Rng.UChance(chance)) {
                // D.Warn("Chance to facehug failed");
                return false;
            }
            return DoFacehug(s, crawler, prey, hat);
        }

        private static bool DoFacehug(GameState s, CombatUnit crawler, CombatUnit prey, InventoryItemSlot hat) {
            try {
                var def = The.Defs.FastGet(DefIdsH.ObjEquipmentHatsCrawlerFacehug);
                var crawlerBeing = crawler.Agent.Being;
                var facehugHat = s.Query.GenObjFull.Ask(def, Rng.Unseeded);
                s.Query.PersistObjData.Ask(facehugHat);

                // equip facehugger hat
                hat.Equip(facehugHat);

                // MUST SET being data id to crawler id AFTER equipping, or the beingDataId will be overwritten by human id!
                facehugHat.BeingDataId = crawlerBeing.Data.Id;
                prey.Agent.BeingData.Mind.Health?.AddEffect(s, MentalEffect.Create(
                    prey.PosIdx,
                    G.Ticks, Consts.TicksPerDay, T.BreakdownCrawlerattached, -100));
                crawlerBeing.Data.StateFlags |= BeingState.Hidden;
                s.Beings.Destroy(crawlerBeing);

                if (prey.Agent.DirectControl.IsUnderDirectControl) {
                    prey.Agent.DirectControl.SetDirectControl(false);
                }

                if (prey.Data.IsFriendly) {
                    // notify
                    var data = Notification.Build(IconId.CWarning, T.BreakdownCrawlerattached, 0, NotificationType.Alert)
                        .Group("crawl_att");

                    data.Being = prey.Agent.BeingData;
                    data.Show();
                    // this will trigger validation issue
                    // prey.Agent.BB.ShowNotification(data);
                    var msg = T.BreakdownCrawlerattachedDesc_V1.F(prey.Agent.BeingData.Name);
                    s.Sys.Log.AddLine(prey.Agent.Being, msg);
                    s.Sys.Log.AddLine(crawlerBeing, msg);

                    The.AchievementTracker.Unlock(
                        AchievementCrawlerAttach.AchievementId);
                }
                s.Sig.CrawlerAttached.Send(crawler.Agent.BeingData, prey.Agent.BeingData);
                return true;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failure during facehug! {0}->{1}", crawler, prey);
                return false;
            }
        }
    }
}
