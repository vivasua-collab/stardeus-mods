using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Factions;
using Game.Input;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using UnityEngine.Pool;

namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipChatResponseDef {
        public static Dictionary<(ShipChatTopic, ShipChatResponseType, ShipChatTone), ShipChatResponseDef> All = new();
        private static readonly Dictionary<ShipChatResponseDef, float> weighed = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
            weighed.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public string ResponseType;
        public ShipChatResponseType Response;
        public string TopicType;
        public ShipChatTopic Topic;
        public string ToneType;
        public ShipChatTone Tone;
        public string[] MessageKeys;
        public string[] MessageTs;
        public string Id;
        public int IdH;
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipChatResponseDef>("Config/Ships/ChatResponses");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (Arrays.IsEmpty(type.MessageKeys)) {
                    D.Err("MessageKeys[] not defined for ShipChatResponseDef: {0}", type.Id);
                }
                if (!Enums.TryFromString(type.ToneType, out type.Tone)) {
                    D.Err("Unknown tone type for ShipChatResponseDef: {0} -> {1}", type.ToneType, type.Id);
                }
                if (!Enums.TryFromString(type.TopicType, out type.Topic)) {
                    D.Err("Unknown topic type for ShipChatResponseDef: {0} -> {1}", type.TopicType, type.Id);
                }
                if (!Enums.TryFromString(type.ResponseType, out type.Response)) {
                    D.Err("Unknown response type for ShipChatResponseDef: {0} -> {1}", type.ResponseType, type.Id);
                }
                var key = (type.Topic, type.Response, type.Tone);
                if (All.TryGetValue(key, out var existing)) {
                    D.Err("ShipChatResponseDef with response/topic/type combo already exists: {0}->{1}. Duplicate: {2}",
                        key,
                        existing.Id,
                        type.Id);

                }
                if (!All.TryAdd(key, type)) {
                    continue;
                }
            }
            Ready.When(WhenH.CoreLoaded2, ValidateAll);
        }

        public static void ValidateAll() {
            // D.Warn("Validating ship chat: {0}", All.Count);
            foreach (var def in All.Values) {
                def.Validate();
            }
        }

        public static string GetMessage(NPCShip ship, ShipChatTopic topic,
                ShipChatResponseType type) {
            weighed.Clear();
            // each ship will respond with same message, to build a character
            var rng = new Rng(Hashes.Quick((int) ship.Id, Primes.Prime060));
            var tone = ShipChatToneAssignmentDef.GetToneFor(ship, rng);
            var key = (topic, type, tone);
            if (All.TryGetValue(key, out var def)) {
                var msg = def.MessageFor(rng);
                // D.Warn("{0}|{1}=>{2} | {3}", topic, type, msg, def.Id);
                switch (type) {
                    case ShipChatResponseType.Positive: {
                        if (tone == ShipChatTone.Rude) {
                            msg = $"{GlyphMap.SpriteFaceNormal} {msg}";
                        } else {
                            msg = $"{GlyphMap.SpriteFaceHappy} {msg}";
                        }
                        break;
                    }
                    case ShipChatResponseType.Outlaw:
                    case ShipChatResponseType.Negative: {
                        msg = $"{GlyphMap.SpriteFaceSad} {msg}";
                        break;
                    }
                    case ShipChatResponseType.End:
                    case ShipChatResponseType.Neutral:
                    case ShipChatResponseType.Busy: {
                        msg = $"{GlyphMap.SpriteFaceNormal} {msg}";
                        break;
                    }
                    default: {
                        D.Err("Unknown response type: {0}", type);
                        break;
                    }
                }
                return msg;
            } else {
                D.Err("Couldn't find ShipChatResponseDef for topic: {0}, type: {1}, tone: {2}. Ship: {3}",
                    topic, type, tone, ship);
                return "...";
            }
        }

        public void Validate() {
            int withTr = 0;
            var toneStr = Tone.ToString().ToLowerInvariant();
            toneStr = $".{toneStr}.";
            var typeStr = Response.ToString().ToLowerInvariant();
            typeStr = $".{typeStr}.";
            for (int i = MessageKeys.Length - 1; i >= 0; i--) {
                string key = MessageKeys[i];
                if (key.HasT()) {
                    withTr++;
                } else if (key.Contains("*")) {
                    // if we have a * in here, we expect the translation
                    if (!key.Contains(toneStr)) {
                        D.Err("{0} key expected to have {1}, but doesn't: {2}", Id, toneStr, key);
                    }
                    if (!key.Contains(typeStr)) {
                        if (Response == ShipChatResponseType.Negative && key.Contains(".busy.")) {
                            // it's OK to use Busy response for Negative
                        } else {
                            if (Topic != ShipChatTopic.QuestDeliveryResponse && !key.Contains(".neutral.")) {
                                D.Err("{0} key expected to have {1}, but doesn't: {2}", Id, typeStr, key);
                            }
                        }
                    }
                    var matched = 0;
                    foreach (var mk in Translations.MainKeys) {
                        if (Strings.Matches(mk, key)) {
                            matched++;
                            withTr++;
                        }
                    }
                }
            }
            // if (withTr > 0) {
            //     D.Warn("Keys {0}: {1}", Id, withTr);
            // }
        }

        public string MessageFor(Rng rng) {
            if (MessageTs != null) {
                return Rng.Unseeded.From(MessageTs);
            }
            var temp = ListPool<string>.Get();
            for (int i = MessageKeys.Length - 1; i >= 0; i--) {
                string key = MessageKeys[i];
                if (key.HasT()) {
                    temp.Add(key.T());
                } else if (key.Contains("*")) {
                    var matched = 0;
                    foreach (var mk in Translations.MainKeys) {
                        if (Strings.Matches(mk, key)) {
                            matched++;
                            temp.Add(mk.T());
                        }
                    }
                    // if (matched > 0) {
                    //     D.Warn("Keys matched {0}: {1}", key, matched);
                    // }
                }
            }
            MessageTs = temp.ToArray();

            if (temp.Count == 0) {
                D.Err("No messages found for ShipChatResponseDef: {0}", Id);
            }

            ListPool<string>.Release(temp);
            return rng.From(MessageTs);
        }

        public override string ToString() {
            return $"ShipChatResponse[{Id}|{TopicType}|{ResponseType}]";
        }
    }
}
