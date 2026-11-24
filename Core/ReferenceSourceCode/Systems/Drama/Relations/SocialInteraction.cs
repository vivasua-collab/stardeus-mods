using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Story;
using Game.Utils;
using KL.Collections;
using KL.Utils;

namespace Game.Systems.Drama {
    public sealed class SocialInteraction {
        /// <summary>
        /// This adds all participants without custom weights, so it will create a
        /// symmetrical opinion change. If you want to add participants with custom
        /// weights, use <see cref="For(StoryLogEntry)"/> and <see cref="With(BeingData, float)"/>
        /// to add participants.
        /// </summary>
        public static SocialInteraction For(StoryEventData data) {
            var soc = For(data.LogEntry);
            if (Arrays.IsEmpty(data.Participants)) {
                D.Err("Creating social interaction from StoryEventData with no participants! {0}", data);
            } else {
                foreach (var p in data.Participants) {
                    soc.With(p);
                }
            }
            return soc;
        }
        public static SocialInteraction For(StoryLogEntry entry) {
            if (!entry.IsValid) {
                D.Err("Creating social interaction from entry that is not valid! {0}", entry);
                return new SocialInteraction();
            }
            return Create(entry.Icon, entry.Title, entry.Desc);
        }
        public static SocialInteraction Create(string icon, string title, string desc) {
            return new SocialInteraction() {
                Icon = icon,
                Title = title,
                Desc = desc,
            };
        }

        public SocialInteraction WithAttitude(float change) {
            AttitudeChange = change;
            return this;
        }
        public SocialInteraction WithLove(float change) {
            LoveChange = change;
            return this;
        }
        public SocialInteraction WithHate(float change) {
            HateChange = change;
            return this;
        }
        public SocialInteraction WithFriendship(float change) {
            FriendshipChange = change;
            return this;
        }

        public SocialInteraction With(BeingData bd) {
            return With(bd.Mind);
        }
        public SocialInteraction With(Mind m) {
            #if !PRODUCTION_BUILD
            if (InvolvedMinds.Contains(m)) {
                D.Err("Adding duplicate mind to InvolvedMinds for {0}", this);
            }
            #endif
            InvolvedMinds.Add(m);
            return this;
        }
        public SocialInteraction With(BeingData bd, float changeWeight) {
            return With(bd.Mind, changeWeight);
        }
        public SocialInteraction With(Mind m, float changeWeight) {
            #if !PRODUCTION_BUILD
            if (InvolvedMinds.Contains(m)) {
                D.Err("Adding duplicate mind to InvolvedMinds for {0}", this);
            }
            #endif
            if (InvolvedMinds.Length == ChangeWeights.Length) {
                ChangeWeights.Add(changeWeight);
            } else {
                D.Err("Can't add being with change weight to social interaction, since some beings were added without weight before! Add them ALL with weights one by one for this to work!: {0}",
                    this);
            }
            InvolvedMinds.Add(m);
            return this;
        }

        public void AppendTooltip(StringBuilder sb, bool list) {
            string title;
            var desc = Desc;
            if (list && desc.Contains(GlyphMap.BRHR)) {
                desc = desc.Substring(0, desc.IndexOf(GlyphMap.BRHR));
            }
            if (AttitudeChange > 0f) {
                title = Texts.Green(desc);
            } else {
                title = Texts.Red(desc);
            }
            sb.AppendFormat(" {0} {1} {2}\n",
                Texts.Accented(GlyphMap.ArrowRight, bold: false), title,
                Texts.GreyParentheses(Units.TicksAgoFromNow(CreatedAt)));
        }

        /// <summary>
        /// Don't add to this directly, use .With(b)
        /// </summary>
        public FrugalArray<Mind> InvolvedMinds;
        /// <summary>
        /// Sometimes we may want to apply a social interaction asymmetrically.
        /// For example, if one being saves another one from death, the one
        /// who was saved should have full change effect, while the hero should
        /// have probably close to 0. The index of the change ratio should match
        /// the index of InvolvedMinds.
        /// If this is empty, all ratios will default to 1;
        /// </summary>
        public FrugalArray<float> ChangeWeights;
        public float AttitudeChange;
        public float FriendshipChange;
        public float LoveChange;
        public float HateChange;
        /// <summary>
        /// Drives master/slave opinion value (master on human, slave on pet);
        /// </summary>
        public float OwnershipChange;
        public string Icon;
        public string Title;
        public string Desc;
        public long CreatedAt;
        // multipliers are not saved
        public float AttitudeMul_Friendship;
        public float AttitudeMul_Love;
        public float AttitudeMul_Hate;
        public float AttitudeMul_Pet;
        public override string ToString() {
            return $"SocInt[m1:{InvolvedMinds.First},m2:{InvolvedMinds.Second},{Title},{Desc}]";
        }
    }
}
