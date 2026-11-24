namespace Game.Data {
    public sealed class CPUResources {
        public long Tick;
        public int CPU;
        public int Disk;
        public int Memory;

        public void CopyFrom(CPUResources other) {
            CPU = other.CPU;
            Disk = other.Disk;
            Memory = other.Memory;
        }

        public void Reset() {
            CPU = Disk = Memory = 0;
        }
    }
}
