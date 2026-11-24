#if !PRODUCTION_BUILD
#if UNITY_EDITOR
// don't debug force stop in player tests
// #define DEBUG_FORCE_STOP
#endif
// #define DEBUG_CLOCK_SET_SPEED
// #define DEBUG_UNLIMITED
// #define DEBUG_CLOCK
// #define DEBUG_CLOCK_SKIP
// #define DEBUG_LOCKS
// #define DEBUG_TICK_PROG
#else
// debug force stop in production
#define DEBUG_FORCE_STOP
#endif
using System;
using KL.Signals;
using KL.Utils;
using System.Collections.Generic;
using Game.Data;
using Game.Constants;
using UnityEngine;
using Unity.Mathematics;
using Game.Procgen;
using Game.Utils;
using Game.UI;
using Game.CodeGen;
using Game.Commands;
using Game;
using System.Text;
using Game.Systems.UIUX;

namespace KL.Clock {
    public class Clock {
        private int FixedUpdatesPerSec => Tunables.FixedUPS;
        // WHY fields in every build: Workshop mods are compiled against the release Game.dll and
        // bind to these as fields. Dev-only properties made every dev-side check (validation
        // players, ModApiCompatTests) see another API than the one that ships (2026-10-24).
        [NonSerialized] public long Ticks;
        [NonSerialized] public float TickProgress;
        [NonSerialized] public ClockSpeed Speed;
        [NonSerialized] public ClockSpeed MinSpeed;
        [NonSerialized] public ClockSpeed MaxSpeed;
        [NonSerialized] public float TicksPerUpdate;
        [NonSerialized] public float TickDuration;
        [NonSerialized] public bool IsForceStopped;
        [NonSerialized] public ForceStopReason StopReasons;
        [NonSerialized] public float FXTimeScale;
        [NonSerialized] public float TimeScale;
        [NonSerialized] public bool IsRestricted;
        [NonSerialized] public ushort UPS;
        [NonSerialized] public bool IsTickLocked;
        [NonSerialized] public long LagSkips;
        public readonly BLong TickB = new(0, 256);
        private long lastAutoPauseDangerTick;
        private bool wantsDangerPause;
        public bool IsStopped => Speed == ClockSpeed.Stopped;
        public bool IsUnlimited => Speed == ClockSpeed.Unlimited;
        public ClockSpeed NextSpeed {
            get {
                if (IsRestricted) {
                    if (Speed >= MaxSpeed) {
                        return ClockSpeed.Normal;
                    }
                }
                switch (Speed) {
                    case ClockSpeed.Normal: return ClockSpeed.Fast1;
                    case ClockSpeed.Fast1: return ClockSpeed.Fast2;
                    default: return ClockSpeed.Normal;
                }
            }
        }
        private int framesSinceLastTickProgress;
        private int framesOnLastTick;
        private float framesPerTick;
        private int fixedUpdatesSinceLastTick;
        private int fixedUpdatesPerTick;
        private readonly float upsNormal;
        private readonly float upsFast1;
        private readonly float upsFast2;
        private float restrictSecondsLeft;
        private readonly List<(long, Action)> afterCallbacks = new();
        #if DEBUG_CLOCK
        public uint TickUpdates;
        #endif
        /// <summary>
        /// For updating being graphics and moving objects
        /// </summary>
        public Signal1<float> OnTickProgress = new(nameof(OnTickProgress));
        /// <summary>
        /// For something that needs to happen at ~60 FPS (icons). This also runs even if clock is stopped
        /// </summary>
        public Signal0 OnUpdate60 = new(nameof(OnUpdate60));
        public Signal1<long> OnTickEarly = new(nameof(OnTickEarly));
        public Signal1<long> OnTick = new(nameof(OnTick));
        public Signal1<long> OnTickLate = new(nameof(OnTickLate));
        /// <summary>
        /// Guaranteed to be invoked at least once per every tick or once per every frame, whichever happens more frequently.
        /// Will run when clock is stopped too
        /// </summary>
        public Signal0 EveryTickOrFrame = new(nameof(EveryTickOrFrame));
        private int lastETOFFrame;
        private long lastETOFTick;
        // Listener time of the tick signals, read and reset every frame by HitchMonitor
        internal readonly SignalTiming TickTiming = new();
        /// <summary>Second parameter is tick duration</summary>
        public Signal1<ClockSpeed> OnSpeedChange = new(nameof(OnSpeedChange));
        public Signal1<ClockSpeed> OnRestrictionChange = new(nameof(OnRestrictionChange));
        public Signal1<bool> OnForceStopChange = new(nameof(OnForceStopChange));
        private readonly Dictionary<int, string> locks = new();
        // Real time since locks first held back a tick that was due, 0 while ticks go
        // through or the clock is paused. ValidateLocks gives up on locks after 60 s of
        // that. Measuring from the last load or unpause instead cleared every lock taken
        // 60 s into play as if it were stuck, letting ticks run during path jobs
        private double tickBlockedSince;
        private ClockSpeed lastToggleSpeed = ClockSpeed.Normal;
        private GameState S;
        private bool hadTickProgressSinceLastTick;
        private bool wantsAfterTickUpdate;
        private event Action onNextUpdate;
        private event Action onNextTick;
        private bool isExecutingOnNextUpdate;
        private bool isExecutingOnNextTick;
        public void OnNextUpdate(Action act) {
            if (isExecutingOnNextUpdate) {
                D.Err("Already executing OnNextUpdate! Can't enqueue another action right now! {0}", act);
                S.ActQ.Enqueue(act);
            } else {
                onNextUpdate += act;
            }
        }
        public void OnNextTick(Action act) {
            if (isExecutingOnNextTick) {
                D.Err("Already executing OnNextTick! Can't enqueue another action right now! {0}", act);
                executeAfterTick.Add((act, Ticks));
            } else {
                onNextTick += act;
            }
        }
        private readonly List<(Action, int)> executeAfterFrame = new();
        /// <summary>
        /// This should ONLY be used for UI, not for game logic, because at high speeds
        /// it will just not work.
        /// </summary>
        /// <param name="framesToWait"></param>
        /// <param name="action"></param>
        public void ExecuteAfterFramesForUI(int framesToWait, Action action) {
            if (framesToWait > 100) {
                D.Err("Trying to wait for > 100 frames, this is not recommended or a bug! This hook will be lost after save / load! {0}",
                    framesToWait);
            }
            var nextFrame = GameTimer.frameCount + framesToWait;
            executeAfterFrame.Add((action, nextFrame));
        }
        private readonly List<(Action, long)> executeAfterTick = new();
        public void ExecuteAfterTicks(int ticksToWait, Action action) {
            if (ticksToWait > Consts.TicksPerDay) {
                D.Err("Trying to wait for > 1 day, this is not recommended or a bug! This hook will be lost after save / load! {0}",
                    Units.TicksToTime(ticksToWait));
            }
            executeAfterTick.Add((action, Ticks + ticksToWait));
        }
        private int accumTPUTicks;
        private int accumTPUUpdates;
        private int accumFrameOnStart;
        private float accumTPUTimeLeft;
        private const float AccumTPUInterval = 1f;
        public bool IsTPUReady;
        public void ResetFPS() {
            GameTimer.ResetFPS();
            IsTPUReady = false;
            accumTPUTicks = 0;
            accumTPUUpdates = 0;
            accumFrameOnStart = GameTimer.frameCount;
            accumTPUTimeLeft = AccumTPUInterval;
        }
        /// <summary>
        /// On next tick progress - won't run if clock is stopped!
        /// </summary>
        private void DoOnNextUpdate() {
            if (onNextUpdate != null) {
                // Same as DoOnNextTick: cleared before invoking and one action per try, or a
                // throwing action stayed queued, threw again every update and left
                // isExecutingOnNextUpdate stuck, diverting every later OnNextUpdate to ActQ
                var actions = onNextUpdate;
                onNextUpdate = null;
                isExecutingOnNextUpdate = true;
                try {
                    var list = actions.GetInvocationList();
                    for (int i = 0; i < list.Length; i++) {
                        try {
                            ((Action) list[i]).Invoke();
                        } catch (Exception ex) {
                            D.LogEx(ex, "Failed executing OnNextUpdate action on tick {0}", Ticks);
                        }
                    }
                } finally {
                    isExecutingOnNextUpdate = false;
                }
            }
        }
        private void DoOnNextFrame() {
            for (int i = executeAfterFrame.Count - 1; i >= 0; i--) {
                if (i >= executeAfterFrame.Count) { continue; }
                (Action, int) delay = executeAfterFrame[i];
                if (delay.Item2 < GameTimer.frameCount) {
                    // removed even when it throws, or it would throw again every frame and
                    // stop the actions after it
                    try {
                        delay.Item1.Invoke();
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed executing delayed frame action on frame {0}",
                            GameTimer.frameCount);
                    }
                    if (i < executeAfterFrame.Count) {
                        executeAfterFrame.RemoveAt(i);
                    }
                }
            }
        }
        private void DoOnNextTick() {
            if (onNextTick != null) {
                // Cleared before invoking: a throwing action used to leave onNextTick set
                // and isExecutingOnNextTick stuck, so it threw again on every tick and all
                // later OnNextTick calls were diverted. Registrations made while this runs
                // already go to executeAfterTick, so clearing first changes nothing else.
                var actions = onNextTick;
                onNextTick = null;
                isExecutingOnNextTick = true;
                try {
                    // one action per try: a throwing action must not drop the ones queued
                    // after it for the same tick
                    var list = actions.GetInvocationList();
                    for (int i = 0; i < list.Length; i++) {
                        try {
                            ((Action) list[i]).Invoke();
                        } catch (Exception ex) {
                            D.LogEx(ex, "Failed executing OnNextTick action on tick {0}", Ticks);
                        }
                    }
                } finally {
                    isExecutingOnNextTick = false;
                }
            }
            for (int i = executeAfterTick.Count - 1; i >= 0; i--) {
                if (i >= executeAfterTick.Count) { continue; }
                (Action, long) delay = executeAfterTick[i];
                if (delay.Item2 < Ticks) {
                    // removed even when it throws, or it would throw again every tick
                    try {
                        delay.Item1.Invoke();
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed executing delayed tick action on tick {0}", Ticks);
                    }
                    if (i < executeAfterTick.Count) {
                        executeAfterTick.RemoveAt(i);
                    }
                }
            }
        }

