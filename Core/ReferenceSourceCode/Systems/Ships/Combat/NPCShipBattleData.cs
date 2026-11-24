using System;
using Game.Data;
using KL.Randomness;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public sealed class NPCShipBattleData {
        [Key("StartTick")] public long StartTick;
        [Key("EndTick")] public long EndTick;
        [Key("AttackerId")] public uint AttackerId;
        [Key("DefenderId")] public uint DefenderId;
        [Key("RNGState")] public uint RNGState;
        [Key("PowerDiff")] public float PowerDiff;

        public static NPCShipBattleData Serialize(NPCShipBattle battle) {
            return new NPCShipBattleData {
                StartTick = battle.StartTick,
                EndTick = battle.EndTick,
                AttackerId = battle.Attacker?.Id ?? 0,
                DefenderId = battle.Defender?.Id ?? 0,
                RNGState = battle.Rng.State,
                PowerDiff = battle.PowerDiff,
            };
        }

        public static bool TryDeserialize(GameState s, NPCShipBattleData data, out NPCShipBattle battle) {
            battle = null;
            if (data == null) return false;

            var attacker = s.Query.NPCShipById.Ask(data.AttackerId);
            var defender = s.Query.NPCShipById.Ask(data.DefenderId);

            if (attacker == null || defender == null) return false;

            var rng = new Rng(data.RNGState);
            battle = new NPCShipBattle {
                StartTick = data.StartTick,
                EndTick = data.EndTick,
                Attacker = attacker,
                Defender = defender,
                Rng = rng,
                PowerDiff = data.PowerDiff,
            };
            return true;
        }
    }
}
