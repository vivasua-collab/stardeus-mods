using System.Collections.Generic;
using System.Linq;
using Game.Data;
using UnityEngine;

namespace Game.Research {
    public class VisualNode {
        private Def researchable;
        public string Id => researchable.Id;
        public Researchable R => researchable.Researchable;

        public List<VisualEdge> EdgesIn;
        public List<VisualEdge> EdgesOut;

        public List<VisualNode> NodesIn => EdgesIn.Select(e => e.In).ToList();
        public List<VisualNode> NodesOut => EdgesOut.Select(e => e.Out).ToList();
        public List<VisualNode> Nodes => NodesIn.Concat(NodesOut).ToList();

        public List<VisualNode> Descendants => NodesOut.Concat(
            NodesOut.SelectMany(n => n.Descendants)).ToList(); 

        public bool IsLink;

        public int X;
        public int Y;
        public Vector2 Pos => new(X, Y);

        public VisualNode(Def r = null) {
            IsLink = r == null;
            researchable = r;
            EdgesIn = new();
            EdgesOut = new();
        }

        public bool SetDepth(int min) {
            var isRoot = NodesIn.Count == 0;
            var desired = isRoot ? 1 : NodesIn.Max(n => n.X) + 1;
            var depth = Mathf.Max(desired, min);

            if ( depth == X ) { return false; }

            X = depth;
            return true;
        }
    }
}
