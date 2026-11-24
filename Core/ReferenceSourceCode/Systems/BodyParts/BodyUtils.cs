using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.BodyParts {
    public static class BodyUtils {
        public static string StateName(BodyState state, bool IsBiological) {
            if ((state & BodyState.Alive) == BodyState.Alive) {
                if ((state & BodyState.Incapacitated) == BodyState.Incapacitated) {
                    return Texts.Yellow(T.BeingStateIncapacitated);
                } else if ((state & BodyState.Injured) == BodyState.Injured) {
                    if (IsBiological) {
                        return Texts.Yellow(T.BodyStateInjured);
                    } else {
                        return Texts.Yellow(T.BodyStateDamaged);
                    }
                } else if ((state & BodyState.Recovering) == BodyState.Recovering) {
                    return Texts.Green(T.BodyStateRecovering);
                } else if ((state & BodyState.Unhealthy) == BodyState.Unhealthy) {
                    return Texts.Yellow(T.BodyStateUnhealthy);
                } else {
                    return Texts.Green(T.BodyStateHealthy);
                }
            } else {
                return Texts.Red(T.Dead);
            }
        }
        public static bool HasWorkingImplant(BeingData being, int idH) {
            var implant = being.Body?.ImplantSlot;
            if (implant != null) {
                if (implant.IsPartRemoved) {
                    return false;
                }
                if (implant.InstalledDef?.Def.IdH == idH) {
                    return implant.State?.IsUsableWithParent == true;
                }
            }
            return false;
        }
        public static string DecoratedPartName(string name, BodyPartDef part,
                BodyPartState state, Mind mind) {
            if (part.HoldsMind) {
                if (mind != null) {
                    name = mind.DecorateListName(name);
                } else {
                    name = $"{name} ({Texts.Grey(T.Empty)})";
                }
            }
            if (state != null) {
                if (state.IsInfected) {
                    name = $"{GlyphMap.SpritePoison} {name}";
                }
                var ws = state.WoundSeverity;
                if (ws >= Severity.Irreparable) {
                    name = $"{GlyphMap.SpriteCrutch} {name}";
                } else if (ws > Severity.Irrelevant) {
                    var wounds = state.Wounds;
                    if (ws >= Severity.Critical
                            || wounds == null
                            || wounds.Count != 1
                            || wounds[0].Type != DamageType.Decay) {
                        name = $"{GlyphMap.SpriteBlood} {name}";
                    }
                }
            }

            return name;
        }
    }
}
