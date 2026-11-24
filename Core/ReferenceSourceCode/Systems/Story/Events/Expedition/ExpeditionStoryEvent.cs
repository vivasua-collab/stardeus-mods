using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Expeditions;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Story {
    public abstract class ExpeditionStoryEvent : StoryEvent {
        public virtual bool IsAvailableFor(Expedition exp) {
            return true;
        }
        public virtual bool IsAvoidable(StoryEventData data, Expedition exp) {
            return true;
        }
        public virtual bool IsCancellable(StoryEventData data, Expedition exp) {
            return true;
        }

        protected bool IsShowingChoice(StoryEventData data) {
            return data.Temp.GetBool("IsShowingChoice", false);
        }
        protected void SetShowingChoice(StoryEventData data, bool yes) {
            if (yes) {
                data.Temp.SetBool("IsShowingChoice", true);
            } else {
                data.Temp.RemoveBool("IsShowingChoice");
            }
        }
        public virtual bool ShouldRemovePOI => true;
        public IExpeditionStage ForceNextStage { get; protected set; }
        public abstract string Title { get; }
        public ShuttleComp Shuttle(StoryContext ctx, StoryEventData data) {
            if (data.TargetTile == null) {
                D.Err("Shuttle not set on expedition story event data! {0}", data);
                return null;
            }
            return data.TargetTile.GetComponent<ShuttleComp>();
        }
        public Expedition GetExpedition(StoryContext ctx, StoryEventData data) {
            var target = data.TargetSO;
            return ctx.S.Sys.Expedition.FindForSpaceObject(target);
        }

        // On failure a random crew member gets wounded (possibly killed) and the player is told
        protected static bool AttemptToRecover(Expedition exp, Rng rng, float chance) {
            if (rng.Chance(chance)) {
                return true;
            }
            if (exp.Crew.Count == 0) {
                WPopup.Warning(T.ExpFoundObjFail, T.ExpFoundObjFailNocrew);
                return false;
            }
            var crewMember = rng.From(exp.Crew);
            var dmgType = DamageUtils.RandomCombat(rng);
            var numWounds = rng.Range(1, 4);
            var wndSrc = NamedEntity.For(T.Expedition);
            var survived = BeingUtils.DealHistoricalDamage(exp.S, crewMember, dmgType, 0,
                numWounds, wndSrc, Severity.Moderate, Severity.Critical, Severity.Severe,
                Severity.Irreparable);
            var name = crewMember.Mind.Name.DisplayName;
            var msg = survived
                ? T.ExpFoundObjFailWounded_V2.F(name, numWounds)
                : T.ExpFoundObjFailDied_V1.F(name);
            WPopup.Warning(T.ExpFoundObjFail, msg);
            return false;
        }

        public virtual void Abort(StoryEventData data, Expedition exp) {
            data.SetState(exp.S, StoryEventState.Ending);
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var shuttle = Shuttle(ctx, data);
            if (shuttle == null || !shuttle.IsOut) {
                data.FailEvent(ctx.S,
                $"Expedition event is running while shuttle is null or not out! {shuttle}");
            }
        }

        public abstract void TickExpedition(StoryEventData data, Expedition exp);

        public override string ToString() {
            return $"ExpeditionStoryEvent [{Id}]";
        }

        public virtual void DiscoverEvent(StoryContext ctx, StoryEventData data, Expedition exp) {
            // D.Log("Discovered expedition event: {0}", this);
        }

        public virtual void StartExpedition(StoryContext ctx, StoryEventData data, Expedition exp) {
            // D.Log("Starting expedition event: {0}", this);
        }
    }
}
