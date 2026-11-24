namespace Game.Systems.Stats {
    public interface IStatSource {
        string StatSourceNameT { get; }
        bool GetEffectFor(Stat stat, out float toAdd, out float limit);
        void UpdateStat(Stat stat);
    }
}
