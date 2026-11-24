using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class ProjectilesSysData {
        [Key("Projectiles")]
        public Projectile[] Projectiles;
    }
}