        private double lastFUTime = 0;
        // Real-time token budget for the lag-skip in FixedUpdate. Real time entitles us to
        // FixedUpdatesPerSec sim updates per second; one token is spent per processed update.
        private float fuBudget;
        // Caps how much catch-up a single stall (frame hitch / load spike) can buffer so a
        // big gap can't trigger a long burst of FixedUpdates in one frame.
        private const float MaxFUBudget = 2.5f;
        /// <summary>
        /// X = <see cref="FixedUpdatesPerSec"/>
        /// We know that the game will do its best to invoke Tick X times per
        /// second in Unity's FixedUpdate(). If the PC cannot handle it it may
        /// be less than X, but we don't care.
        /// We have to decide how frequently we will produce a tick when this
        /// method is called.
        /// </summary>

        public void FixedUpdate(float dt) {
            var d = Time.realtimeSinceStartupAsDouble;
            #if DEBUG_CLOCK
            D.Warn("FixedUpdate: {0}, {1:0.000000}s", dt, (float) (d - lastFUTime));
            #endif
            // Lag skip via a real-time token budget. Unity invokes FixedUpdate ~FixedUPS times
            // per real second at every clock speed (timeScale/fixedDeltaTime == FixedUPS), but
            // it quantizes those calls to frame boundaries and runs several back-to-back when
            // catching up after a hitch. Comparing raw real-time gaps to a threshold can't tell
            // a legitimate next-frame update (1/FPS apart, < 1/FixedUPS when FPS is high) from a
            // catch-up burst, which is why earlier threshold logic skipped constantly. Instead,
            // accrue FixedUpdatesPerSec tokens per real second and spend one per processed
            // update: steady FPS never false-skips, and genuine bursts are throttled by the cap.
            if (Speed < ClockSpeed.Unlimited) {
                if (lastFUTime > 0d) {
                    fuBudget += (float) (d - lastFUTime) * FixedUpdatesPerSec;
                    if (fuBudget > MaxFUBudget) {
                        fuBudget = MaxFUBudget;
                    }
                } else {
                    fuBudget = 1f;
                }
                lastFUTime = d;
                if (fuBudget < 1f) {
                    RegisterLagSkip();
                    return;
                } else {
                    fuBudget -= 1f;
                }
            } else {
                lastFUTime = d;
            }
            if (restrictSecondsLeft > 0f) {
                restrictSecondsLeft -= dt;
                if (restrictSecondsLeft <= 0f) {
                    Unrestrict();
                }
            }
            if (Speed == ClockSpeed.Stopped || IsForceStopped) {
                wantsAfterTickUpdate = true;
                wantsDangerPause = false;
                // no tick is due while paused, so locks are not holding anything back
                tickBlockedSince = 0d;
                return;
            }
            if (Speed == ClockSpeed.Unlimited) {
                var startTime = Time.realtimeSinceStartupAsDouble;
                var budget = dt / 3f * 0.9d; // 10% of each fixed update left for rendering etc
                // Unlimited is only reachable in tests and in dev builds, and it exists to
                // burn through ticks, so danger must never pause it.
                wantsDangerPause = false;
                TicksPerUpdate = 1f;
                for (int i = 0; i < 500; i++) {
                    if (!TryForceTick()) {
                        #if DEBUG_UNLIMITED
                        D.Log("Breaking clock unlimited after {0} iterations due to TryForceTick: FPS: {1}",
                            i, GameTimer.FPS);
                        #endif
                        break;
                    }
                    if (Speed < ClockSpeed.Unlimited || IsForceStopped) {
                        #if DEBUG_UNLIMITED
                        D.Log("Breaking clock unlimited after {0} iterations due to IsForceStopped: FPS: {1}",
                            i, GameTimer.FPS);
                        #endif
                        break;
                    }
                    var took = Time.realtimeSinceStartupAsDouble - startTime;
                    if (took > budget) {
                        #if DEBUG_UNLIMITED
                        D.Log("Breaking clock unlimited after {0}, ticks (took: {1:0.00000s}). FPS: {2}. Frame: {3}. Budget: {4}",
                            i, took, GameTimer.FPS, Time.frameCount, budget);
                        #endif
                        break;
                    }
                }
            } else {
                // X invocations = 1 sec
                if (++fixedUpdatesSinceLastTick >= fixedUpdatesPerTick) {
                    IncrementTick();

                    if (wantsDangerPause) {
                        wantsDangerPause = false;
                        lastAutoPauseDangerTick = Ticks;
                        D.Warn("Will pause due to danger");
                        S.CmdQ.Enqueue(new CmdSetGameSpeed(MinSpeed));
                    }
                }
            }
        }

