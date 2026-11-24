using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using System;
using KL.Collections;
using System.Reflection;
using System.Text;
using Game.Data.Economy;
using KL.Randomness;
using System.Linq;
using System.Collections;
using Game.UI;
using Game.Utils;
using Game.Data.Factions;
using UnityEngine.SceneManagement;

namespace Game.Console {
    public class ConsoleCommandGameObject : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandGameObject());
        }

        protected override Dictionary<string, string> subCommands => new() {
            { "list", "ListObjects" },
            { "find", "FindObject" },
        };

        protected override HashSet<string> commandsThatDontBlockAchievements =>
            new HashSet<string>() {
                "list",
                "find",
            };

        protected ConsoleCommandResult FindObject(ConsoleCommandArguments args) {
            string query = null;
            if (args.HasArgument(2)) { query = args.GetString(2); }
            if (query == null) {
                return Error("Usage: game_object find <query>");
            }
            var sb = new StringBuilder();
            var go = GameObject.Find(query);
            if (go == null) {
                 Type type = Type.GetType(query);
                if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type)) {
                    var found = (MonoBehaviour) GameObject.FindFirstObjectByType(type);
                    if (found != null) {
                        go = found.gameObject;
                    }
                }
            }

            if (go == null) {
                return Error("Object not found by name or class: {0}", query);
            }

            sb.AppendFormat("Found: [{0}] {1}\n", go.activeSelf, go.name);
            foreach (var comp in go.GetComponents<MonoBehaviour>()) {
                sb.AppendFormat(" - [{0}] {1}\n", comp.enabled ? '+' : '-', comp);
            }

            return OK(sb.ToString());
        }

        private void DumpHierarchy(Transform parent, StringBuilder sb, int depth) {
            sb.AppendFormat(" {0} [{1}] {2}\n", new string('-', depth + 1), parent.gameObject.activeSelf ? '+' : '-', parent.name);
            for (int i = 0; i < parent.childCount; i++) {
                var child = parent.GetChild(i);
                DumpHierarchy(child, sb, depth + 1);
            }
        }

        protected ConsoleCommandResult ListObjects(ConsoleCommandArguments args) {
            string query = null;
            var sb = new StringBuilder();
            if (args.HasArgument(2)) { query = args.GetString(2); }
            if (query == null) {
                var scene = SceneManager.GetActiveScene();
                sb.AppendFormat("GameObjects in scene {0}:\n", scene.name);
                foreach (var go in scene.GetRootGameObjects()) {
                    DumpHierarchy(go.transform, sb, 0);
                }
            }
            return OK(sb.ToString());
        }

        public override void Initialize() {
            Name = "game_object";
            HelpLine = "Commands to manipulate and debug Unity GameObjects";
        }
    }
}
