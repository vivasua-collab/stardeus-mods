using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class Material_HighValueQuestReward : QuestReward, IRewardPackage {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new Material_HighValueQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "Material_HighValue";
        public const string Key_MatId = "MatId";
        public const string Key_Amt = "Amt";
        private MatType matType;
        private int amt;
        public Material_HighValueQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {

            var matTypeIdKey = def.DataKey(Key_MatId);
            var amtKey = def.DataKey(Key_Amt);
            if (isLoading) {
                var typeId = data.Data.GetString(matTypeIdKey);
                matType = MatType.Get(typeId);
                amt = data.Data.GetInt(amtKey);
                if (matType == null) {
                    D.Err("Failed loading HighValueMaterial type: {0}", typeId);
                } else {
                    SetTexts();
                    return;
                }
            }
            var rng = new Rng(seed);
            var netWorthFrac = rng.Curve(def.Config.GetFloatSet(PropertyIdH.NetWorthCurve));
            var min = def.Config.GetInt(PropertyIdH.MinValue, 0);
            var max = def.Config.GetInt(PropertyIdH.MaxValue, 0);
            var minPrice = def.Config.GetInt(PropertyIdH.MinPrice, 0);
            S.Sys.Economy.UpdateNetWorth();
            double nw = S.Situation.NetWorth * 1_000_000d;
            var value = nw * netWorthFrac;
            if (min > 0 && value < min) {
                value = min;
            }
            if (max > 0 && value > max) {
                value = max;
            }
            value *= SectorDifficultyMul();
            // Difficulty-driven max stack size (200 in easy sectors .. 1000 in hard ones),
            // also used to bias material selection toward items pricey enough to deliver
            // `value` within the cap: amt = value/price <= maxQty => price >= value/maxQty.
            // Without this, a cheap material (e.g. lithium) could yield an absurd stack size.
            var maxQty = (int) CurveDef.Get(CurveId.Quests_RewardMaxMatQuantity)
                .Evaluate(S.Situation.SectorDifficulty);
            if (maxQty > 0) {
                var capFloor = (int) (value / maxQty);
                if (capFloor > minPrice) {
                    minPrice = capFloor;
                }
            }
            var mat = S.Query.RandomMatWithValue.Ask(2, minPrice, value, rng);
            var d = data.GetOrCreateData();
            matType = mat.Type;
            amt = mat.StackSize;
            // Hard safety net: covers the rarity-fallback recursion in RandomMatWithValue
            // which drops the price floor when no candidate matches it.
            if (maxQty > 0 && amt > maxQty) {
                amt = maxQty;
            }
            d.SetString(matTypeIdKey, matType.Id);
            d.SetInt(amtKey, amt);
            SetTexts();
        }

        private void SetTexts() {
            Icon = matType.IconId;
            Title = Texts.LinkToCodexNoIcon(matType.Def);
            if (amt > 1) {
                Title = $"{Title} <b>x{amt}</b>";
            }
        }

        public override string Apply() {
            var storages = S.Sys.Inventory.StoragesByGroup(matType.GroupH);
            if (!Arrays.IsEmpty(storages)) {
                foreach (var st in storages) {
                    if (st.WillAccept(matType, amt, out _)) {
                        st.Store(matType, null, amt, null);
                        return T.QuestRewardMaterial_V2.F(matType.Def.CodexLink, amt);
                    }
                }
            }
            // couldn't find a capable loader, let's spawn shit near the ship computer
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                    c => c.Tile.IsConstructed);
            if (cpu != null) {
                var spawnPoint = EntityUtils.SpawnableAround(cpu.Tile.CenterIdx, 5f, true);
                var pos = EntityUtils.ToPos(spawnPoint);
                if (!Pos.IsOOB(pos)) {
                    var spawned = EntityUtils.SpawnRawMaterial(matType,
                        amt, pos, 0.5f);
                    if (spawned != null) {
                        return T.QuestRewardMaterial_V2.F(matType.Def.CodexLink, amt);
                    }
                }
            }

            D.Err("failed to apply material reward, nowhere to spawn it! {0}", this);
            return null;
        }

        public bool PopulatePackage(QuestPackage package) {
            package.Add(matType, amt);
            return true;
        }
    }
}
