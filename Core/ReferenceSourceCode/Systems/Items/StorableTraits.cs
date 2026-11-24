using System;

namespace Game.Systems.Items {
    // What the objects a storage group can hold have in common, derived from their
    // defs. Decides which storage filters mean anything - an urn's ashes have neither.
    [Flags]
    public enum StorableTraits {
        None = 0,
        // At least one storable def carries ObjQuality
        Quality = 1,
        // At least one storable def is worn gear, see ItemOwnership.TracksOwnership
        Ownership = 2,
        // At least one storable def is a body part, which can be crippled
        BodyPart = 4,
    }
}
