using System.Collections.Generic;
using Game.Components;
using Game.Data;

namespace Game.Systems.Quests {
    // What the ProvideChargeStation power steps count as done. The Build and Connect steps
    // used to pick "the device" each on their own terms, so Build could complete on the
    // stasis pod's reactor (wired to its own floor socket, off the CPU grid) while Connect
    // could never find it. Every step and every step text now asks this one place.
    //
    // Each later check implies the earlier one (a device on the CPU grid also counts as
    // built), so no step can wait on something the player has already moved past.
    // "On the CPU grid" is ElectricNode.IsReachable, current after every grid rebuild.
    //
    // Takes the step's GameState rather than going through A: step texts are also built
    // while a save is loading.
    public static class PowerTutorialMatch {
        public static ElectricNodeComp FindCpu(GameState s) {
            return s.Components.FindFirstByType<ShipComputerComp>()?.Tile.ENode;
        }

        // BuildFirstConnector: a connector that is, or can be, linked to the CPU
        public static bool HasConnectorForCpu(GameState s, Def connDef) {
            var cpu = FindCpu(s);
            if (cpu == null) { return false; }
            foreach (var node in Nodes(s)) {
                if (node.Tile.Definition == connDef && node.Tile.IsConstructed
                        && (node.IsConnectedTo(cpu) || node.CanConnectTo(cpu, out _))) {
                    return true;
                }
            }
            return false;
        }

        // ConnectFirstConnector: linked straight to the CPU. Being on the CPU grid through
        // something else is not enough, so connectors the ship starts with do not skip the
        // "link it to the ship computer" lesson.
        public static bool HasConnectorLinkedToCpu(GameState s, Def connDef) {
            var cpu = FindCpu(s);
            if (cpu == null) { return false; }
            foreach (var node in Nodes(s)) {
                if (node.Tile.Definition == connDef && node.IsConnectedTo(cpu)) {
                    return true;
                }
            }
            return false;
        }

        // BuildFirstDevice: already on the CPU grid, or free and in reach of a connector
        // that is (or can be) on it. A device wired into another island - the stasis pod,
        // a survivor area, a wreck - is neither, because linking it is not what the
        // Connect step can ask for.
        public static bool HasBuiltDevice(GameState s, Def def, Def connDef) {
            return HasDeviceOnCpuGrid(s, def)
                || TryFindLinkableDevice(s, def, connDef, out _, out _);
        }

        // ConnectFirstDevice
        public static bool HasDeviceOnCpuGrid(GameState s, Def def) {
            foreach (var node in Nodes(s)) {
                if (node.Tile.Definition == def && node.Tile.IsConstructed
                        && node.Node.IsReachable) {
                    return true;
                }
            }
            return false;
        }

        // Link targets for the step texts, from the same rules the checks use
        public static void FindLinkTargets(GameState s, Def def, Def connDef,
                out ElectricNodeComp dev, out ElectricNodeComp conn) {
            if (TryFindLinkableDevice(s, def, connDef, out dev, out conn)) { return; }
            dev = null;
            conn = null;
            ElectricNodeComp devOnGrid = null;
            foreach (var node in Nodes(s)) {
                var tile = node.Tile;
                if (conn == null && tile.Definition == connDef && tile.IsConstructed
                        && node.Node.IsReachable) {
                    conn = node;
                }
                if (tile.Definition != def || !tile.IsConstructed) { continue; }
                if (!node.Node.IsReachable) {
                    dev ??= node;
                } else {
                    devOnGrid ??= node;
                }
            }
            dev ??= devOnGrid;
        }

        public static ElectricNodeComp FindConnectorOnCpuGrid(GameState s, Def connDef) {
            foreach (var node in Nodes(s)) {
                if (node.Tile.Definition == connDef && node.Tile.IsConstructed
                        && node.Node.IsReachable) {
                    return node;
                }
            }
            return null;
        }

        private static Dictionary<int, ElectricNodeComp>.ValueCollection Nodes(GameState s) {
            return s.Sys.Electricity.NodeComponents.Values;
        }

        private static bool TryFindLinkableDevice(GameState s, Def def, Def connDef,
                out ElectricNodeComp dev, out ElectricNodeComp conn) {
            dev = null;
            conn = null;
            var cpu = FindCpu(s);
            if (cpu == null) { return false; }
            var nodes = Nodes(s);
            foreach (var node in nodes) {
                if (node.Tile.Definition != def || node.Node.NumConnections != 0) {
                    continue;
                }
                foreach (var other in nodes) {
                    if (other.Tile.Definition != connDef) { continue; }
                    if (!other.Node.IsReachable && !other.CanConnectTo(cpu, out _)) {
                        continue;
                    }
                    // CanConnectTo also rejects unconstructed and heavily damaged nodes
                    if (node.CanConnectTo(other, out _)) {
                        dev = node;
                        conn = other;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
