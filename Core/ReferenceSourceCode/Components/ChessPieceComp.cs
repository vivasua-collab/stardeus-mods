using Game.Achievements;
using Game.Utils;
namespace Game.Components {
    public sealed class ChessPieceComp : BaseComponent<ChessPieceComp> {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ChessPieceComp());
        }

        public override void OnReady(bool wasLoaded) {
            BaseAchievements.OnChessPiecePlaced(S);
        }

        public override string ToString() {
            return " * ChessPiece";
        }
    }
}
