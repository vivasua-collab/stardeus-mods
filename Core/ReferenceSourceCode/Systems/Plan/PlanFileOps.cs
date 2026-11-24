using System.Collections.Generic;
using Game.Utils;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Plan {
    [MessagePackObject]
    public class SavedPlan {
        [Key("Width")] public int Width;
        [Key("Height")] public int Height;
        [Key("Data")] public int[] Data;
    }

    public static class PlanFileOps {
        private const string PlanExtension = ".plan";
        public static string PlansDir => $"{Application.persistentDataPath}/Plans";

        public static void Save(int[] data, int width, int height, string name) {
            Files.EnsureDir(PlansDir);
            var saved = new SavedPlan {
                Width = width,
                Height = height,
                Data = data
            };
            var path = $"{PlansDir}/{Files.SanitizeFileName(name)}{PlanExtension}";
            SaveLoadUtils.TrySerialize(saved, path);
        }

        public static List<string> GetNames() {
            return Files.ListDirectoryFiles(PlansDir, PlanExtension, true);
        }

        public static void LoadInto(string name, int[] target, int targetWidth, int targetHeight) {
            var path = $"{PlansDir}/{name}{PlanExtension}";
            if (!SaveLoadUtils.TryDeserialize<SavedPlan>(path, out var saved)) { return; }
            if (saved?.Data == null) { return; }

            int offsetX = (targetWidth - saved.Width) / 2;
            int offsetY = (targetHeight - saved.Height) / 2;

            for (int i = 0; i < saved.Data.Length; i++) {
                if (saved.Data[i] == 0) { continue; }
                int sx = i / saved.Height;
                int sy = i - saved.Height * sx;
                int tx = sx + offsetX;
                int ty = sy + offsetY;
                if (tx < 0 || tx >= targetWidth || ty < 0 || ty >= targetHeight) { continue; }
                target[tx * targetHeight + ty] = saved.Data[i];
            }
        }
    }
}
