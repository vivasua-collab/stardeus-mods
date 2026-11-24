using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class SandboxBlock_EntityInfo : SandboxBlock {
        public const string BlockId = "EntityInfo";
        public override int Order => -100;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_EntityInfo());

        public override bool ShowFor(GameState s, BeingData being) => true;
        public override bool ShowFor(GameState s, Tile tile) => true;
        public override bool ShowFor(GameState s, ObjData obj) => true;

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            int entityId;
            object entityRef;
            if (ctx.Being != null) {
                entityId = ctx.Being.Being?.Id ?? 0;
                entityRef = (object)ctx.Being.Being ?? ctx.Being;
            } else if (ctx.Tile != null) {
                entityId = ctx.Tile.Id;
                entityRef = ctx.Tile;
            } else if (ctx.Obj != null) {
                entityId = ctx.Obj.Obj?.Id ?? 0;
                entityRef = (object)ctx.Obj.Obj ?? ctx.Obj;
            } else {
                return;
            }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var captured = entityRef;
            var idRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            idRow.Load(IconId.WDev, $"Entity #{entityId}");
            UIUtils.AddTipFunc(lifetime, idRow, () => D.DumpInstance(captured));

            var seed = ctx.S.SeedStr;
            var seedRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            seedRow.Load(IconId.CLeaf, T.GameSeed, seed,
                () => GUIUtility.systemCopyBuffer = seed);
            UIUtils.AddTip(lifetime, seedRow, T.CopyGameSeed);
        }
    }
}