        private void RegisterLagSkip() {
            LagSkips++;
            if (LagSkips % 100 == 0) {
                D.Warn("Clock FixedUpdate skipped {0} times due to lag", LagSkips);
            #if DEBUG_CLOCK_SKIP
            } else {
                D.Warn("Lag clock skip! budget: {0:0.000}. Total skips: {1}", fuBudget, LagSkips);
            #endif
            }
        }

        public bool TryForceTick() {
            if (IncrementTick()) {
                // DoTickProgress(0f, false);
                S.ActQ.Execute(1f);
                return true;
            }
            return false;
        }

        private bool IncrementTick() {
            if (IsTickLocked) {
                ValidateLocks();
                return false;
            }
            tickBlockedSince = 0d;
            if (!hadTickProgressSinceLastTick) {
                // this means that FixedUpdate is running faster than Update!
                // we are dropping heavy frames, we must force TickProgress now!
                S.ActQ.Execute(0.1f);
                TicksPerUpdate = 1f;
                DoTickProgress(1f, true);
                UpdateTPU(forcedTick: true);
            }
            fixedUpdatesSinceLastTick = 0;
            accumTPUTicks++;
            Ticks++;
            framesOnLastTick = GameTimer.frameCount;
            // D.Warn("tick {0} | {1}", Ticks, Speed);
            G.Ticks = Ticks;
            D.Ticks = Ticks;
            ExecuteAfter();

            TickProgress = 0f;
            // D.Warn("Clock [{0}]: Tick: {1}. Time: {2:0.0000}s",
            //     Speed, Ticks, Time.realtimeSinceStartupAsDouble);
            // One listener that throws must not skip the rest of the tick. With a plain
            // Send it did, on every tick, while Ticks kept counting: the game looked
            // unpaused but nothing simulated (STA-1514, STA-1335). SendSafe isolates each
            // listener and throttles the log; TickFaultReporter tells the player once.
            OnTickEarly.SendSafeTimed(Ticks, TickTiming);
            InvokeEveryTickOrFrame(Ticks);
            OnTick.SendSafeTimed(Ticks, TickTiming);
            OnTickLate.SendSafeTimed(Ticks, TickTiming);
            try {
                TickB.Set(Ticks);
            } catch (Exception ex) {
                D.LogEx(ex, "TickB listener failed on tick {0}", Ticks);
            }
            hadTickProgressSinceLastTick = false;
            DoOnNextTick();

            // force the next Update() to do TickProgress immediately
            // timeSinceLastTickProgress = -1f;
            // framesSinceLastTickProgress = 0;
            return true;
        }

