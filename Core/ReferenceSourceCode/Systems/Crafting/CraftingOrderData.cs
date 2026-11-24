using System;
using Game.Components;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Crafting {
    [MessagePackObject]
    public sealed class CraftingOrderData {
        [Key("CrafterId")] public int CrafterId;
        [Key("State")] public CraftingOrderState State;
        [Key("Demand")] public ComponentData Demand;
        [Key("Deficit")] public ComponentData Deficit;
        [Key("Progress")] public float Progress;
        [Key("SuppressDeficit")] public bool SuppressDeficit;

        public static CraftingOrderData Serialize(CraftingOrder o) {
            if (!o.Crafter.Entity.IsActive) {
                D.Err("Trying to serialize crafting order for inactive crafter: {0} | {1}",
                    o, o.Crafter.Entity);
                return null;
            }
            var od = new CraftingOrderData();
            od.State = o.State;
            od.CrafterId = o.Crafter.EntityId;
            if (o.Deficit?.HasDeficit == true) {
                od.Deficit = new ComponentData();
                o.Deficit.Save(od.Deficit);
            }
            od.Progress = o.Progress;
            od.SuppressDeficit = o.SuppressDeficitWarning;
            od.Demand = new ComponentData();
            CraftingDemand.Save(o.Demand, od.Demand);
            if (o.Ingredients != null) {
                od.Demand.SetMats("Ingredients", o.Ingredients);
            }
            return od;
        }

        public static bool TryDeserialize(GameState s, CraftingOrderData od,
                out CraftingOrder order) {
            if (od.CrafterId == 0) {
                D.Err("Order was saved with CrafterId == 0: {0}", od);
                order = null;
                return false;
            }
            var crafter = s.Components.Find<MultiCrafterComp>(od.CrafterId);
            if (crafter == null) {
                D.Err("Failed to find crafter by id {0} for order: {1}",
                    od.CrafterId, D.DumpInstance(od));
                order = null;
                return false;
            }
            var demand = CraftingDemand.Load(od.Demand);
            if (demand == null) {
                order = null;
                return false;
            }
            order = new CraftingOrder();
            order.State = od.State;
            order.Crafter = crafter;
            order.Demand = demand;
            order.Progress = od.Progress;
            order.SuppressDeficitWarning = od.SuppressDeficit;
            order.Ingredients = od.Demand.GetMats("Ingredients", null);
            if (od.Deficit != null) {
                order.InitDeficit();
                order.Deficit.Load(od.Deficit);
            }
            order.VerifyIngredients(true);
            return true;
        }
    }
}
