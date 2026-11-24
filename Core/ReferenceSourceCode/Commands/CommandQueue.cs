#if !PRODUCTION_BUILD
// #define TRACK_STACK_TRACE
// #define INSTANT_EXEC
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using KL.Utils;

namespace Game.Commands {
    public class CommandQueue {
        public GameState S;
        private readonly Queue<Command> queue;
        private KL.Clock.Clock clock;
        public int ExecutionCount;
        private bool HasPendingCommands => queue.Count > 0;
        public int Size => queue.Count;
        public Command[] DumpPendingCommands => queue.ToArray();
        private readonly HashSet<int> CurrentUniqueCommands = new();
        public void SetClock(KL.Clock.Clock clock) {
            this.clock = clock;
        }
        public CommandQueue() {
            queue = new Queue<Command>();
        }

        private HashSet<int> forbiddenCommands;
        public void Forbid(string commandId) {
            forbiddenCommands ??= new HashSet<int>();
            forbiddenCommands.Add(Hashes.S(commandId));
        }

        public void Allow(string commandId) {
            forbiddenCommands.Remove(Hashes.S(commandId));
        }

        public void AllowAll() {
            forbiddenCommands = null;
        }

        public void Enqueue(Command command) {
            if (command.IsSystem && command.IsUnique) {
                D.Err("Having Unique System commands is not reliable, use something else!: {0}",
                    command.Id);
            }
            if (forbiddenCommands != null && forbiddenCommands.Contains(command.IdH)) {
                command.OnForbidden(S);
                return;
            }
            if (!Ready.Gen || G.ClockSpeed > ClockSpeed.Fast2) {
                if (command.IsSystem) {
                    D.Log("Insta-executing command: {0} (system: {1})",
                        command.Id, command.IsSystem);
                }
                // run commands as fast as possible during gen
                if (command is AsyncCommand asc) {
                    if (asc.TrySameFrame) {
                        asc.WithState(S);
                        ExecuteCoroutine.Execute(asc.ExecuteAsync(), asc.Id, 100_000);
                        return;
                    }
                } else {
                    command.Execute(S);
                    return;
                }
            }

            #if INSTANT_EXEC
            if (command is not AsyncCommand asc2) {
                command.Execute(S);
            } else {
                if (asc2.TrySameFrame) {
                    asc2.WithState(S);
                    ExecuteCoroutine.Execute(asc2.ExecuteAsync(),
                        $"Instant Async Cmd: {asc2.Id}");
                    return;
                }
            }
            #endif
            #if TRACK_STACK_TRACE
            command.Trace = System.Environment.StackTrace;
            #endif
            if (command.IsUnique) {
                if (!CurrentUniqueCommands.Add(command.IdH)) {
                    D.Err("Rejecting unique command duplicate: {0}",
                        command.Id);
                    return;
                }
            }
            D.Ass(command != null, "Command was null!");
            if (clock != null) {
                command.EnqueueTick = clock.Ticks;
            }
            queue.Enqueue(command);
        }
        public void Clear() {
            queue.Clear();
            CurrentUniqueCommands.Clear();
        }

        // the command Execute started last, also when it threw
        public Command LastStarted { get; private set; }

        public Command Execute() {
            if (HasPendingCommands) {
                var cmd = queue.Dequeue();
                LastStarted = cmd;
                // finally: a command that threw kept its unique id, so every later unique
                // command with that id (spawn_derelict, tilemap_refresh, section moves) was
                // rejected as a duplicate for the rest of the game
                try {
                    if (GameState.IsCurrent(S)) {
                        cmd.Execute(S);
                    }
                } finally {
                    ExecutionCount++;
                    if (cmd.IsUnique) {
                        CurrentUniqueCommands.Remove(cmd.IdH);
                    }
                }
                return cmd;
            }
            return null;
        }

        public void EnsureEmptyAfterLoad() {
            if (Size == 0) {
                Clear();
                return;
            }

            if (Size == 1 && queue.Peek().IsSystem) {
                CurrentUniqueCommands.Clear();
                return;
            }

            D.Err("Having pending commands in the queue in after load! {0}",
                D.DumpCollection(DumpPendingCommands));
            Clear();
        }
    }
}
