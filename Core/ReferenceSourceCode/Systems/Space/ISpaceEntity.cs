using System;
using System.Collections.Generic;
using Game.Data.Space;
using UnityEngine;

namespace Game.Systems.Space {
    public interface ISpaceEntity {
        public int Id { get; }
        public string Name { get; }
        public Vector2 Position { get; }
        public Vector2 UIPosition { get; }
        public List<SpaceLink> Links { get; }
        public Color LinkColor { get; }
        public SpaceBiome Biome { get; }
        public SpaceObject SO { get; }
        public void AddLink(SpaceLink link);
        public void RemoveLink(SpaceLink link);
    }
}
