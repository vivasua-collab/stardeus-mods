using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Naming;
using Game.CodeGen;
using Game.Data.Factions;
using Game.Input;

namespace Game.Systems.Story {
    public class CapsuleBeingStoryEvent : StoryEvent {
        public const string EventId = "CapsuleBeing";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleBeingStoryEvent());
        }

        private readonly Dictionary<int, float> choices = new() {
            { SpeciesIdH.CleaningBot, 0.33f },
            { SpeciesIdH.Drone, 0.25f },
            { SpeciesIdH.Carrier, 0.25f },
            { SpeciesIdH.Human, 0.15f },
            { SpeciesIdH.Pig, 0.1f },
            { SpeciesIdH.Cat, 0.1f },
            { SpeciesIdH.Dog, 0.1f },
            { SpeciesIdH.Robot, 0.1f },
            { SpeciesIdH.Sentry, 0.05f },
            { SpeciesIdH.Orbotron, 0.01f },
        };

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            var rng = new Rng(data.Seed);
            var speciesId = rng.WeightedFrom(choices);
            var vars = new GenBeingVars(Species.Get(speciesId), BeingType.Friendly);
            vars.Body = FactionDef.RandomBodyVars(rng);
            var beingData = ctx.S.Query.GenBeingFull.Ask(vars, rng);
            BeingUtils.DealHistoricalDamage(
                ctx.S, beingData, DamageType.Blunt, Consts.TicksPerWeek,
                rng.Range(2, 4), null, Severity.Mild, Severity.Moderate,
                Severity.Severe, Severity.Critical);
            var posIdx = data.TargetObj?.Obj?.PosIdx ?? 0;
            var being = ctx.S.Beings.Create(EntityUtils.CenterOf(posIdx), beingData);
            var ff = CapsuleOpenHelper.ContainedText(data,
                Texts.LinkToBeing(being.Data, being.Species.Name));
            ff = Texts.Green(ff);
            ctx.S.Sys.Log.AddLine(ff);
            data.TargetBeing = being.Data;
            data.CreateLogEntry(
                being.Definition.Preview,
                $"{CapsuleOpenHelper.ContainerName(data)} {GlyphMap.ArrowRight} {being.Species.Name}",
                T.EvtCapsuleBeingDesc_V1.F(being.Data.Mind.Name.DisplayName));
            data.ShowPopup();
            BeingUtils.ShowColonyGreetingFor(ctx.S, being, null);
            if (A.FocusOnEvents) {
                PauseClock();
                EntityUtils.CameraFocusOn(being, true);
            }
            BeingUtils.AddInitialMentalBoostFor(being.Data, 3);
        }
    }
}
