using Game.Utils;

namespace Game.Components {
    /// <summary>
    /// Use this to display an additional icon in a component that already may
    /// display an icon for some Advert. For example, CrafterComp may show an
    /// icon about "production goal reached", but it will use the ExtraIconComp
    /// To show the icon of the product that is being made.
    /// </summary>
    public sealed class ExtraInfoComp : BaseComponent<ExtraInfoComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ExtraInfoComp());
        }

        public override string ToString() {
            return " * ExtraIcon";
        }
    }
}
