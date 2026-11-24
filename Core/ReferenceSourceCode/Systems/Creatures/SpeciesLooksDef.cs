using System;
using System.Linq;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Components;
using Game.Rendering;

namespace Game.Systems.Creatures {
    public class SpeciesLooksDef {
        private static readonly Dictionary<int, SpeciesLooksDef> all = new();

        [ExecuteOnReload]
        private static void ClearStatics() {
            all.Clear();
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Register() {
            LoadKnown();
        }

        public static void LoadKnown() {
            if (all.Count != 0) { return; }
            D.Warn("Loading species looks...");
            var types = The.ModLoader.LoadObjects<SpeciesLooksDef>("Config/Species/Looks");
            foreach (var s in types) {
                // D.Log("Registering known need type: {0}", s);
                s.IdH = Hashes.S(s.Id);
                s.Configure();
                if (!all.TryAdd(s.IdH, s)) {
                    D.Err("Cannot add SpeciesLooksDef: {0}. Already added!", s.Id);
                }
            }
        }
        public static SpeciesLooksDef Get(string type) {
            return Get(type, Hashes.S(type));
        }

        public static SpeciesLooksDef Get(string type, int typeH) {
            if (all.TryGetValue(typeH, out var ability)) {
                return ability;
            }
            D.Err("SpeciesLooksDef with id {0} (hash {1}) is not defined! See Config/Species/Looks folder",
                    type, typeH);
            return null;
        }
        public static SpeciesLooksDef Get(int type) {
            if (all.TryGetValue(type, out var ability)) {
                return ability;
            }
            return null;
        }
        #if !PRODUCTION_BUILD
        // Dev iteration helper: re-reads only the surgery view layout from the Looks JSON onto
        // the already-registered defs. The rest of the def is deliberately left alone, because
        // everything else (part types, sprite caches, Species.LooksDef links) is resolved once
        // at load time and swapping instances at runtime would leave those references
        // inconsistent.
        public static void ReloadBodyPartSlotLayouts() {
            var types = The.ModLoader.LoadObjects<SpeciesLooksDef>("Config/Species/Looks");
            foreach (var fresh in types) {
                var existing = Get(Hashes.S(fresh.Id));
                if (existing == null) { continue; }
                existing.BodyPartSlotViewOffsets = fresh.BodyPartSlotViewOffsets ?? new();
                existing.BodyPartSlotLines = fresh.BodyPartSlotLines ?? new();
            }
        }
        #endif
        public string Id;
        public int IdH;
        // Horizontal = sleeping, dead or broken down in case of robots
        // Sprite should have same name but end with _H
        public bool HasHorizontal;
        public SpeciesLooksPartDef[] PartDefs;
        public readonly Dictionary<int, SpeciesLooksPartDef> Parts = new();
        public float BaseOffset;
        public Vector2 BarOffset; // = new(0f, -0.3f);
        public Vector2 BubbleOffset; // = new(0.5f, 0.8f);
        public Vector2 CarryOffset; // = new(0, 0.5f);
        public string PreviewRot;
        public float PreviewScale;
        public Vector2 PreviewOffset;
        public Vector2 PreviewOffsetWeapon;
        public float BodyPreviewYShift;
        public float BodyPreviewScaleMul;
        public string BodyPreviewRot;
        public string HeadPreviewRot;
        public Vector2 RiderOffset;
        public Vector2[] FaceOffsetsH;
        public int SortOrderAdjustment;
        /// <summary>
        /// In the body management view, each body slot widget's offset from the center when the preview is
        /// </summary>
        public Dictionary<string, Vector2> BodyPartSlotViewOffsets;
        /// <summary>
        /// In the body management view, each body slot line coordinates
        /// </summary>
        public Dictionary<string, Vector2[]> BodyPartSlotLines;
        public SpeciesLooksEyeOverrideDef[] EyeOverrides;
        public string[] RebuildForConditions;
        public int[] RebuildForConditionIds;
        public bool IsAnimated;
        // Bob and sway while walking (Ambient Motion setting), none when null
        public WalkBobDef Walk;
        // Squirm now and then while lying incapacitated (Ambient Motion setting), none when null
        public DownedWiggleDef Wiggle;
        public bool Cache;
        private readonly Dictionary<Facing.Type, float> carryZ = new() {
            { Facing.Type.Down, -0.5f },
            { Facing.Type.Left, -0.5f },
            { Facing.Type.Up, 0.5f },
            { Facing.Type.Right, -0.5f },
        };

        private readonly Dictionary<Facing.Type, int> carrySortOrders = new() {
            { Facing.Type.Down, SpriteOrder.CarriedObj_D },
            { Facing.Type.Up, SpriteOrder.CarriedObj_U },
            { Facing.Type.Left, SpriteOrder.CarriedObj_L },
            { Facing.Type.Right, SpriteOrder.CarriedObj_R },
        };

        // Public setters so the loader can fill them: with internal ones Newtonsoft silently
        // dropped every species' authored values and all selection boxes were auto-sized
        public Vector2 SelectOffset { get; set; }
        public Vector2 SelectSize { get; set; }
        public override string ToString() {
            return $"BeingLooksDef: {Id}";
        }
        public string[] CarriedObjSortOrders;
        public float[] CarryZOffsets;
        public Vector2[] WeaponOffsets;
        public void Configure() {
            if (PreviewScale == 0f) {
                PreviewScale = 1f;
            }

            PreviewRot ??= "D";

            if (CarryZOffsets?.Length == 4) {
                carryZ[Facing.Type.Down] = CarryZOffsets[0];
                carryZ[Facing.Type.Left] = CarryZOffsets[1];
                carryZ[Facing.Type.Up] = CarryZOffsets[2];
                carryZ[Facing.Type.Right] = CarryZOffsets[3];
            }

            if (WeaponOffsets?.Length > 0) {
                PreviewOffsetWeapon = WeaponOffsets[(int) Facing.Type.Down];
            }

            if (CarriedObjSortOrders?.Length == 4) {
                carrySortOrders[Facing.Type.Down] = SpriteOrder.ForName(
                    CarriedObjSortOrders[(int) Facing.Type.Down], Id);
                carrySortOrders[Facing.Type.Left] = SpriteOrder.ForName(
                    CarriedObjSortOrders[(int) Facing.Type.Left], Id);
                carrySortOrders[Facing.Type.Up] = SpriteOrder.ForName(
                    CarriedObjSortOrders[(int) Facing.Type.Up], Id);
                carrySortOrders[Facing.Type.Right] = SpriteOrder.ForName(
                    CarriedObjSortOrders[(int) Facing.Type.Right], Id);
            }

            if (RebuildForConditions != null) {
                RebuildForConditionIds = new int[RebuildForConditions.Length];
                for (int i = RebuildForConditions.Length - 1; i >= 0; i--) {
                    var cond = RebuildForConditions[i];
                    RebuildForConditionIds[i] = Hashes.S(cond);
                }
            }

            if (EyeOverrides != null) {
                foreach (var ov in EyeOverrides) {
                    ov.Configure(Id);
                }
            }

            if (PartDefs == null) {
                // D.Err("PartDefs failed to parse in SpeciesLooksDef: {0}", Id);
                return;
            }

            var maxSize = 0f;
            // Only the parts that resolve stay in PartDefs: InitializeFor builds a look part for
            // every entry, and one without a type broke loading every being of the species (a
            // mod's "Tail" looks part with no Tail look part type kept boot loading forever)
            var kept = new List<SpeciesLooksPartDef>(PartDefs.Length);
            foreach (var part in PartDefs) {
                if (part?.Type == null) {
                    D.Err("Unknown part type {0} in species looks def: {1}", part?.PartType, Id);
                    continue;
                }
                if (!Parts.TryAdd(part.Type.IdH, part)) {
                    D.Err("Duplicate part type {0} in species looks def: {1}",
                        part.Type, Id);
                    continue;
                }
                if (part.Scale > maxSize) {
                    maxSize = part.Scale;
                }
                kept.Add(part);
            }
            if (kept.Count != PartDefs.Length) {
                PartDefs = kept.ToArray();
            }

            if (BodyPartSlotViewOffsets == null) {
                D.Err("BodyPartSlotViewOffsets are null for SpeciesLooksDef! {0}", Id);
                BodyPartSlotViewOffsets = new();
            }

            if (BodyPartSlotLines == null) {
                D.Err("BodyPartSlotLines are null for SpeciesLooksDef! {0}", Id);
                BodyPartSlotLines = new();
            }

            var autoSelectSize = SelectSize == Consts.V2Zero;
            if (autoSelectSize) {
                SelectSize = new Vector2(maxSize, maxSize);
            }
        }
        public float CarryZ(Facing.Type facing) {
            return carryZ[facing];
        }

        public int SortOrderFor(LookPart part, Facing.Type facing) {
            // can't make all parts on the same look layer because they would get split into multiple atlases
            // and then some stuff would get rendered without proper sorting
            var type = part.Type;
            if (type == null) {
                D.Err("No order for part with unresolved type: {0}", part);
                return SpriteOrder.Being + SortOrderAdjustment;
            }
            var baseOrder = type.SortOrderDirectionalResolved != null
                ? type.SortOrderDirectionalResolved[(int) facing]
                : SpriteOrder.ForName(type.SortOrder, Id);
            return type.IgnoreSortAdjustment ? baseOrder : baseOrder + SortOrderAdjustment;
        }

        public int CarriedObjSortOrder(Facing.Type facing) {
            return carrySortOrders[facing];
        }

        public void InitializeFor(BeingData d) {
            var looks = new BeingLooks();
            d.Looks = looks;

            if (PartDefs == null) {
                throw new Exception(
                    $"PartDefs not defined for being species looks: {this} -> {d}");
            }

            int idx = -1;
            looks.NumParts = PartDefs.Length;
            looks.Parts = new LookPart[looks.NumParts];
            looks.HeadPartIdx = -1;
            int bodyPartIdx = -1;
            for (int i = 0; i < looks.NumParts; i++) {
                var def = PartDefs[i];
                if (def == null) {
                    throw new Exception(
                        $"PartDefs[{i}] is null for looks: {this} -> {d}");
                }
                var t = def.Type;

                // Bump the index for parts that have a mesh
                // Details will have to follow the main part!
                idx++;

                var part = new LookPart {
                    Offset = def.Offset,
                    Scale = def.Scale,
                    Index = idx,
                    RenderSorted = def.RenderSorted,
                    PosFilter = def.PositionFilter,
                };
                part.SetType(t);
                if (t == LookPartTypeDef.Head) {
                    looks.HeadPartIdx = i;
                } else if (t == LookPartTypeDef.Body) {
                    bodyPartIdx = i;
                }

                looks.Parts[i] = part;
            }
            // head is inside the body
            if (looks.HeadPartIdx == -1) {
                looks.HeadPartIdx = bodyPartIdx;
            }
        }
        public bool TryEyeSpriteOverride(BeingData data,
                LookPartTypeDef type,
                out SpeciesLooksEyeOverrideDef eyeOverride) {
            if (EyeOverrides != null) {
                var ol = EyeOverrides.Length;
                for (int i = 0; i < ol; i++) {
                    var ov = EyeOverrides[i];
                    if (ov.Matches(data, type)) {
                        eyeOverride = ov;
                        return true;
                    }
                }
                // no override
            }
            eyeOverride = null;
            return false;
        }
        public void ResolveFace(BeingData data, LookPart part, bool forAvatar, bool vertical,
                ref string pSpriteId, ref bool ignoreColor, bool forceClosed = false) {
            // Add (L)/(R) if eyes differ on sides
            bool hasLR = true;
            bool canHaveState = true;
            // eye state as "Closed", etc
            string state = null;
            var isEye = part.Type != LookPartTypeDef.Face;
            if (isEye && TryEyeSpriteOverride(data, part.Type,
                    out SpeciesLooksEyeOverrideDef ov)) {
                // may only add state
                if (ov.SpriteId != null) {
                    pSpriteId = ov.SpriteId;
                    hasLR = ov.HasLR;
                }
                if (ov.SpriteState != null) {
                    state = ov.SpriteState;
                } else {
                    canHaveState = ov.CanBeClosed;
                }
                ignoreColor = ov.IgnoreColor;
            }
            if (canHaveState && !forAvatar && (data.Body.IsIncapacitated ||
                    (!vertical && data.HasCondition(ConditionIdH.Sleeping)))) {
                if (!data.HasTrait(TraitIdH.OpenEyedSleeper) == true) {
                    state = "Closed";
                }
            }
            // used to resolve a blink's closed-eye sprite up front (see BeingLooksComp blink cache)
            if (forceClosed && canHaveState && state == null) {
                state = "Closed";
            }
            if (isEye && state != null) {
                // close the eyes when lying down
                pSpriteId = string.Intern($"{pSpriteId}_{state}");
            }
            if (hasLR) {
                if (part.Type == LookPartTypeDef.Eye_L) {
                    pSpriteId = string.Intern($"{pSpriteId}(L)");
                } else if (part.Type == LookPartTypeDef.Eye_R) {
                    pSpriteId = string.Intern($"{pSpriteId}(R)");
                }
            }
        }
    }
}
