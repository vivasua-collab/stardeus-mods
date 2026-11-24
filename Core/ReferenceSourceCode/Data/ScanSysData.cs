using System.Collections.Generic;
using Game.Systems;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class ScanSysData {
        [Key("Progress")] public float ScanProgress;
        [Key("ProgressDeep")] public float ScanDeepProgress;
        [Key("Difficulty")] public float ScanDifficulty;
        [Key("DifficultyDeep")] public float ScanDeepDifficulty;
        [Key("Mode")] public ScanMode Mode;
        [Key("Mat")] public string TargetMat;
        [Key("Start")] public long StartTick;
        [Key("StartDeep")] public long DeepStartTick;
        [Key("FoundId")] public int FoundSOId;
        [Key("FoundDeepId")] public int FoundDeepSOId;
        [Key("DeepFocus")] public string DeepFocusMat;
        [Key("DeepFocusMisses")] public int DeepFocusMisses;
    }
}