        private void InvokeEveryTickOrFrame(long ticks) {
            if (ticks > lastETOFTick || GameTimer.frameCount > lastETOFFrame) {
                lastETOFTick = ticks;
                lastETOFFrame = GameTimer.frameCount;
                // D.Warn("EveryTickOrFrame: T:{0},F:{1} @ {2}", ticks,
                //     GameTimer.frameCount, G.ClockSpeed);
                EveryTickOrFrame.SendSafe();
            }
        }

        private void ExecuteAfter() {
            if (afterCallbacks.Count == 0) { return; }
            // Execute After() callbacks
            // the catch is per callback, so one failure does not delay the others
            for (int i = afterCallbacks.Count - 1; i >= 0; i--) {
                if (i >= afterCallbacks.Count) { continue; }
                (long, Action) c = afterCallbacks[i];
                if (Ticks < c.Item1) { continue; }
                try {
                    c.Item2.Invoke();
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed executing after callback: {0}", c);
                }
                if (i < afterCallbacks.Count) {
                    afterCallbacks.RemoveAt(i);
                }
            }
        }

        #if DEBUG_CLOCK
        private double last60FPSUpdate;
        #endif

        public void Update(float dt) {
            UpdateTPU();
            if (wantsAfterTickUpdate || Speed == ClockSpeed.Stopped) {
                wantsAfterTickUpdate = false;
                #if DEBUG_CLOCK
                var d = Time.realtimeSinceStartupAsDouble;
                D.Warn("Last60: {0} {1:0.00000}s", Time.frameCount, (float) (d - last60FPSUpdate));
                last60FPSUpdate = d;
                #endif
                OnUpdate60.SendSafe();
            }
            InvokeEveryTickOrFrame(Ticks);
            DoOnNextFrame();
            if (Speed == ClockSpeed.Stopped) {
                Physics.Simulate(Time.fixedDeltaTime);
                return;
            }
            if (IsForceStopped) {
                Physics.Simulate(Time.fixedDeltaTime);
                return;
            }
            #if DEBUG_CLOCK
            D.Log("Update: {0:0.0000}", dt);
            #endif
            if (Speed < ClockSpeed.Unlimited) {
                // at unlimited speed we do 1 tick per update anyway and we
                // don't expect any more
                AdvanceTickProgress(dt);
            }
        }

        private void UpdateTPU(bool forcedTick = false) {
            if (Speed == ClockSpeed.Stopped || IsForceStopped) { return; }
            var dt = GameTimer.unscaledDeltaTime;
            accumTPUTimeLeft -= dt;
            if (accumTPUTimeLeft <= 0f) {
                // var before = TicksPerUpdate;
                if (accumTPUUpdates > 0)  {
                    TicksPerUpdate = accumTPUTicks / (float) accumTPUUpdates;
                    if (TicksPerUpdate > 1f) {
                        #if DEBUG_TICK_PROG
                        D.Warn("TicksPerUpdate > 1! Ticks: {0}, Updates: {1}, Speed: {2}",
                            accumTPUTicks, accumTPUUpdates, Speed);
                        #endif
                        TicksPerUpdate = 1f;
                    }
                    var framesSinceLastUpdate = GameTimer.frameCount - accumFrameOnStart;
                    #if !PRODUCTION_BUILD
                    // A forced tick (IncrementTick) is counted right after this call, so its
                    // window only looks empty: the load warmup forces one after a long frame
                    if (accumTPUTicks == 0 && G.ClockSpeed < ClockSpeed.Unlimited && !forcedTick) {
                        D.ErrSoft("accTPUTicks == 0!");
                    }
                    #endif
                    if (accumTPUTicks > 0) {
                        framesPerTick = framesSinceLastUpdate / (float) accumTPUTicks;
                    }
                    #if DEBUG_TICK_PROG
                    D.Warn("frames per tick = {0} / {1} = {2}",
                        framesSinceLastUpdate, accumTPUTicks, framesPerTick);
                    #endif

                    // D.Log("Updating TPU: {0}->{1}", before, TicksPerUpdate);
                } else {
                    TicksPerUpdate = 0;
                    framesPerTick = 0;
                }
                accumFrameOnStart = GameTimer.frameCount;
                accumTPUUpdates = 0;
                accumTPUTicks = 0;
                accumTPUTimeLeft = AccumTPUInterval;
                IsTPUReady = true;
            } else if (!IsTPUReady && accumTPUTicks > 0) {
                // get a mostly accurate value ASAP
                TicksPerUpdate = Mathf.MoveTowards(TicksPerUpdate,
                    accumTPUTicks / (float) accumTPUUpdates, TicksPerUpdate * 0.3f);
                if (TicksPerUpdate > 1f) {
                    #if !PRODUCTION_BUILD
                    D.Warn("TicksPerUpdate ESTIMATE > 1! Ticks: {0}, Updates: {1}, Speed: {2}",
                        accumTPUTicks, accumTPUUpdates, Speed);
                    #endif
                    TicksPerUpdate = 1f;
                }
            }
        }

