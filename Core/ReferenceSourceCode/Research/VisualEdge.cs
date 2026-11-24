using UnityEngine;

namespace Game.Research {
    public class VisualEdge {
        public VisualNode In;
        public VisualNode Out;
        public bool IsLink;

        public VisualEdge(VisualNode nodeIn, VisualNode nodeOut) {
            this.In = nodeIn;
            this.Out = nodeOut;
            IsLink = nodeIn.IsLink || nodeOut.IsLink;
        }

        public float Length  => System.Math.Abs(In.Y - Out.Y) * ( IsLink ? 10 : 1 );

        public int Span => Out.X - In.Y;

        public bool IsValid => In != null && Out != null && In != Out;  
    }
}
