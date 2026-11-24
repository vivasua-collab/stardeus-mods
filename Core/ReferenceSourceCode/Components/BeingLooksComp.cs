using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.CodeGen;
using Game.Visuals.Filters;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using KL.Randomness;
using Game.Systems.Rendering;

using Game.Utils;
using Game.Systems.UIUX;
namespace Game.Components {
    // FIXME Redo Z ordering without sort order
    public sealed class BeingLooksComp : BaseComponent<BeingLooksComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BeingLooksComp());
        }
        private AnimatedSpriteSet[] SpriteSets;
        private AnimatedSpriteSet[] SpriteSetsH;
        public BeingLooks Looks;
        private bool disablePosFilters;
        private bool disablePosFiltersPermanent;
        private ulong hiddenParts;
        public SpeciesLooksDef LooksDef;
        // Shared by all upright sprite sets, null for species that do not bob
        public WalkBob Walk { get; private set; }
        // Shared by all lying sprite sets, null for species that do not wiggle when downed
        public DownedWiggle Wiggle { get; private set; }
        // Shared by all sprite sets of the being, kept across looks changes
        public readonly HitFlash Flash = new();
        public readonly DodgeLean Dodge = new();
        private bool isAltFrame;
        public void NextFrame() {
            isAltFrame = !isAltFrame;
        }

        // Blinking: open/closed eye sprites + slot indices cached for the Down-facing
        // vertical sprite set (the only one eyes render in), so a blink is just two
        // AnimatedSpriteSet.SetSprite calls instead of a full looks rebuild. See CheckBlink.
        private int eyeLIdx = -1;
        private int eyeRIdx = -1;
        private SpriteData eyeLOpenSprite;
        private SpriteData eyeLClosedSprite;
        private SpriteData eyeROpenSprite;
        private SpriteData eyeRClosedSprite;
        private bool isBlinking;
        private float nextBlinkTime;
        private float blinkCloseUntil;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                TogglePosFilters(false, true);
            }
        }

        public void TogglePosFilters(bool on, bool permanent) {
            // D.Warn("Toggling pos filters: {0}/{1} ({2})", on, permanent, Being);
            var off = !on;
            disablePosFiltersPermanent = permanent;
            if (disablePosFilters == off) { return; }
            disablePosFilters = off;
            if (SpriteSets == null) { return; }
            for (int i = SpriteSets.Length - 1; i >= 0; i--) {
                SpriteSets[i].SkipPositionFilters = off;
                if (LooksDef.HasHorizontal) {
                    SpriteSetsH[i].SkipPositionFilters = off;
                }
            }
        }

        private void RebuildHides() {
            hiddenParts = 0UL;
            var lpc = Looks.NumParts;
            for (int i = lpc - 1; i >= 0; i--) {
                var lp = Looks.Parts[i];
                // if sprite is visible, let's add its hides
                if (lp.SpriteId != null) {
                    hiddenParts |= lp.HidesMask;
                }
            }
        }


        public void Rebuild(bool force = false, bool clearCache = false) {
            if (clearCache) {
                ClearCache();
            }
            RebuildHides();
            RebuildSprites();
            Looks.IsDirty = false;
            Being.Graphics.RebuildLooks(force);
            if (clearCache) {
                Being.Data.Avatar.Rebuild(Being.Data);
                Looks.SurgeryPreview?.Rebuild(Being.Data);
            }
        }

        private void InitializeCache() {
            hasCache = LooksDef.Cache;
            if (hasCache) {
                looksCache = new Dictionary<int, AnimatedSpriteSet>();
            }
        }

        public AnimatedSpriteSet SpriteSetFor(Facing.Type facing, bool isVertical) {
            var intF = (int) facing;
            if (!LooksDef.HasHorizontal) {
                return SpriteSets[intF];
            }
            return isVertical ? SpriteSets[intF] : SpriteSetsH[intF];
        }

        private SpriteData Rescale(SpriteData sd, LookPart part) {
            if (Maths.Approximately(part.Scale, 1f)) {
                return sd;
            }
            var sdScaled = sd.Copy();
            sdScaled.Scale = part.Scale;
            return sdScaled;
        }

        // FIXME garbage
        private SpriteData GetFacingSprite(LookPart part, string pSpriteId,
                Facing.Type facing, bool vertical, out bool ignoreColor,
                bool isFallback = false, bool forceClosedEyes = false) {

            ignoreColor = false;
            if (string.IsNullOrWhiteSpace(pSpriteId)) {
                return null;
            }

            if (!vertical) {
                if (part.Type == LookPartTypeDef.Weapon) {
                    // We don't show weapons when units are down
                    return null;
                }
                // face horizontal will use same sprites as vertical
                if (!part.IsFace) {
                    pSpriteId = string.Intern($"{pSpriteId}_H");
                }
            } else if (isAltFrame) {
                pSpriteId = string.Intern($"{pSpriteId}_1");
            }

            bool allowGen = false;
            bool initialVertical = vertical;
            if (part.IsFace) {
                // D.Warn("Getting face sprite: {0} | vert: {1}", part, vertical);

                // we don't see eyes from sides / up
                if (vertical && facing != Facing.Type.Down) {
                    return null;
                }

                LooksDef.ResolveFace(Being.Data, part, false, vertical, ref pSpriteId,
                    ref ignoreColor, forceClosed: forceClosedEyes);

                // face parts don't have horizontal versions
                vertical = true;
                // we'll generate rotated versions from the initial one
                allowGen = true;
            }

            var sprites = RenderingService.Sprites;
            // 1. resolve the sprite id for current facing
            string spriteId = Facing.FaceSpriteTo(pSpriteId, facing);

            // 2. try grabbing the sprite data for this id
            if (sprites.TryFind(spriteId, out var sd)) {
                return Rescale(sd, part);
            }

            // 3. if we didn't have the sprite data, try flipped opposite
            // but only if it's R-L, for up / down we'll use same sprite
            if (allowGen && facing != Facing.Type.Down
                    && sprites.TryFind(Facing.FaceSpriteTo(pSpriteId,
                        Facing.Type.Down), out var sdD)) {
                // try rotating _D sprite to left or right
                // D.Warn("Sprite missing, rotating (L/R): {0}. Vert: {1}. Face: {2}. Facing: {3}",
                //     spriteId, vertical, part.IsFace, facing);
                var sdRot = sdD.Copy();
                switch (facing) {
                    case Facing.Type.Left: {
                            sdRot.Rotation = SpriteData.Rot.Rot90;
                            break;
                        }
                    case Facing.Type.Right: {
                            sdRot.Rotation = SpriteData.Rot.Rot270;
                            break;
                        }
                    case Facing.Type.Up: {
                            sdRot.Rotation = SpriteData.Rot.Rot180;
                            break;
                        }
                }

                sdRot.Id = spriteId;
                sdRot.IdH = Hashes.S(spriteId);
                sprites.Store(sdRot);
                return Rescale(sdRot, part);
            }
            // 4. Check if we have non direction version
            var rawId = pSpriteId;
            D.Ass(rawId != null, "Null sprite id: {0}", part);
            if (sprites.TryFind(rawId, out var sdRaw)) {
                var sdRawCopy = sdRaw.Copy();
                sdRawCopy.Id = spriteId;
                sdRawCopy.IdH = Hashes.S(spriteId);
                sprites.Store(sdRawCopy);
                return Rescale(sdRawCopy, part);
            }
            if (forceClosedEyes) {
                // No closed-eye variant available for this sprite - don't mutate the
                // part's (open) SpriteId over a speculative blink-cache lookup, just
                // report "can't blink" to the caller.
                return null;
            }
            if (!isFallback) {
                The.Looks.GenerateMissing(Rng.Unseeded, Being.Data, part);
                return GetFacingSprite(part, part.SpriteId, facing, initialVertical,
                    out ignoreColor, isFallback: true);
            }
            part.SpriteId = null;
            D.ErrSoft("Panic: Not found sprite {0} for facing {1} on {2} [{3}] (face: {4}). vert: {5}",
                part.Type, facing, entity.Definition, pSpriteId, part.IsFace, vertical);
            #if !PRODUCTION_BUILD
            // return sprites.Find(IconIdH.CMissing);
            #else
            #endif
            return sprites.Find(GraphicsId.NoAtlasTransparent);
        }


        private void SetPosFilter(AnimatedSpriteSet set, Facing.Type facing,
                LookPart look, int seed) {
            if (disablePosFilters) {
                set.SkipPositionFilters = true;
                if (disablePosFiltersPermanent) {
                    return;
                }
            }
            if (look.PosFilterH == -1) { return; }
            if (look.PosFilterH == 0) {
                if (string.IsNullOrEmpty(look.PosFilter)) {
                    look.PosFilterH = -1;
                    return;
                }
                if (!PositionFilterTypesH.TryHashFor(look.PosFilter, out look.PosFilterH)) {
                    D.Err("Position filter not found by name: {0}",
                        look.PosFilter);
                    look.PosFilterH = -1;
                }
            }
            if (set.PositionFilters == null) {
                set.PositionFilters = new AnimatedSpriteSet.SpritePosFilter[set.Size];
            }
            set.PositionFilters[look.Index] =
                PositionFilters.ByNameHash(look.PosFilterH, seed);
        }

        public void RebuildSprites() {
            try {
                bool needsAvatarRebuld = false;
                for (int i = Being.LooksFilters.Count - 1; i >= 0; i--) {
                    var filter = Being.LooksFilters[i];
                    needsAvatarRebuld |= filter.FilterLooks(LooksDef, Looks);
                }
                foreach (var facing in Facing.Types) {
                    BuildSpriteSetFor(facing, true);
                    if (LooksDef.HasHorizontal) {
                        BuildSpriteSetFor(facing, false);
                    }
                }
                if (needsAvatarRebuld) {
                    Being.Data.Avatar.Rebuild(Being.Data);

                }
            } catch (Exception ex) {
                WPopup.Create(true, IconId.CWarning, "Possible game breaking issue", null, string.Format(
                        "Failed rebuilding sprites for species specimen: {0}. Reason: {1}",
                            Being.Species.Id, ex.Message)).Spawn();
                D.LogEx(ex, "Failed building looks sprites");
            }
        }

        private Dictionary<int, AnimatedSpriteSet> looksCache;
        public void ClearCache() {
            if (hasCache) {
                looksCache.Clear();
            }
        }
        private bool hasCache;

        private void ReplaceSprite(AnimatedSpriteSet[] set, int idx, AnimatedSpriteSet newSprite) {
            set[idx] = newSprite;
        }

        public bool IsFailed;

        private void BuildSpriteSetFor(Facing.Type facing, bool vertical) {
            try {
                if (Looks.NumParts == 0) {
                    D.Err("Refusing to build sprites set for looks with 0 parts! {0}", this);
                    return;
                }

                int cacheHash = 0;
                if (hasCache) {
                    cacheHash = Hashes.Quick(Primes.Prime005, Looks.GetHashCode());
                    cacheHash = Hashes.Quick(cacheHash, (int) facing);
                    if (!vertical) {
                        cacheHash = Hashes.Quick(cacheHash, Primes.Prime018);
                    } else if (isAltFrame) {
                        cacheHash = Hashes.Quick(cacheHash, Primes.Prime038);
                    }
                    if (looksCache.TryGetValue(cacheHash, out var spr)) {
                        if (vertical) {
                            ReplaceSprite(SpriteSets, (int) facing, spr);
                        } else {
                            ReplaceSprite(SpriteSetsH, (int) facing, spr);
                        }
                        /*
                        // D.Log("Looks cache hit! {0} {1} {2} -> {3}",
                            facing, vertical, looks, cacheHash);
                        */
                        return;
                    }
                }
                if (vertical && facing == Facing.Type.Down) {
                    // stale from a previous rebuild - repopulated below if eyes are
                    // still visible/closable, left cleared (CanBlink false) otherwise
                    eyeLIdx = -1;
                    eyeRIdx = -1;
                    eyeLOpenSprite = eyeLClosedSprite = null;
                    eyeROpenSprite = eyeRClosedSprite = null;
                }
                // 1 - figure out how much sprites layers will we have?
                var sprSet = new AnimatedSpriteSet(this, Looks.NumParts,
                    entity.Definition.LayerId, Being.Motion.Position.Exact,
                    trackRenderedPos: Looks.HeadPartIdx);
                sprSet.IsRotatable = true;

                sprSet.ZOffset = LooksDef.BaseOffset;
                sprSet.Flash = Flash;
                sprSet.Dodge = Dodge;
                if (vertical && LooksDef.Walk != null) {
                    sprSet.Walk = Walk ??= new WalkBob(LooksDef.Walk);
                } else if (!vertical && LooksDef.Wiggle != null) {
                    var wiggleSeed = EntityId;
                    if (wiggleSeed == 0) {
                        wiggleSeed = Rng.UInt;
                    }
                    sprSet.Wiggle = Wiggle ??= new DownedWiggle(LooksDef.Wiggle, wiggleSeed,
                        IsDowned);
                }

                var lookParts = Looks.Parts;
                for (int i = 0; i < lookParts.Length; i++) {
                    var lookPart = lookParts[i];
                    if (!IsPartVisible(lookPart)) { continue; }
                    var idx = lookPart.Index;
                    sprSet.RenderSorted[idx] = lookPart.RenderSorted;
                    sprSet.Sprites[idx] = GetFacingSprite(lookPart,
                        lookPart.SpriteId, facing, vertical, out var ignoreColor);
                    // D.Warn("Building looks for {0}: {1}: vert: {2}: {3} ({4})",
                    //     Being, facing, vertical, sprSet.Sprites[idx], lookPart.SpriteId);

                    if (vertical && facing == Facing.Type.Down &&
                            (lookPart.Type == LookPartTypeDef.Eye_L
                                || lookPart.Type == LookPartTypeDef.Eye_R)) {
                        CacheBlinkSprite(lookPart, idx, sprSet.Sprites[idx]);
                    }

                    var sortOrder = LooksDef.SortOrderFor(lookPart, facing);
                    if (!vertical) {
                        sortOrder -= 5;
                    }
                    if (lookPart.Type == LookPartTypeDef.Shadow) {
                        sprSet.SetRenderer(S, idx, Renderers.Shadows, sortOrder);
                        sprSet.SetInitialLayer(idx, Renderers.Layer_Shadows);
                    } else {
                        sprSet.SetSortOrder(S, idx, sortOrder);
                    }

                    if (lookPart.Type == LookPartTypeDef.Weapon && LooksDef.WeaponOffsets != null) {
                        sprSet.Offsets[idx] = lookPart.Offset + LooksDef.WeaponOffsets[(int) facing];
                    } else {
                        sprSet.Offsets[idx] = lookPart.Offset;
                    }
                    if (!vertical && lookPart.IsFace && LooksDef.FaceOffsetsH != null) {
                        // if (sprSet.OffsetsPostRot == null) {
                        //     sprSet.OffsetsPostRot = new Vector2[sprSet.Size];
                        // }
                        // sprSet.OffsetsPostRot[idx] = LooksDef.FaceOffsetsH[(int) facing];
                        sprSet.Offsets[idx] += LooksDef.FaceOffsetsH[(int) facing];
                    }

                    var seed = EntityId;
                    if (seed == 0) {
                        seed = Rng.UInt;
                    }
                    SetPosFilter(sprSet, facing, lookPart, seed);
                    if (!ignoreColor) {
                        sprSet.SetColor(idx, lookPart.Color);
                    } else {
                        sprSet.SetColor(idx, Consts.ColorWhite);
                    }
                }
                if (vertical) {
                    if (SpriteSets == null) {
                        SpriteSets = new AnimatedSpriteSet[4];
                    }
                    ReplaceSprite(SpriteSets, (int) facing, sprSet);
                } else {
                    if (SpriteSetsH == null) {
                        SpriteSetsH = new AnimatedSpriteSet[4];
                    }
                    ReplaceSprite(SpriteSetsH, (int) facing, sprSet);
                }
                if (hasCache) {
                    looksCache.Add(cacheHash, sprSet);
                }
            } catch (Exception ex) {
                IsFailed = true;
                D.LogEx(ex, "Failed building sprite set for being {0}. Facing {1}. HasHorizontal {2}",
                    Being.DefinitionId, facing, LooksDef.HasHorizontal);
            }
        }

        // Resolves and caches the closed-eye variant next to the just-resolved open sprite,
        // for the given eye part's slot in the Down-facing vertical sprite set. Called from
        // BuildSpriteSetFor; see the blink fields above for why this is cached up front.
        private void CacheBlinkSprite(LookPart part, int idx, SpriteData openSprite) {
            if (openSprite == null) { return; }
            var closedSprite = GetFacingSprite(part, part.SpriteId, Facing.Type.Down, true,
                out _, forceClosedEyes: true);
            if (part.Type == LookPartTypeDef.Eye_L) {
                eyeLIdx = idx;
                eyeLOpenSprite = openSprite;
                eyeLClosedSprite = closedSprite;
            } else {
                eyeRIdx = idx;
                eyeROpenSprite = openSprite;
                eyeRClosedSprite = closedSprite;
            }
        }

        // True when both eyes have a resolved closed-eye variant to blink to.
        public bool CanBlink =>
            eyeLIdx >= 0 && eyeLClosedSprite != null &&
            eyeRIdx >= 0 && eyeRClosedSprite != null;

        private void SetEyesClosed(bool closed) {
            var ss = SpriteSets?[(int) Facing.Type.Down];
            if (ss == null) { return; }
            // Applied directly to the Down/vertical set (not Graphics.SpriteSet, which may no
            // longer be this set by the time a scheduled reopen fires) - SetSprite is a no-op
            // on the renderer side while that set isn't the one currently showing.
            ss.SetSprite(eyeLIdx, closed ? eyeLClosedSprite : eyeLOpenSprite, S);
            ss.SetSprite(eyeRIdx, closed ? eyeRClosedSprite : eyeROpenSprite, S);
        }

        // Per-tick blink state machine, driven by BeingBlinkSys for eligible (humanoid,
        // camera-close) active beings. Cheap to call on every ineligible being too - bails
        // out on the first failed gate.
        public void CheckBlink() {
            if (!CanBlink) { return; }
            var g = Being.Graphics;
            if (g.IsHidden || !g.IsVertical || g.CurrentFacing != Facing.Type.Down) { return; }
            if (!RenderUtils.IsVisible(g.Position)) { return; }
            var t = GameTimer.time;
            if (isBlinking) {
                if (t >= blinkCloseUntil) {
                    SetEyesClosed(false);
                    isBlinking = false;
                    nextBlinkTime = t + Rng.URange(
                        Tunables.BeingBlinkIntervalMin,
                        Tunables.BeingBlinkIntervalMax);
                }
            } else if (nextBlinkTime == 0f) {
                // first eligible check ever - schedule instead of blinking immediately,
                // so newly-eligible beings don't all blink together on the same tick
                nextBlinkTime = t + Rng.URange(
                    Tunables.BeingBlinkIntervalMin,
                    Tunables.BeingBlinkIntervalMax);
            } else if (t >= nextBlinkTime) {
                SetEyesClosed(true);
                isBlinking = true;
                blinkCloseUntil = t + Rng.URange(
                    Tunables.BeingBlinkDurationMin,
                    Tunables.BeingBlinkDurationMax);
            }
        }

        // Lying hurt where it fell: alive, and not resting in a bed or healing device (both add
        // Sleeping, and a bed that hides the body would leave only the head swinging)
        private bool IsDowned() {
            var data = Being.Data;
            return data.Body.IsIncapacitated && data.HasStateFlag(BeingState.Alive)
                && !data.HasCondition(ConditionIdH.Sleeping);
        }

        private bool IsPartVisible(LookPart lookPart) {
            // The look is unset
            if (lookPart.SpriteId == null) { return false; }
            var bit = lookPart.Type.Bit;
            return (hiddenParts & bit) != bit;
        }
        public void SetLooks(SpeciesLooksDef def, BeingLooks looks) {
            LooksDef = def;
            Looks = looks;
            Walk = null;
            Wiggle = null;
            Being.Graphics.LooksDef = def;
            if (def.IsAnimated) {
                Being.Motion.IsAnimated = true;
            }
            Being.SelectableSize = def.SelectSize;
            Being.SelectOffset = def.SelectOffset;
            InitializeCache();

            S.Clock.ExecuteAfterFramesForUI(1, () => Rebuild());
        }

        public void OnConditionChange(int condId) {
            // D.Warn("Condition changed");
            if (Arrays.ContainsSafe(LooksDef.RebuildForConditionIds, condId)) {
                Rebuild(true, clearCache: true);
            }
        }


        #if !PRODUCTION_BUILD
        public ulong DebugHiddenParts => hiddenParts;
        public void DebugRebuildHides() => RebuildHides();
        #endif
        public override string ToString() {
            return $" * BeingLooks [{EntityId}]";
        }
    }
}