        private void AdvanceTickProgress(float dt) {
            // TickProgress = timeSinceLastTick / TickDuration;
            // tpu = 0.001;
            // TickProgress = timeSinceLastTick / TicksPerUpdate;

            if (framesPerTick == 0f) {
                return;
            }

            var framesSinceLastTick = GameTimer.frameCount - framesOnLastTick;
            TickProgress = framesSinceLastTick / framesPerTick;

            #if DEBUG_TICK_PROG
            D.Log("TickProgress = {0} / {1} = {2}", framesSinceLastTick, framesPerTick, TickProgress);
            #endif

            // we need to decide how many frames to skip between tick progress
            // the rules will be as follows:
            // - we try 60 fps
            // - if frame rate > 60FPS, we will try dividing the frame rate by an int
            // until we get a value that is >= 60fps.
            // that (int - 1) is the number of frames we can skip
            // at 60 fps = we skip every 0 frames
            // at 90 fps = we skip every 0 frames
            // at 120 fps = we skip every 1 frame
            // at 144 fps = we skip every 1 frame
            // at 240 fps = we skip every 3 frames 240 / 60 = 4 -> 3

            var canSkipFrames = CanSkipFrames();
            // D.Warn("frame: {0} | since: {1}", GameTimer.FPS, framesSinceLastTickProgress);
            if (framesSinceLastTickProgress++ < canSkipFrames) {
                #if DEBUG_TICK_PROG
                D.Log("AdvanceTickProgress: {0}/{1} return",
                    framesSinceLastTickProgress - 1, canSkipFrames);
                #endif
                return;
            }

            if (TickProgress > 1f) {
                if (IsTickLocked) {
                    #if DEBUG_TICK_PROG
                    D.Log("AdvanceTickProgress: Tick Locked, return");
                    #endif
                    wantsAfterTickUpdate = true;
                    TicksPerUpdate = 0f;
                    #if !PRODUCTION_BUILD
                    D.Log("Tick locked: {0}, TickProgress > 1f, not updating",
                        D.Dump(locks));
                    // #else
                    // D.Warn("Tick locked: {0}, TickProgress > 1f, not updating",
                    //     string.Join(",", locks.Values));
                    #endif
                    return;
                } else {
                    #if DEBUG_TICK_PROG
                    D.Log("AdvanceTickProgress: Clamp TickProgress to 1");
                    #endif
                    TickProgress = 1f;
                }
            }

            // TicksPerUpdate = minDelayBetweenTickProgress / TickDuration;
            #if DEBUG_CLOCK
            D.Warn("Clock Update [{0}]: Tick: {1}. Progress: {2} Time: {3:0.0000}s",
                Speed, Ticks, TickProgress, Time.realtimeSinceStartupAsDouble);
            #endif
            #if DEBUG_TICK_PROG
            D.Log("AdvanceTickProgress: Invoke, {0}", TickProgress);
            #endif
            DoTickProgress(TickProgress, false);
        }

        private int CanSkipFrames() {
            var fps = GameTimer.FPS;
            if (fps < 105f) { return 0; }
            if (The.Prefs.GetBool(
                    Pref.GUncapAnimationFPS, PrefH.GUncapAnimationFPS, true)) {
                return 0;
            }
            if (fps < 150f) { return 1; }
            if (fps < 220f) { return 2; }
            if (fps < 280f) { return 3; }
            return (int) math.floor(fps / 60f) - 1;
        }

        private float ExpectedUpdatesPerSecond() {
            var fps = GameTimer.FPS;
            if (fps < 105f) {
                return fps;
            }
            if (The.Prefs.GetBool(
                    Pref.GUncapAnimationFPS, PrefH.GUncapAnimationFPS, true)) {
                return fps;
            }
            var frac = math.floor(fps / 60f);
            return fps / frac;
        }

