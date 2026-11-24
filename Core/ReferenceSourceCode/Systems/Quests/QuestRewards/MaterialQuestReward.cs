using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class MaterialQuestReward : QuestReward, IRewardPackage {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new MaterialQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "Material";
        private MatType mat;
        private int amt;
        public MaterialQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            LoadMat(def);
        }

        private void LoadMat(QuestRewardDef def) {
            var cfg = def.Config;
            var typeId = cfg.GetString(PropertyIdH.MatType, null);
            amt = cfg.GetInt(PropertyIdH.Amount, 1);
            if (typeId == null) {
                var matTypeIdKey = cfg.GetString(PropertyIdH.DVar_MatTypeId);
                var amtKey = cfg.GetString(PropertyIdH.DVar_Amount);
                typeId = Data.Data.GetString(matTypeIdKey);
                amt = Data.Data.GetInt(amtKey);
            }
            mat = MatType.Get(typeId);
            if (mat == null) {
                throw new System.Exception(
                    $"Material not found by id: {typeId}. It's either quest misconfiguration or mod was removed.");
            }
            Icon = mat.IconId;
            Title = Texts.LinkToCodexNoIcon(mat.Def);
            Title = $"{Title} <b>x{amt}</b>";
        }

        public override string Apply() {
            var storages = S.Sys.Inventory.StoragesByGroup(mat.GroupH);
            if (!Arrays.IsEmpty(storages)) {
                foreach (var st in storages) {
                    if (st.WillAccept(mat, amt, out _)) {
                        st.Store(mat, null, amt, null);
                        return T.QuestRewardMaterial_V2.F(mat.Def.CodexLink, amt);
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
                    var spawned = EntityUtils.SpawnRawMaterial(mat,
                        amt, pos, 0.5f);
                    if (spawned != null) {
                        return T.QuestRewardMaterial_V2.F(mat.Def.CodexLink, amt);
                    }
                }
            }

            D.Err("failed to apply material reward, nowhere to spawn it! {0}", this);
            return null;
        }

        public bool PopulatePackage(QuestPackage package) {
            package.Add(mat, amt);
            return true;
        }
    }
}
