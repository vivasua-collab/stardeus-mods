using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Components {
    // Computer Resources block of the codex. CPU, GPU, Memory and Disk come from four
    // components, but the block needs one header, so the first of them in the def writes it
    // all, in a fixed order. Base amounts only: codex text must not depend on the save
    public static class ComputerResourcesDefInfo {
        public static void Append(StringBuilder sb, Def def, int callerH) {
            if (FirstComputeComponent(def) != callerH) { return; }
            if (sb.Length > 0) {
                sb.Append("\n\n");
            }
            sb.Append(Texts.SubTitle(T.ComputerResources));
            bool first = true;
            AppendLine(sb, def, ComponentIdH.CPUModule, CPUModuleComp.DefaultAmount,
                T.ComputerResourcesCpu, T.CpuModuleAmount_V1, ref first);
            AppendLine(sb, def, ComponentIdH.GPUModule, GPUModuleComp.DefaultAmount,
                T.ResearchComputingGpu, T.CpuModuleAmount_V1, ref first);
            AppendLine(sb, def, ComponentIdH.MemoryModule, MemoryModuleComp.DefaultAmount,
                T.ComputerResourcesMemory, T.MemoryModuleAmount_V1, ref first);
            AppendLine(sb, def, ComponentIdH.DiskModule, DiskModuleComp.DefaultAmount,
                T.ComputerResourcesStorage, T.StorageModuleAmount_V1, ref first);
        }

        private static int FirstComputeComponent(Def def) {
            if (def.Components == null) { return 0; }
            foreach (var comp in def.Components) {
                var h = comp.ComponentH;
                if (h == ComponentIdH.CPUModule || h == ComponentIdH.GPUModule
                        || h == ComponentIdH.MemoryModule || h == ComponentIdH.DiskModule) {
                    return h;
                }
            }
            return 0;
        }

        private static void AppendLine(StringBuilder sb, Def def, int componentH,
                int defaultAmount, string label, string format, ref bool first) {
            var config = def.ComponentConfigFor(componentH, false);
            if (config == null) { return; }
            if (!first) {
                sb.Append('\n');
            }
            first = false;
            var value = format.F(config.GetInt(PropertyIdH.Amount, defaultAmount));
            sb.Append($"{GlyphMap.ArrowRight} {label}: <b>{value}</b>");
        }
    }
}