        private void DoTickProgress(float prog, bool late) {
            #if DEBUG_CLOCK
            TickUpdates++;
            if (late) {
                D.ErrSoft("TPU LATE!: {0:0.00000}. DT: {1:0.00000}. Prog: (stale, will be forced to 1:) {2:0.00000}. Speed: {3}, Tick: {4}. Updates: {5}",
                    TicksPerUpdate, Time.deltaTime, TickProgress, Speed, Ticks, TickUpdates);
            } else {
                D.Log("TPU: {0:0.00000}. DT: {1:0.00000}. Prog: {2:0.00000}. Speed: {3}. Tick: {4}. Updates: {5}",
                    TicksPerUpdate, Time.deltaTime, prog, Speed, Ticks, TickUpdates);
            }
            #endif
            accumTPUUpdates++;
            framesSinceLastTickProgress = 0;
            #if DEBUG_TICK_PROG
            D.Warn("tick progress: {0} | {1} | {2} | {3}", Ticks, prog, TicksPerUpdate, Speed);
            #endif
            // isolated like the tick signals: a throwing listener skipped the others and the
            // rest of the tick progress (OnNextUpdate actions, the progress bookkeeping)
            OnTickProgress.SendSafeTimed(prog, TickTiming);
            DoOnNextUpdate();
            hadTickProgressSinceLastTick = true;
            wantsAfterTickUpdate = true;
        }
        public void DangerPause() {
            if (wantsDangerPause) { return; }
            if (Speed == MinSpeed) { return; }
            bool autoPause = S.Prefs.GetBool(Pref.AutoPauseWhenDanger,
                PrefH.AutoPauseWhenDanger, false);
            if (!autoPause) { return; }
            if (Ticks - lastAutoPauseDangerTick < Consts.TicksPer60Minutes) {
                return;
            }
            wantsDangerPause = true;
        }

        private void ValidateLocks() {
            #if DEBUG_CLOCK
            D.ErrSoft("Tick is locked by: {0}", string.Join(", ", locks.Values));
            #endif
            #if DEBUG_LOCKS
            if (Coroutines.IsRunningTests) {
                D.ErrSoft("Tick is locked by: {0}", string.Join(", ", locks.Values));
            }
            #endif

            //accumulatedTime = extendedTickRate;
            // Coroutines.IsImmediateCRTAvailable = false;

            var now = GameTimer.realtimeSinceStartupAsDouble;
            if (tickBlockedSince == 0d) {
                tickBlockedSince = now;
            }
            if (now - tickBlockedSince > 60d) {
                D.Err("Tick was locked for over 60 seconds! Clearing locks: {0}. ClockSpeed: {1}",
                    string.Join(", ", locks.Values), Speed);
                locks.Clear();
                IsTickLocked = false;
                tickBlockedSince = 0d;
            }
        }

        public bool TryLock(int key, string reason) {
            if (locks.TryGetValue(key, out var oldR)) {
                if (oldR != reason) {
                    D.Err("Lock already exists with Reason: {0}. Trying to lock with new reason: {1}",
                        oldR, reason);
                }
                return false;
            }
            #if DEBUG_LOCKS
            D.Warn("Locking tick: {0} {1}", key, reason);
            #endif

            locks.Add(key, reason);
            IsTickLocked = true;
            return true;
        }
        public void Unlock(int key) {
            #if DEBUG_LOCKS
            if (locks.TryGetValue(key, out var reason)) {
                if (locks.Remove(key)) {
                    D.Warn("Unlocking tick: {0} {1}", key, reason);
                } else {
                    D.Warn("Failed to remove lock key: {0} ({1})", key, reason);
                }
            } else {
                D.Err("Unlocking tick when it was not locked ! {0}", key);
            }
            #else
            locks.Remove(key);
            #endif
            IsTickLocked = locks.Count != 0;
        }


        public ClockSpeed Toggle() {
            if (Speed == MinSpeed) {
                SetSpeed(lastToggleSpeed);
                return lastToggleSpeed;
            } else {
                lastToggleSpeed = Speed;
                SetSpeed(MinSpeed);
                return MinSpeed;
            }
        }

        public void Destroy() {
            MinSpeed = ClockSpeed.Stopped;
            SetSpeed(ClockSpeed.Stopped);
            S = null;
            Cleanup();
        }

        public void Restrict(ClockSpeed speed, float seconds) {
            restrictSecondsLeft = seconds;
            MaxSpeed = speed;
            IsRestricted = true;
            if (Speed > MaxSpeed) {
                SetSpeed(MaxSpeed);
            }
            // Coroutines.IsImmediateCRTAvailable = false;
            OnRestrictionChange.Send(MaxSpeed);
        }
        public void Unrestrict() {
            MaxSpeed = ClockSpeed.Unlimited;
            // Coroutines.IsImmediateCRTAvailable = true;
            IsRestricted = false;
            OnRestrictionChange.Send(ClockSpeed.Unlimited);
        }

        public void Cleanup() {
            OnTickProgress.Release();
            OnTick.Release();
            OnTickEarly.Release();
            OnTickLate.Release();
            OnSpeedChange.Release();
            OnRestrictionChange.Release();
            onNextUpdate = null;
            onNextTick = null;
        }

        public DateTime ToDateTime(long tickDuration, long ticks = 0) {
            if (ticks == 0) { ticks = Ticks; }
            return DateTime.FromFileTimeUtc(ticks * tickDuration);
        }

        public void Initialize() {
            Ticks = 0;
            G.Ticks = 0;
            #if PRODUCTION_BUILD
            SetSpeed(ClockSpeed.Normal);
            #else
            SetSpeed(ClockSpeed.Stopped);
            #endif
            wantsAfterTickUpdate = true;
            SetTimeScale();
        }

        public string GetSpeedMultiplierForUI(ClockSpeed speed) {
            switch (speed) {
                case ClockSpeed.Normal: return "1";
                case ClockSpeed.Fast1: return (upsFast1 / (float) upsNormal)
                    .ToString("0.#");
                case ClockSpeed.Fast2: return (upsFast2 / (float) upsNormal)
                    .ToString("0.#");
            }
            return "0";
        }
        public void Pause() {
            SetSpeed(MinSpeed);
        }

