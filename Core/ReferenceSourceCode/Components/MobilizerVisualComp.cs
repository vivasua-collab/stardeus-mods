using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components {
    public sealed class MobilizerVisualComp : BaseComponent<MobilizerVisualComp>, ILooksFilter {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MobilizerVisualComp());
        }
        private bool isDefaultGrounded;
        private string looksDefault;

        protected override void OnConfig() {
            isDefaultGrounded = Config.GetBool(PropertyIdH.IsDefaultGrounded);
        }

        public bool FilterLooks(SpeciesLooksDef looksDef, BeingLooks looks) {
            var mobSlots = Being.Data.Body.MobilizerSlots;
            if (mobSlots == null) {
                D.Err("No mobilizer slot for being with MobilizerVisual component! {0}",
                    entity.Definition);
                return false;
            }

            bool changed = false;
            bool isEmpty = true;
            bool canFly = false;
            // Can't use Being.Data.HasAbility here, it's too early
            for (int i = mobSlots.Length - 1; i >= 0; i--) {
                Systems.BodyParts.BodyPartSlot part = mobSlots[i];
                if (part.IsPartRemoved) { continue; }
                isEmpty = false;
                if (Arrays.ContainsSafe(part.InstalledDef.AddsAbilities, AbilityId.Fly)) {
                    canFly = true;
                    break;
                }
            }
            var needAltLooks = isDefaultGrounded && canFly || !isDefaultGrounded && !canFly;

            foreach (var partDef in looksDef.PartDefs) {
                if (!partDef.MobilizerAffected) {
                    continue;
                }
                var part = looks.Get(partDef.Type);
                if (part == null) {
                    D.Err("PANIC - no part in being looks: {0} -> {1}", partDef.Type, Being);
                    continue;
                }
                if (partDef.MobilizerAltPosFilter != null) {
                    if (isEmpty) {
                        part.PosFilter = null;
                        part.PosFilterH = -1;
                    } else if (needAltLooks) {
                        part.PosFilter = partDef.MobilizerAltPosFilter;
                        part.PosFilterH = 0;
                    } else {
                        part.PosFilter = partDef.PositionFilter;
                        part.PosFilterH = 0;
                    }
                }
                if (partDef.MobilizerAltOffsetY != 0f || partDef.MobilizerEmptyOffsetY != 0f) {
                    if (isEmpty) {
                        part.Offset = partDef.Offset + new Vector2(0f, partDef.MobilizerEmptyOffsetY);
                    } else if (needAltLooks) {
                        part.Offset = partDef.Offset + new Vector2(0f, partDef.MobilizerAltOffsetY);
                    } else {
                        part.Offset = partDef.Offset;
                    }
                }
                if (partDef.MobilizerAltHidden) {
                    if (needAltLooks) {
                        part.Color.a = 0f;
                    } else {
                        part.Color.a = 1f;
                    }
                } else if (partDef.MobilizerDefaultHidden) {
                    if (needAltLooks) {
                        part.Color.a = 1f;
                    } else {
                        part.Color.a = 0f;
                    }

                }
                if (partDef.MobilizerLooksDefault == null) {
                    continue;
                }
                var spriteId = part.SpriteId;
                var looksAlt = partDef.MobilizerLooksAlt;
                var looksEmpty = partDef.MobilizerLooksEmpty;
                if (string.IsNullOrWhiteSpace(looksEmpty)) {
                    looksEmpty = null;
                }
                var looksDefault = partDef.MobilizerLooksDefault;
                if (isEmpty) {
                    changed |= SetPartSprite(part, spriteId, looksEmpty, looksAlt, looksDefault);
                } else if (needAltLooks) {
                    changed |= SetPartSprite(part, spriteId, looksAlt, looksEmpty, looksDefault);
                } else {
                    if (looksEmpty != null && spriteId.Contains(looksEmpty)) {
                        part.SpriteId = spriteId.Replace(looksEmpty, looksDefault);
                        changed = true;
                    } else if (spriteId.Contains(looksAlt)) {
                        part.SpriteId = spriteId.Replace(looksAlt, looksDefault);
                        changed = true;
                    }
                }
            }

            if (entity.TryGetComponent<ParticleTrailComp>(out var trail)) {
                bool needsParticles = !isEmpty && !needAltLooks;
                if (needsParticles) {
                    trail.ReenableParticles();
                } else {
                    trail.DisableParticles();
                }
            }

            return changed;
        }

        private static bool SetPartSprite(LookPart part, string spriteId,
                string wantSpr, string otherSpr, string defSpr) {
            if (wantSpr != null) {
                if (!spriteId.Contains(wantSpr)) {
                    if (otherSpr != null && spriteId.Contains(otherSpr)) {
                        part.SpriteId = spriteId.Replace(otherSpr, wantSpr);
                        return true;
                    } else {
                        part.SpriteId = spriteId.Replace(defSpr, wantSpr);
                        return true;
                    }
                }
            } else if (spriteId.Contains(otherSpr)) {
                part.SpriteId = spriteId.Replace(otherSpr, defSpr);
                return true;
            }
            return false;
        }

        public override string ToString() {
            return " * MobilizerVisual";
        }
    }
}
