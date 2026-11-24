namespace Game.Procgen {
    public interface IShapeEvaluator {
        void SetBounds(float maxX, float maxY);
        float Evaluate(float x);
        float AdjustedX(float x);
        float AdjustedY(float y);
        bool Evaluate(float x, float y);
    }
}
