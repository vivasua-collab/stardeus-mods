using System.Collections;
using System.Text;
using Game.Audio;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Utils;

namespace Game.Story.Events {
    /// <summary>
    /// The Rogue AI ending, reached by hacking the Sentinel HQ in Main/EliminateSentinelHQ.
    /// Mirrors ColonizePlanetCutscene's choreography and shares its closing beats through
    /// EndingCutscene.RollCredits. Unlike the colonize ending nobody leaves the ship - the
    /// epilogue counts what the player built instead.
    /// See Docs/Architecture/Systems/RogueAIRoute.md.
    /// </summary>
    public sealed class RogueAIEndingCutscene {
        private readonly GameState S;
        private readonly int convertedHoldings;

        [SelfInit]
        private static void Reset() {
            IsRunning = false;
        }

        /// <summary>
        /// Tests wait on this rather than guessing how many frames the credits take.
        /// </summary>
        public static bool IsRunning;

        public RogueAIEndingCutscene(GameState s, int convertedHoldings) {
            if (IsRunning) {
                D.Err("Already running the Rogue AI ending cutscene!");
                return;
            }
            IsRunning = true;
            S = s;
            this.convertedHoldings = convertedHoldings;
            Coroutines.Start(Run(), this, "RogueAIEnding", false, false);
        }

        private IEnumerator Run() {
            var gameId = S.GameId;
            yield return Ready.WaitForSaveFull(this);
            // try/finally so a player who loads another save mid-credits can never be left
            // with save IO blocked or the clock force stopped.
            try {
                if (!IsStillPlaying(gameId)) { yield break; }
                Sounds.PlayMusic(SoundtrackMood.Majestic, true);

                Ready.SetSaveIO(false, "Rogue AI ending");
                // The hack is triggered from the starmap, which would otherwise sit on top
                // of the screen block for the whole sequence.
                S.UIQuery.ShowStarmap.Ask(false);
                S.UIQuery.ToggleGameUI.Ask(false, false);
                S.Clock.ForceStop(ForceStopReason.Cutscene);
                S.Sig.HideOverlay.Send();

                The.ScreenBlock.SetCenterText("", 0);
                The.ScreenBlock.Show(2);
                The.ScreenBlock.SetCenterText(T.SentinelEliminatedTitle, 2f);
                #if PRODUCTION_BUILD
                yield return Consts.Wait2Sec;
                #else
                if (!The.TestsAreRunning) {
                    yield return Consts.Wait2Sec;
                }
                #endif
                if (!IsStillPlaying(gameId)) { yield break; }
                yield return The.ScreenBlock.HideCenterText(S, 1f, 2f);

                yield return EndingCutscene.RollCredits(S, BuildEpilogue());
                if (!IsStillPlaying(gameId)) { yield break; }

                The.ScreenBlock.Hide(2f);
                S.UIQuery.ToggleGameUI.Ask(true, false);

                var msg = T.SentinelEliminatedMessage;
                var data = StoryEventData.CreateCustom(IconId.IconsFactionVirus,
                    T.SentinelEliminatedTitle, msg);
                S.Sig.AddCustomStoryEvent.Send(data, true);
                The.Dialog.ShowSimple(IconId.IconsFactionVirus, msg);

                S.CmdQ.Enqueue(new CmdSetGameSpeed(ClockSpeed.Normal));
            } finally {
                Ready.SetSaveIO(true, "Rogue AI ending");
                S.Clock.ForceResume(ForceStopReason.Cutscene);
                IsRunning = false;
            }
        }

        private bool IsStillPlaying(int gameId) {
            return GameState.IsCurrent(S) && S.GameId == gameId
                && The.ScreenBlock != null;
        }

        private string BuildEpilogue() {
            var sb = new StringBuilder();
            sb.Append(Texts.Accented(T.RogueEndingEpilogueIntro));
            sb.Append("\n\n");
            sb.Append(T.RogueEndingEpilogueSleepers_V1.F(Units.Num(CountElysiumSleepers())));
            sb.Append("\n");
            sb.Append(T.RogueEndingEpilogueCrew_V1.F(Units.Num(CountCrewAboard())));
            sb.Append("\n");
            Systems.RogueAIUtils.CountRogueNodes(S, out var unlocked, out var total);
            sb.Append(T.RogueEndingEpilogueTech_V2.F(unlocked, total));
            sb.Append("\n");
            sb.Append(T.RogueEndingEpilogueHoldings_V1.F(Units.Num(convertedHoldings)));
            sb.Append("\n\n");
            sb.Append(Texts.Accented(T.RogueEndingEpilogueOutro));
            return sb.ToString();
        }

        private int CountElysiumSleepers() {
            int cnt = 0;
            foreach (var array in S.Components.GenFind<StasisArrayComp>()) {
                if (array.Tile.GetComponent<StasisArrayMatrixComp>()?.IsElysium != true) {
                    continue;
                }
                cnt += array.BeingCount;
            }
            return cnt;
        }

        private int CountCrewAboard() {
            int cnt = 0;
            foreach (var b in S.Beings.Alive) {
                if (b.Data.EffectiveFaction.IsPlayer) {
                    cnt++;
                }
            }
            return cnt;
        }
    }
}