        public void ForceStop(ForceStopReason reason) {
            #if DEBUG_FORCE_STOP
            D.Warn("Force stopping clock: {0}", reason);
            #endif
            StopReasons |= reason;
            bool wasForceStopped = IsForceStopped;
            IsForceStopped = StopReasons != ForceStopReason.None;
            SetTimeScale();
            if (!wasForceStopped && Speed > ClockSpeed.Stopped && IsForceStopped) {
                if ((reason & ForceStopReason.SkipSound) == ForceStopReason.None) {
                    UISounds.PlayClockChange(ClockSpeed.Stopped);
                }
            }
            if (wasForceStopped != IsForceStopped) {
                OnForceStopChange.Send(IsForceStopped);
            }
        }

        public void ForceResume(ForceStopReason reason) {
            #if DEBUG_FORCE_STOP
            D.Warn("Force resuming clock: {0}", reason);
            #endif
            StopReasons &= ~reason;
            bool wasForceStopped = IsForceStopped;

            IsForceStopped = StopReasons != ForceStopReason.None;
            SetTimeScale();
            if (wasForceStopped && Speed > ClockSpeed.Stopped && !IsForceStopped) {
                if ((reason & ForceStopReason.SkipSound) == ForceStopReason.None) {
                    UISounds.PlayClockChange(Speed);
                }
            }
            if (wasForceStopped != IsForceStopped) {
                OnForceStopChange.Send(IsForceStopped);
            }
        }
        public ClockSpeed SetSpeed(ClockSpeed newSpeed) {
            if (IsRestricted && newSpeed > MaxSpeed) {
                newSpeed = MaxSpeed;
            }
            if (newSpeed == Speed) { return newSpeed; }
            var prevSpeed = Speed;

            if (newSpeed < MinSpeed) {
                newSpeed = MinSpeed;
            }

            #if DEBUG_CLOCK_SET_SPEED
            D.Warn("Clock: {0}=>{1}", Speed, newSpeed);
            #endif
            Speed = newSpeed;
            if (newSpeed == ClockSpeed.Stopped) {
                tickBlockedSince = 0d;
            }
            fixedUpdatesPerTick = GetFixedUpdatesPerTickFor(newSpeed);
            TickDuration = Time.fixedDeltaTime * fixedUpdatesPerTick;
            SetTimeScale();

            #if DEBUG_CLOCK
            D.Warn("Clock: {0}. Fixed Updates per tick: {1}. Tick Duration: {2:0.0000}s",
                Speed, fixedUpdatesPerTick, TickDuration);
            #endif

            OnSpeedChange.Send(newSpeed);
            UpdateGlobals(newSpeed);

            return prevSpeed;
        }

        private static void UpdateGlobals(ClockSpeed newSpeed) {
            G.ClockSpeed = newSpeed;
            switch (newSpeed) {
                case ClockSpeed.Stopped: {
                        G.AnimSpeedMul = 0f;
                        break;
                    }
                case ClockSpeed.Normal: {
                        G.AnimSpeedMul = 0.33f;
                        break;
                    }
                case ClockSpeed.Fast1: {
                        G.AnimSpeedMul = 0.5f;
                        break;
                    }
                case ClockSpeed.Fast2: {
                        G.AnimSpeedMul = 1f;
                        break;
                    }
                case ClockSpeed.Unlimited: {
                        G.AnimSpeedMul = 0.01f;
                        break;
                    }
                default: {
                        D.Err("Unhandled AnimSpeedMul for clock speed: {0}", newSpeed);
                        G.AnimSpeedMul = 0.33f;
                        break;
                    }
            }
            D.ClockSpeed = (int)newSpeed;
        }

        private void SetTimeScale() {
            ClockSpeed spd = Speed;
            if (IsForceStopped) {
                spd = ClockSpeed.Stopped;
            }
            switch (spd) {
                case ClockSpeed.Stopped: {
                    TimeScale = 0f;
                    FXTimeScale = 0f;
                    TicksPerUpdate = 0f;
                    break;
                }
                case ClockSpeed.Normal: {
                    TimeScale = 1f;
                    FXTimeScale = 1f;
                    TicksPerUpdate = ResetTPU();
                    break;
                }
                case ClockSpeed.Unlimited: {
                    TimeScale = FixedUpdatesPerSec;
                    FXTimeScale = 2f;
                    TicksPerUpdate = 1f;
                    break;
                }
                default: {
                    var fixedUpdatesPerTick = GetFixedUpdatesPerTickFor(ClockSpeed.Normal);
                    var normTickDur = fixedUpdatesPerTick * (1f / FixedUpdatesPerSec);
                    TimeScale = normTickDur / TickDuration;
                    // Make it smoother
                    if (TimeScale > 1) {
                        TimeScale = 1f + ((TimeScale - 1f) * 0.25f);
                    }
                    FXTimeScale = Maths.Min(2f, TimeScale);
                    TicksPerUpdate = ResetTPU();
                    #if DEBUG_CLOCK
                    D.Warn("Tick per update: {0}, Tick dur: {1}. FXTS: {2}",
                        TicksPerUpdate, normTickDur, FXTimeScale);
                    #endif
                    break;
                }
            }
            Time.timeScale = FXTimeScale;
            if (FXTimeScale > 0.001f) {
                Time.fixedDeltaTime = 1f / FixedUpdatesPerSec  * FXTimeScale;
                Physics.simulationMode = SimulationMode.FixedUpdate;
            } else {
                Time.fixedDeltaTime = 1f / FixedUpdatesPerSec;
                Physics.simulationMode = SimulationMode.Script;
            }
            #if DEBUG_CLOCK
            D.Warn("Setting FX time scale: {0}: FDT: {1}",
                FXTimeScale, Time.fixedDeltaTime);
            #endif
        }

