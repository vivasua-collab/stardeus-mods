using System;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class EntityOverviewContext {
        public Entity Entity;
        public Tile Tile;
        public ObjData Obj;
        public BeingData Being;
        public Lifetime Lifetime;

        public void Reset() {
            Entity = null;
            Tile = null;
            Obj = null;
            Being = null;
            Lifetime = null;
        }
    }
}
