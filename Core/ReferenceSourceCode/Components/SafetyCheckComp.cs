using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Notify;
using Game.Systems.Items;

namespace Game.Components {
    public sealed class SafetyCheckComp : BaseComponent<SafetyCheckComp>,
            IRareTickListener, IRelocatable {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new SafetyCheckComp());
        }

        // private EventNotification warningNot;
        private NotificationHandle warning;

        public override string ToString() {
            return " * Safety Check";
        }

        private float tempLow;
        private float tempHigh;
        public bool IsSafe => isSafe;
        private bool isSafe;
        public bool IsIgnored => isIgnored;
        private bool isIgnored;
        public string LastWarning => lastWarning;
        private long lastCheckAt;

        protected override void OnConfig() {
            tempLow = Units.CelsiusToKelvin(Config.GetInt(PropertyIdH.MinTempCelsius, -20));
            tempHigh = Units.CelsiusToKelvin(Config.GetInt(PropertyIdH.MaxTempCelsius, 40));
            isSafe = true;
        }

        public void RareTick(long currentTick) {
            Check(currentTick, false);
        }

        // Friendlies minus the bots is every player being that breathes - colonists and
        // animals; add the guests and that is everyone aboard whom vacuum or extreme
        // temperature can hurt. Bots are neither, which is why the wrecked-ship openings stay
        // quiet until the first stasis wake-up. BeingsSys re-checks every device the moment
        // this flips, see RebuildBeingsSituation.
        public static bool HasBeingsToProtect(GameState s) {
            var sit = s.Situation;
            return sit.Friendlies - sit.Workers > 0 || sit.Guests > 0;
        }

        // Re-evaluates every SafetyCheckComp at once, called by BeingsSys the moment
        // HasBeingsToProtect starts being true, so the player sees the real picture
        // immediately rather than up to 15 in-game minutes later on the next rare tick.
        public static void RecheckAll(GameState s) {
            if (!Ready.AreasInitial) { return; }
            var comps = s.Components.AllByType<SafetyCheckComp>();
            if (comps == null) { return; }
            var tick = s.Ticks;
            foreach (SafetyCheckComp c in comps) {
                c.Check(tick, true);
            }
        }

        // forced bypasses the lastCheckAt bookkeeping below: while nobody who can be hurt
        // by vacuum or cold is aboard, no comp ever reaches that bookkeeping, so every
        // lastCheckAt is still 0 and a normal (unforced) call would burn its "first tick is
        // a warm-up" skip and measure nothing.
        private void Check(long currentTick, bool forced) {
            if (isIgnored) { return; }
            if (!HasBeingsToProtect(S)) { return; }
            // If there is active construction somewhere, we may want to use
            // stale data instead of just returning
            // We will only check for this for the first time

            if (Tile?.IsConstructed == false) {
                return;
            }
            if (!S.Prefs.GetBool(Pref.WarnUnsafeArea, PrefH.WarnUnsafeArea, true)) {
                return;
            }
            if (Tile?.Health.IsDamaged == true) {
                return;
            }
            if (!forced) {
                if (lastCheckAt == currentTick) {
                    return;
                }
                if (lastCheckAt == 0) {
                    lastCheckAt = currentTick;
                    return;
                }
            }
            lastCheckAt = currentTick;
            #if !PRODUCTION_BUILD
            // TODO remove if never seen the error
            if (!Ready.AreasInitial) {
                D.Err("Initial areas not ready for safety rare tick!");
                return;
            }
            #endif
            var pos = Entity.PosIdx;
            if (pos < 1) {
                D.Err("Uninitialized POS during RareTick: {0}", Entity);
            }
            if (Obj != null) {
                // Ignore carried entities
                if (Obj.Carrier != null) {
                    return;
                }
                // Ignore doorways
                if (EntityUtils.IsDoorAt(pos)) {
                    return;
                }
            }
            var noOxygen = S.GetO2(pos) < Consts.MinOxygen;
            var heat = S.GetTemp(pos);
            var tooHot = heat > tempHigh;
            var tooCold = heat < tempLow;
            UpdateWarning(currentTick, noOxygen, tooHot, tooCold);
        }

        private bool lastNoOxygen;
        private bool lastTooHot;
        private bool lastTooCold;
        private string lastWarning;

        private void UpdateWarning(long tick, bool noOxygen, bool tooHot, bool tooCold) {
            var wasSafe = isSafe;
            isSafe = !noOxygen && !tooHot && !tooCold;
            if (isSafe) {
                if (!wasSafe) {
                    RemoveWarning();
                }
                return;
            }
            if (warning.IsValid &&
                    lastNoOxygen == noOxygen && lastTooHot == tooHot && lastTooCold == tooCold) {
                lastWarning = null;
                return;
            }
            lastNoOxygen = noOxygen;
            lastTooCold = tooCold;
            lastTooHot = tooHot;
            string msg;
            if (noOxygen) {
                if (tooHot) {
                    msg = T.SafetyLacksOxygenTooHot;
                } else if (tooCold) {
                    msg = T.SafetyLacksOxygenTooCold;
                } else {
                    msg = T.SafetyLacksOxygen;
                }
            } else {
                if (tooHot) {
                    msg = T.SafetyTooHot;
                } else if (tooCold) {
                    msg = T.SafetyTooCold;
                } else {
                    msg = T.ObjectInUnsafeArea;
                    D.Err("Unexpected situation, enough of everything but creating safety warning!");
                }
            }
            lastWarning = msg;
            ShowInfoIcon(IconId.CDanger, msg);
            if (warning.IsValid) { return; }
            var data = new NotificationData();
            data.Type = NotificationType.Warning;
            data.GroupId = "unsafe_loc";
            data.GroupIcon = IconId.CDanger;
            data.GroupTitle = T.ObjectInUnsafeArea;
            data.Icon = entity.Definition.Preview;;
            if (Obj?.Data != null) {
                data.IconTint = Obj.Data.Color;
            }
            data.Title = T.ObjectInUnsafeArea;
            data.TooltipFunc = () => lastWarning;
            data.WithOnHover((h) => {
                EntityUtils.ShowEntityLink(null, Entity, h);
            });
            data.Flags |= NotificationFlags.BellRare;
            data.Obj = Obj?.Data;
            data.Tile = Tile;
            warning = S.Query.AddNotification.Ask(data);
            // warningNot = EventNotification.CreateGroupable(tick, "unsafe_loc",
            //     UDB.Create(this, UDBT.IEvent, IconId.CDanger,
            //         T.ObjectInUnsafeArea).WithTooltip(msg), Priority.Low)
            //         .WithFlag(EventNotificationFlags.BellRare);
            // S.Sig.AddEvent.Send(warningNot);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("Ignore", isIgnored);
        }

        public void SetIgnored(bool ignored) {
            if (ignored) {
                RemoveWarning();
            }
            isIgnored = ignored;
        }

        public override void OnRemove() {
            Notification.Remove(ref warning);
        }

        protected override void OnLoad(ComponentData data) {
            isIgnored = data.GetBool("Ignore", false);
        }

        private void RemoveWarning() {
            HideInfoIcon();
            isSafe = true;
            lastNoOxygen = false;
            lastTooHot = false;
            lastTooCold = false;
            lastWarning = null;
            Notification.Remove(ref warning);
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<SafetyCheckComp>(out var sc)) {
                sc.isIgnored = isIgnored;
            }
        }
    }
}