        private float ResetTPU() {
            // D.Warn("Resetting tpu");
            // we have ticks per second
            // we update no more than 60 times per second, but if FPS is lower
            // then less
            var ticksPerSecond = ExpectedTicksPerSecond();
            // optimistic estimate
            var expUpdPerSec = ExpectedUpdatesPerSecond();
            var tpu = ticksPerSecond / expUpdPerSec;
            accumTPUTimeLeft = AccumTPUInterval;
            accumTPUTicks = 0;
            accumTPUUpdates = 0;
            if (ticksPerSecond < 1) {
                framesPerTick = 60;
            } else {
                framesPerTick = GameTimer.FPS / ticksPerSecond;
            }
            framesOnLastTick = GameTimer.frameCount;
            // D.Log("Resetting TPU to: {0}. Speed: {1}. TPS: {2}. UPS: {3}, FPS: {4}",
            //     tpu, Speed, ticksPerSecond, expUpdPerSec, GameTimer.FPS);
            return tpu;
        }

        public void SetMinSpeed(ClockSpeed speed) {
            MinSpeed = speed;
        }

        public void Load(long ticks) {
            Ticks = ticks;
            TickB.SetSilent(ticks);
            G.Ticks = ticks;
            tickBlockedSince = 0d;
            framesOnLastTick = GameTimer.frameCount;
        }
        public Clock(GameState s) {
            S = s;
            upsNormal = GetBaseUPS(TunableKey.ClockSpeedNormal_UPS, "ClockSpeedNormal_UPS");
            upsFast1 = GetBaseUPS(TunableKey.ClockSpeedFast1_UPS, "ClockSpeedFast1_UPS");
            upsFast2 = GetBaseUPS(TunableKey.ClockSpeedFast2_UPS, "ClockSpeedFast2_UPS");
            G.Ticks = 0;
            framesOnLastTick = GameTimer.frameCount;
            accumTPUTimeLeft = AccumTPUInterval;
            Speed = ClockSpeed.Normal;
            SetSpeed(ClockSpeed.Stopped);
            // kept alive by its subscriptions on the tick signals
            new TickFaultReporter(this);
        }

        private float ExpectedTicksPerSecond() {
            switch (Speed) {
                case ClockSpeed.Stopped: return 0;
                case ClockSpeed.Normal: return upsNormal;
                case ClockSpeed.Fast1: return upsFast1;
                case ClockSpeed.Fast2: return upsFast2;
                case ClockSpeed.Unlimited: return GameTimer.FPS;
                default: {
                    D.Err("ExpectedTPU unhandled for speed: {0}", Speed);
                    return 0;
                }
            }
        }

        private int GetFixedUpdatesPerTickFor(ClockSpeed speed) {
            switch (speed) {
                case ClockSpeed.Normal: return (int) (FixedUpdatesPerSec / upsNormal);
                case ClockSpeed.Fast1: return (int) (FixedUpdatesPerSec/ upsFast1);
                case ClockSpeed.Fast2: return (int) (FixedUpdatesPerSec / upsFast2);
                case ClockSpeed.Unlimited: return 1;
                default: return 0;
            }
        }

        private float GetBaseUPS(int tunableKey, string ctx) {
            var ups = Tunable.Float(tunableKey);
            if (ups > FixedUpdatesPerSec) {
                D.Err("Max UPS for clock speed is {2}. A tunable {0} attempted to set {1}. Setting to {2}.",
                    ctx, ups, FixedUpdatesPerSec);
                ups = FixedUpdatesPerSec;
            } else if (ups < 1) {
                D.Err("Min UPS for clock speed is 1. A tunable {0} attempted to set {1}. Setting to 1.",
                    ctx, ups);
                ups = 1;
            }
            if (!Maths.Approximately(FixedUpdatesPerSec / ups, (int) (FixedUpdatesPerSec / ups))) {
                D.Err("{3} must be dividable by UPS! {0} attempts to set {1}, the actual value will be {2}",
                    ctx, ups, (int) (FixedUpdatesPerSec / ups), FixedUpdatesPerSec);
                // ups = (int) (FixedUpdatesPerSec / ups);
            }
            return ups;
        }

        public void After(int ticks, Action callback) {
            if (ticks < 1) {
                D.Err("Trying to add After ticks callback with ticks < 1! {0}",
                    callback);
                callback();
                return;
            }
            // ExecuteAfter compares against the absolute tick
            afterCallbacks.Add((Ticks + ticks, callback));
        }

        public void AfterLoad() {
            ResetFPS();
            TicksPerUpdate = ResetTPU();
            if (Speed == ClockSpeed.Stopped) {
                wantsAfterTickUpdate = true;
                SetTimeScale();
            }
        }

        public void BeforeSave() {
            DoOnNextUpdate();
            DoOnNextTick();
            if (executeAfterTick.Count > 0) {
                foreach (var ex in executeAfterTick) {
                    D.Warn("Execute after tick will not be invoked! {0} -> {1}",
                        Units.TicksToTime(ex.Item2 - G.Ticks), ex.Item1);
                }
            }
        }
    }
}
