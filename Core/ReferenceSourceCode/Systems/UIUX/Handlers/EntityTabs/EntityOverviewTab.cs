using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class EntityOverviewTab : BaseEntityTab {
        public const string TabId = "EntityOverview";

        [SelfInit]
        private static void Register() {
            RegisterTab(new EntityOverviewTab());
        }

        public override string TabName => T.EnergyOverview;

        private readonly EntityOverviewContext ctx = new();

        private EntityOverviewBlock[] blocks;

        public override int Order => -20;

        public override bool ShowFor(GameState s, Tile tile) {
            InitBlocks();
            bool willShow = false;
            for (int i = blocks.Length - 1; i >= 0; i--) {
                if (blocks[i].ShowFor(s, tile)) {
                    willShow = true;
                    break;
                }
            }
            if (!willShow) { return false; }
            ctx.Tile = tile;
            ctx.Entity = tile;
            return true;
        }

        public override bool ShowFor(GameState s, ObjData obj) {
            InitBlocks();
            bool willShow = false;
            for (int i = blocks.Length - 1; i >= 0; i--) {
                if (blocks[i].ShowFor(s, obj)) {
                    willShow = true;
                    break;
                }
            }
            if (!willShow) { return false; }
            ctx.Obj = obj;
            ctx.Entity = obj.Obj;
            return true;
        }

        public override bool ShowFor(GameState s, BeingData being) {
            InitBlocks();
            bool willShow = false;
            bool unrevealed = StasisUtils.IsUnrevealed(being);
            for (int i = blocks.Length - 1; i >= 0; i--) {
                if (unrevealed && !blocks[i].ShowForUnrevealed) { continue; }
                if (blocks[i].ShowFor(s, being)) {
                    willShow = true;
                    break;
                }
            }
            if (!willShow) { return false; }
            ctx.Being = being;
            ctx.Entity = being.Being;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            InitBlocks();
            ctx.Lifetime = lifetime;
            int idx = 0;
            bool unrevealed = ctx.Being != null && StasisUtils.IsUnrevealed(ctx.Being);
            for (int i = blocks.Length - 1; i >= 0; i--) {
                var block = blocks[i];
                if (unrevealed && !block.ShowForUnrevealed) { continue; }
                block.LoadContent(ctx, group, ref idx);
            }

            return idx > 0;
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;
            bool unrevealed = ctx.Being != null && StasisUtils.IsUnrevealed(ctx.Being);
            for (int i = blocks.Length - 1; i >= 0; i--) {
                var block = blocks[i];
                if (unrevealed && !block.ShowForUnrevealed) { continue; }
                block.LoadFooter(ctx, group, ref idx);
            }

            return idx > 0;
        }

        private void InitBlocks() {
            if (blocks != null) { return; }
            blocks = Registries.EntityOverviewBlocks.All.Values.OrderByDescending(
                b => b.Order).ToArray();
        }


        protected override void Cleanup() {
            ctx.Reset();
        }

        public override string Id => TabId;
        public override bool ShowForUnrevealed => true;
    }
}
