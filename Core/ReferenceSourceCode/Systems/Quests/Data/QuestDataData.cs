using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Utils;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Quests {
    [MessagePackObject]
    public sealed class QuestDataData {
        [Key("Id")] public uint Id;
        [Key("DefId")] public string DefId;
        [Key("Title")] public string Title;
        [Key("Desc")] public string Desc;
        [Key("TextVariantIndex")] public int TextVariantIndex = -1;
        [Key("Steps")] public QuestStepData[] Steps;
        [Key("ProviderId")] public uint ProviderId;
        [Key("ProviderType")] public QuestProviderType ProviderType;
        [Key("State")] public QuestState State;
        [Key("AvailableTick")] public long AvailableTick;
        [Key("StartTick")] public long StartTick;
        [Key("EndTick")] public long EndTick;
        [Key("Vars")] public QuestVarData[] Variables;
        [Key("Rewards")] public QuestRewardData[] Rewards;
        [Key("Data")] public ComponentData Data;
        [Key("Bools")] public QuestBools Bools;
        [Key("ProviderExtraBeingIds")] public uint[] ProviderExtraBeingIds;

        public static QuestDataData[] Serialize(List<Quest> quests) {
            if (Arrays.IsEmpty(quests)) {
                return null;
            }
            var qd = new QuestDataData[quests.Count];
            for (int i = quests.Count - 1; i >= 0; i--) {
                Quest q = quests[i];
                var d = Serialize(q.Data);
                qd[i] = d;
            }
            return qd;
        }

        private static QuestDataData Serialize(QuestData data) {
            var d = new QuestDataData();
            d.Id = data.Id;
            d.DefId = data.Def.Id;
            d.TextVariantIndex = data.TextVariantIndex;
            // Text that comes from the def is left out, so loading resolves it in the language
            // the game runs in then. Only text a story event formatted itself is saved.
            d.Title = data.Title == data.Def.GetTitleT(data.TextVariantIndex) ? null : data.Title;
            d.Desc = data.Desc == data.Def.GetDescT(data.TextVariantIndex) ? null : data.Desc;
            if (data.Provider != null) {
                d.ProviderId = data.Provider.ProviderId;
                d.ProviderType =  data.Provider.ProviderType;
                if (data.Provider is ColonistQuestProvider cp && cp.ExtraBeingIds != null) {
                    d.ProviderExtraBeingIds = cp.ExtraBeingIds;
                }
            }
            d.State = data.State;
            d.StartTick = data.StartTick;
            d.EndTick = data.EndTick;
            d.AvailableTick = data.AvailableTick;
            d.Data = data.Data;
            d.Bools = data.Bools;
            d.Variables = QuestVarData.Serialize(data.Variables);
            d.Rewards = QuestRewardData.Serialize(data.Rewards);
            d.Steps = QuestStepData.Serialize(data.Steps);
            return d;
        }

        public static bool TryDeserialize(GameState s, QuestDataData data, out QuestData q) {
            q = new QuestData();
            q.Id = data.Id;
            q.State = data.State;
            q.TextVariantIndex = data.TextVariantIndex;
            q.StartTick = data.StartTick;
            q.EndTick = data.EndTick;
            q.AvailableTick = data.AvailableTick;
            q.Def = QuestDef.Get(data.DefId);
            q.Data = data.Data;
            q.Bools = data.Bools;
            if (q.Def == null) {
                D.Err("QuestData doesn't have a def by id: {0}: {1}",
                    data.DefId, data);
                return false;
            }
            if (Arrays.IsEmpty(q.Def.TitleKeys)) {
                q.TextVariantIndex = -1;
            }
            q.Title = LoadText(data.Title, q.Def.GetTitleKey(q.TextVariantIndex),
                q.Def.GetTitleT(q.TextVariantIndex));
            q.Desc = LoadText(data.Desc, q.Def.GetDescKey(q.TextVariantIndex),
                q.Def.GetDescT(q.TextVariantIndex));
            if (data.ProviderId > 0) {
                if (data.ProviderType == QuestProviderType.Ship) {
                    var ship = s.Query.NPCShipById.Ask(data.ProviderId);
                    if (ship != null) {
                        q.Provider = ship;
                    } else {
                        if (!q.IsEnded) {
                            D.Err("Could not deserialize quest source ship: {0}: {1}",
                                data.DefId, data);
                        } else {
                            D.Warn("QuestData can't find a SHIP provider by id: {0}: {1}",
                                data.ProviderId, data);
                        }
                    }
                } else if (data.ProviderType == QuestProviderType.SpaceObject) {
                    if (s.Universe.TryFind((int) data.ProviderId, out var so)) {
                        q.Provider = so;
                    } else if (q.IsEnded && q.Def.Flags.HasFlag(QuestFlags.Signal)) {
                        D.Warn("QuestData can't find the removed Signal provider by id: {0}: {1}",
                            data.ProviderId, data);
                    } else {
                        if (!q.IsEnded) {
                            D.Err("Could not deserialize quest source SO: {0}: {1}",
                                data.DefId, data);
                        } else {
                            D.Warn("QuestData can't find a SO provider by id: {0}: {1}",
                                data.ProviderId, data);
                        }
                        return false;
                    }
                } else if (data.ProviderType == QuestProviderType.Colonist) {
                    var mind = s.Query.FindMind.Ask(data.ProviderId);
                    if (mind?.Owner != null) {
                        var cp = new ColonistQuestProvider(mind.Owner);
                        cp.QuestId = q.Id;
                        if (data.ProviderExtraBeingIds != null) {
                            cp.ExtraBeingIds = data.ProviderExtraBeingIds;
                        }
                        q.Provider = cp;
                    } else {
                        if (!q.IsEnded) {
                            D.Err("Could not deserialize quest colonist provider: {0}: {1}",
                                data.DefId, data);
                        } else {
                            D.Warn("QuestData can't find colonist provider mind by id: {0}: {1}",
                                data.ProviderId, data);
                        }
                        return false;
                    }
                } else {
                    D.Err("Undefined quest provider type when save/loading: {0}",
                        D.DumpInstance(data));
                }
            }
            if (!QuestVarData.TryDeserialize(s, data.Variables, q)) {
                D.Err("Could not deserialize quest vars: {0}: {1}",
                    data.DefId, data);
                return false;
            }
            if (!QuestRewardData.TryDeserialize(s, data.Rewards, q)) {
                D.Err("Could not deserialize quest rewards: {0}: {1}",
                    data.DefId, data);
                return false;
            }
            if (!QuestStepData.TryDeserialize(s, data.Steps, q)) {
                // Quest steps changed a lot, and now there are test files
                // that have old step Ids, it's ok to fail to deserialize them
                D.ErrSoft("Could not deserialize quest steps: {0}: {1}",
                    data.DefId, data);
                return false;
            }

            return true;
        }

        // Saves made before def text was left out hold it as text. The English one is what a
        // quest got while its key had no translation yet (or the game ran in English), so it
        // is swapped for the current language; anything else is a story event's own text.
        private static string LoadText(string saved, string key, string defText) {
            if (saved == null) { return defText; }
            if (key != null && saved == Translations.FallbackFor(key)) { return defText; }
            return saved;
        }
    }
}
