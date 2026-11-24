using System;
using Game.Constants;
using Game.Data.Space;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceLink {
        public ISpaceEntity From { get; private set; }
        public ISpaceEntity To { get; private set; }
        public Color ColorFrom => From.LinkColor;
        public Color ColorTo => To.LinkColor;
        public Vector2 FromWithOffset(float offset) => Vector2.MoveTowards(From.Position, To.Position, offset);
        public Vector2 ToWithOffset(float offset) => Vector2.MoveTowards(To.Position, From.Position, offset);
        public float Length => Vectors.Distance(From.Position, To.Position);
        public float LengthSqr => Vectors.SqrDistance(From.Position, To.Position);

        public static SpaceLink ForWarp(SpaceLink cache, ISpaceEntity from, ISpaceEntity to) {
            cache ??= new SpaceLink(from, to);
            cache.From = from;
            cache.To = to;
            return cache;
        }

        public float LinkAngleFor(ISpaceEntity reg) {
            if (reg == From) {
                return Vectors.AnglesBetween(To.Position, From.Position, From.Position + new Vector2(1f, 0));
            } else {
                return Vectors.AnglesBetween(From.Position, To.Position, To.Position + new Vector2(1f, 0));
            }
        }

        public ISpaceEntity OtherSideOf(ISpaceEntity from) {
            if (From == from) {
                return To;
            }
            if (To == from) {
                return From;
            }
            return null;
        }

        public SpaceLink(ISpaceEntity from, ISpaceEntity to) {
            // ensure that lower id is always first
            if (from.Id < 1 || to.Id < 1) {
                D.Err("Creating link with incorrect linkables!");
            }
            if (from.Id > to.Id) {
                var temp = to;
                to = from;
                from = temp;
            }
            From = from;
            To = to;
        }

        public override int GetHashCode() {
            return Hashes.Quick(From.Id, To.Id);
        }

        public override bool Equals(object obj) {
            if (obj is SpaceLink other) {
                return other.From == From && other.To == To;
            }
            return false;
        }

        public bool HasCommonLinkable(SpaceLink other) {
            return From == other.From || To == other.To;
        }

        public bool Contains(ISpaceEntity ntt) {
            if (ntt == null) {
                return false;
            }
            return From == ntt || To == ntt;
        }

        public override string ToString() {
            return $"SpaceLink[{From}<->{To}]";
        }

        public static bool ExistsFor(ISpaceEntity a, ISpaceEntity b) {
            foreach (var link in a.Links) {
                var other = link.OtherSideOf(a);
                if (other == b) {
                    return true;
                }
            }
            #if !PRODUCTION_BUILD
            foreach (var link in b.Links) {
                var other = link.OtherSideOf(b);
                if (other == a) {
                    D.Err("Unexpected! Link did not exist on a but exists on b!");
                    return false;
                }
            }
            #endif
            return false;
        }

        public static float AngleBetween(SpaceLink a, SpaceLink b) {
            Vector2 va;
            Vector2 vb;
            Vector2 vc;
            if (a.From == b.From) {
                va = a.To.Position;
                vb = a.From.Position;
                vc = b.To.Position;
            } else if (a.From == b.To) {
                va = a.To.Position;
                vb = a.From.Position;
                vc = b.From.Position;
            } else if (a.To == b.From) {
                va = a.From.Position;
                vb = a.To.Position;
                vc = b.To.Position;
            } else if (a.To == b.To) {
                va = a.From.Position;
                vb = a.To.Position;
                vc = b.From.Position;
            } else {
                D.Err("No intersection between linkables! {0} and {1}", a, b);
                return 180f;
            }
            return Vectors.AnglesBetween(va, vb, vc);
        }

        public static SpaceLink Between(ISpaceEntity a, ISpaceEntity b) {
            foreach (var link in a.Links) {
                var otherSide = link.OtherSideOf(a);
                if (otherSide == b) {
                    return link;
                }
            }
            return null;
        }

        // A region link no relay can cross. Decided from the generated sectors, not the region
        // templates: generation strips a forced relay that has no relay neighbour
        public static bool IsIsolatedRegion(SpaceLink link) {
            if (link.From is SpaceRegion from && link.To is SpaceRegion to) {
                return !SOUtils.HasHyperjumpRelay(from) || !SOUtils.HasHyperjumpRelay(to);
            }
            return false;
        }

        public void UpdateName(GameObject go) {
            go.name = string.Format("SpaceLink_{0}_{1}",
                From.Name, To.Name);
        }
    }
}
