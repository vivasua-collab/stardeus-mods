using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace Game.Systems.Space {
    [MessagePackObject]
    public class SpaceLinkData {
        [Key("From")] public int From;
        [Key("To")] public int To;

        public static SpaceLinkData Serialize(SpaceLink link) {
            var d = new SpaceLinkData();
            d.From = link.From.Id;
            d.To = link.To.Id;
            return d;
        }
        public static void DeserializeLinks<T>(Dictionary<int, T> byId,
                SpaceLinkData[] links) where T : ISpaceEntity {
            foreach (var l in links) {
                var from = byId[l.From];
                var to = byId[l.To];
                var link = new SpaceLink(from, to);
                from.AddLink(link);
                to.AddLink(link);
            }
        }
    }
}
