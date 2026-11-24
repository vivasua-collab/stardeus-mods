using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using MessagePack;
using System;
using Game.Systems.Naming;
using Game.Systems.UIUX;

namespace Game.Systems.Mental {
    [MessagePackObject]
    public sealed class BeingName {
        [IgnoreMember] public string DisplayName;
        [Key("FullName")] public string FullName;
        [Key("FirstName")] public string FirstName;
        [Key("LastName")] public string LastName;
        [Key("Nickname")] public string Nickname;
        /// <summary>
        /// Need this to be able to consume the name once it is assigned for good
        /// </summary>
        [IgnoreMember] public NameResult NameResult;
        private BCallback nameChangedB;
        [IgnoreMember] public BCallback NameChangedB => nameChangedB ??= new BCallback();
        private string shortName;
        public string FullNameOr(string fallback) {
            var fn = FullName;
            if (string.IsNullOrWhiteSpace(fn)) {
                fn = fallback;
            }
            return fn;
        }
        [IgnoreMember] public string FullNameOrNameless {
            get {
                var fn = FullName;
                if (string.IsNullOrWhiteSpace(fn)) {
                    fn = T.Nameless;
                }
                return fn;
            }
        }
        public string DisplayNameOr(string fallback) {
            var fn = DisplayName;
            if (string.IsNullOrWhiteSpace(fn)) {
                fn = fallback;
            }
            return fn;
        }


        [IgnoreMember] public string ShortName {
            get {
                if (shortName != null) { return shortName; }
                shortName = ShortNames.Pick(Nickname, FirstName, LastName)
                    ?? T.Nameless;
                return shortName;
            }
        }
        public void UpdateName() {
            shortName = null;
            if (string.IsNullOrWhiteSpace(Nickname)) {
                DisplayName = $"{FirstName} {LastName}".Trim();
                FullName = DisplayName;
            } else {
                DisplayName = Nickname;
                if (!string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName)) {
                    DisplayName = Nickname;
                    FullName = $"{FirstName} \"{Nickname}\" {LastName}";
                } else {
                    DisplayName = Nickname;
                    FullName = $"{FirstName} {Nickname} {LastName}".Trim();
                }
            }
            nameChangedB?.Invoke();
        }
        public override string ToString() {
            return $"BeingName[{FirstName},{Nickname},{LastName}]";
        }

        public static BeingName Copy(BeingName name) {
            var n = new BeingName();
            n.FirstName = name.FirstName;
            n.LastName = name.LastName;
            n.FullName = name.FullName;
            n.Nickname = name.Nickname;
            n.shortName = name.shortName;
            n.UpdateName();
            return n;
        }

        public static BeingName BlankFor(Species species) {
            var n = new BeingName();
            n.Nickname = species?.Name ?? T.Nameless;
            n.UpdateName();
            return n;
        }

        public static string Serialize(BeingName name) {
            return $"{name.FirstName ?? string.Empty}\n{name.LastName ?? string.Empty}\n{name.Nickname ?? string.Empty}";
        }

        public static BeingName Deserialize(string str) {
            var name = str.Split('\n');
            var n = new BeingName();
            if (!string.IsNullOrEmpty(name[0])) {
                n.FirstName = name[0];
            }
            if (!string.IsNullOrEmpty(name[1])) {
                n.LastName = name[1];
            }
            if (!string.IsNullOrEmpty(name[2])) {
                n.Nickname = name[2];
            }
            n.UpdateName();
            return n;
        }

        public void Consume(BeingData data) {
            if (NameResult != null) {
                NameResult.Consume(data);
                NameResult = null;
            }
        }

        public void Reset() {
            FirstName = null;
            LastName = null;
            Nickname = null;
            shortName = null;
            FullName = null;
            NameResult = null;
        }
        public void ParseName(string name) {
            Reset();
            if (name.Contains(' ')) {
                var nameParts = name.Split(' ');
                // first word always first name
                FirstName = nameParts[0];
                // last word always last name
                LastName = nameParts[^1];
                // nickname is anything in between
                if (nameParts.Length > 2) {
                    Nickname = string.Join(' ', nameParts, 1, nameParts.Length - 2).Trim();
                    int attempts = 0;
                    while (Nickname.Contains("  ") && attempts++ < 50) {
                        Nickname = Nickname.Replace("  ", " ");
                    }
                }
            } else {
                Nickname = name;
            }
        }
    }
}
