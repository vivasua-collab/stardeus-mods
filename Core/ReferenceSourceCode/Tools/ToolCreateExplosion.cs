using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;
namespace Game.Tools {
    public sealed class ToolCreateExplosion : Tool, IModifyTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolCreateExplosion());
        }
        public const string ToolId = "create_explosion";
        public bool CanBuild => false;
        public bool CanRemove => true;
        public override string Id => ToolId;
        public override string MenuIcon => IconId.WExplosion;
        public override bool IsConfigurable => false;
        public override bool IsDevTool => true;

        public override void OnClickDown(Vector2 coords) {
            var lvl = Rng.URange(1, 10);
            Explosion.Create(S, lvl, position: coords);
            D.Warn("Dealing explosion with level: {0} at {1}", lvl, coords);
            // S.CmdQ.Enqueue(new CmdCreateExplosion(coords, r, s));
        }

        public override string MenuNameFor(string param) {
            return The.Defs.Get(param).NameKey;
        }
    }
}
