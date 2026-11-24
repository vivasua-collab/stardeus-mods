using KL.Utils;

namespace Game.Systems.Firex {
    /// <summary>
    /// 0 = No blast radius, only damages self,
    /// 2 = cooking station explosion
    /// 5 = large matter reactor explosion
    /// 7 = fusion drive explosion
    /// 10 = big nuclear explosion taking down floor with it
    /// </summary>
    /// <seealso cref="FlammableComp"/>
    public interface IExplosivenessProvider {
        bool IsExplosivenessProviderDisabled { get; }
        int ExplosionLevel { get; }
        Curve ExplosivenessCurve { get; }
        void OnExplode();
    }
}
