using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.CodeGen;
using Game.Data;
using Game.Modifiers;
using Game.Systems.Mental;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class CustomBeingNameSource : NameSource {
        public const string SourceId = "CustomBeing";
        public const BeingType supportedBeingTypes =
            BeingType.StasisWakeup |
            BeingType.Friendly |
            BeingType.Clone |
            BeingType.Newborn;

        [SelfInit]
        private static void Register() {
            Register(SourceId, new CustomBeingNameSource());
        }

        public override int PriorityFor(NameType type) {
            // above name in game and basic built-in stuff
            return 1000;
        }

        public override bool Supports(NameRequest req) {
            return req.Type == NameType.Being
                && (!req.Species.IsBiological || req.Species.IsHumanoid)
                && (req.BeingType & supportedBeingTypes) != BeingType.None;
        }

        public override float ChanceFor(NameRequest request) {
            return 0.2f;
        }

        private void ConsumeName(NameResult res) {
            if (res.InternalResult is string entry) {
                Consume(entry);
            }
        }

        private readonly List<string> tempList = new();

        public override bool TryNameFor(NameRequest req, out NameResult res) {
            var file = Persist.PersistentPath(ModifierCustomNames.CustomNamesFile);
            if (!File.Exists(file)) {
                IsDisabled = true;
                res = null;
                D.Warn("CustomNames.txt does not exist, disabling name source");
                return false;
            }

            var readRes = Persist.ReadBytes(file, out var bytes);
            if (!readRes.IsSuccess) {
                IsDisabled = true;
                res = null;
                D.Err("CustomNames.txt is unreadable, disabling name source");
                return false;
            }

            var allNames = Encoding.UTF8.GetString(bytes);
            var lines = allNames.Split('\n');

            tempList.Clear();
            for (int i = lines.Length - 1; i >= 0; i--) {
                string l = lines[i];
                l = l.Trim();
                if (string.IsNullOrWhiteSpace(l)) { continue; }
                if (l.StartsWith("//")) { continue; }
                if (IsConsumed(l)) { continue; }
                tempList.Add(l);
            }

            if (tempList.Count == 0) {
                IsDisabled = true;
                D.Warn("CustomNames.txt is exhausted, disabling name source");
                res = null;
                return false;
            }

            var name = req.Rng.From(tempList);
            tempList.Clear();

            res = new NameResult(ConsumeName);
            var bn = new BeingName();
            bn.ParseName(name);
            D.Warn("Got custom name: {0} -> {1}", name, bn);
            res.SetBeingName(bn, name);
            return true;
        }

    }
}
