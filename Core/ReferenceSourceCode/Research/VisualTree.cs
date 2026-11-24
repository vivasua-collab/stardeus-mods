using System.Collections.Generic;
using System.Linq;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Research {
    public class VisualTree {

        private List<VisualNode> nodes;
        private Dictionary<string, VisualNode> nodesById;
        private List<VisualEdge> edges;

        private int Width => nodes.Max(n => n.X);
        private int Height => nodes.Max(n => n.Y);

        public void Build() {
            LoadNodes();
            CreateEdges();
            PositionX();
            NormalizeEdges();
            Collapse();
            MinimizeCrossings();
            MinimizeEdgeLength();
            RemoveEmptyRows();
        }

        private void LoadNodes() {
            nodes = ResearchTree.All.Select(
                n => new VisualNode(n)).ToList();
            nodesById = new();
            foreach (var n in nodes) {
                nodesById[n.Id] = n;
            }
        }

        private void CreateEdges() {
            edges = new();
            foreach (var node in nodes) {
                if (Researchable.IsEmptyPrereq(node.R)) { continue; }
                foreach (var prereq in node.R.Prerequisites) {
                    var prereqN = nodesById.Get(prereq, null);
                    var edge = new VisualEdge(prereqN, node);
                    prereqN.EdgesOut.Add(edge);
                    node.EdgesIn.Add(edge);
                    if (!edge.IsValid) {
                        D.Err("Invalid edge, most likely prerequisite is missing: {0}, {1}",
                            prereq, edge);
                        continue;
                    }
                }
            }
        }

        private void PositionX() {
            bool changed = true;
            var i = 1;
            var maxI = 50;

            while (changed && i++ < maxI) {
                var min = 1;
                changed = false;

                foreach ( var node in nodes ) {
                    if (node.SetDepth(min)) { changed = true; }
                }

                min = nodes.Max( n => n.X ) + 1;
            }
        }

        private void NormalizeEdges() {
            foreach (var edge in edges) {
                if (edge.Span < 2) { continue; }
                // remove and decouple long edge
                edges.Remove(edge);
                edge.In.EdgesOut.Remove(edge);
                edge.Out.EdgesIn.Remove(edge);

                var cur = edge.In;
                var yOffset = Mathf.CeilToInt(
                    (edge.Out.Y - edge.In.Y) / (float) edge.Span);

                // create and hook up link chain
                for ( var x = edge.In.X + 1; x < edge.Out.X; x++ ) {
                    var link = new VisualNode();
                    link.X  = x;
                    link.Y = edge.In.Y + yOffset * ( x - edge.In.X );
                    var linkEdge = new VisualEdge(cur, link);
                    cur.EdgesOut.Add(linkEdge);
                    link.EdgesIn.Add(linkEdge);
                    nodes.Add(link);
                    edges.Add(linkEdge);
                    cur = link;
                }

                // hook up final link to out node
                var finalEdge = new VisualEdge(cur, edge.Out);
                cur.EdgesOut.Add(finalEdge);
                edge.Out.EdgesIn.Add(finalEdge);
                edges.Add(finalEdge);
            }
        }

        private void Collapse() {
            for (var l = 1; l <= Width; l++) {
                var nodes = Layer(l, true);
                var Y = 1;
                foreach (var node in nodes) {
                    node.Y = Y++;
                }
            }
        }

        private void MinimizeCrossings() {
            // initialize each layer by putting nodes with the most (recursive!) children on bottom
            for (var X = 1; X <= Width; X++) {
                var nodes = Layer(X).OrderBy(n => n.Descendants.Count).ToList();
                for (var i = 0; i < nodes.Count; i++) {
                    nodes[i].Y = i + 1;
                }
            }

            // up-down sweeps of mean reordering
            var progress = false;
            int iteration = 0, burnout = 2, maxIter = 50;
            while ((!progress || burnout > 0) && iteration < maxIter) {
                progress = BarymetricSweep(iteration++);
                if (!progress) { burnout--; }
            }

            // greedy sweep for local optima
            iteration = 0;
            burnout = 2;
            while (burnout > 0 && iteration < maxIter) {
                progress = GreedySweep(iteration++);
                if (!progress) { burnout--; }
            }
        }

        private void MinimizeEdgeLength() {
            // move and/or swap nodes to reduce the total edge length
            // perform sweeps of adjacent node reorderings
            var progress = false;
            int iteration = 0, burnout = 2, max_iterations = 50;
            while ((!progress || burnout > 0) && iteration < max_iterations) {
                progress = EdgeLengthSweep_Local(iteration++);
                if (!progress) { burnout--; }
            }

            // sweep until we had no progress 2 times, then keep sweeping until we had progress
            iteration = 0;
            burnout = 2;
            while (burnout > 0 && iteration < max_iterations) {
                progress = EdgeLengthSweep_Global(iteration++);
                if (!progress) { burnout--; }
            }
        }

        private bool EdgeLengthSweep_Global(int iteration) {
            // calculate edge length before sweep
            var before = EdgeLength();

            // do left/right sweep, align with left/right nodes for 4 different iterations.
            //if (iteration % 2 == 0)
            for (var l = 2; l <= Width; l++) {
                EdgeLengthSweep_Global_Layer(l, true);
            }
            //else
            //    for (var l = 1; l < Size.x; l++)
            //        EdgeLengthSweep_Global_Layer(l, false);

            // calculate edge length after sweep
            var after = EdgeLength();

            // return progress
            return after < before;
        }

        private bool EdgeLengthSweep_Local(int iteration) {
            // calculate edge length before sweep
            var before = EdgeLength();

            // do left/right sweep, align with left/right nodes for 4 different iterations.
            if (iteration % 2 == 0)
                for (var l = 2; l <= Width; l++)
                    EdgeLengthSweep_Local_Layer(l, true);
            else
                for (var l = Width - 1; l >= 0; l--)
                    EdgeLengthSweep_Local_Layer(l, false);

            // calculate edge length after sweep
            var after = EdgeLength();

            // return progress
            return after < before;
        }

        private void EdgeLengthSweep_Global_Layer(int l, bool @in) {
            // The objective here is to;
            // (1) move and/or swap nodes to reduce total edge length
            // (2) not increase the number of crossings

            var length = EdgeLength(l, @in);
            var crossings = Crossings(l);
            if (System.Math.Abs(length) < 0.00000001f) { return; }

            var layer = Layer(l, true);
            foreach (var node in layer) {
                // we only need to loop over positions that might be better for this node.
                // min = minimum of current position, minimum of any connected nodes current position
                var neighbours = node.Nodes;
                if (!neighbours.Any()) { continue; }

                var min = Mathf.Min(node.Y, neighbours.Min(n => n.Y));
                var max = Mathf.Max(node.Y, neighbours.Max(n => n.Y));
                if (min == max && min == node.Y) { continue; }

                for (var y = min; y <= max; y++) {
                    if (y == node.Y) { continue; }

                    // is this spot occupied?
                    var otherNode = NodeAt(l, y);

                    // occupied, try swapping
                    if (otherNode != null) {
                        Swap(node, otherNode);
                        var candidateCrossings = Crossings(l);
                        if (candidateCrossings > crossings) {
                            // abort
                            Swap(otherNode, node);
                        } else {
                            var candidateLength = EdgeLength(l, @in);
                            if (length - candidateLength < 0.0000001f) {
                                // abort
                                Swap(otherNode, node);
                            } else {
                                length = candidateLength;
                            }
                        }
                    // not occupied, try moving
                    } else {
                        var oldY = node.Y;
                        node.Y = y;
                        var candidateCrossings = Crossings(l);
                        if (candidateCrossings > crossings) {
                            // abort
                            node.Y = oldY;
                        } else {
                            var candidateLength = EdgeLength(l, @in);
                            if (length - candidateLength < 0.00000001f) {
                                // abort
                                node.Y = oldY;
                            } else {
                                length = candidateLength;
                            }
                        }
                    }
                }
            }
        }

        private float EdgeLength() {
            var length = 0f;
            for (var layer = 1; layer < Width; layer++) {
                length += EdgeLength(layer, true);
            }
            return length;
        }

        private float EdgeLength(int layer) {
            if (layer == 0) {
                return EdgeLength(layer, false);
            }
            if (layer == Width) {
                return EdgeLength(layer, true);
            }
            return EdgeLength(layer, true) *
                   EdgeLength(layer, false); // multply to favor moving nodes closer to one endpoint
        }

        private float EdgeLength( int layer, bool @in ) {
            // get in/out edges for layer
            var edges = Layer(layer)
                       .SelectMany( n => @in ? n.EdgesIn : n.EdgesOut)
                       .OrderBy(e => e.In.Y)
                       .ThenBy(e => e.Out.Y)
                       .ToList();

            if (Arrays.IsEmpty(edges)) { return 0f; }

            return edges.Sum(e => e.Length) * (@in ? 2 : 1);
        }

        private void EdgeLengthSweep_Local_Layer(int l, bool @in) {
            // The objective here is to;
            // (1) move and/or swap nodes to reduce local edge length
            // (2) not increase the number of crossings
            var x = @in ? l - 1 : l + 1;
            var crossings = Crossings(x);

            var layer = Layer(l, true);
            foreach (var node in layer) {
                foreach (var edge in @in ? node.EdgesIn : node.EdgesOut) {
                    // current length
                    var length = edge.Length;
                    var neighbour = @in ? edge.In : edge.Out;
                    if (neighbour.X != x) {
                        // D.Warn("{0} is not at layer {1}", neighbour, x);
                    }

                    // we only need to loop over positions that might be better for this node.
                    // min = minimum of current position, node position
                    var min = Mathf.Min(node.Y, neighbour.Y);
                    var max = Mathf.Max(node.Y, neighbour.Y);

                    // already at only possible position
                    if (min == max && min == node.Y) { continue; }

                    for (var y = min; y <= max; y++) {
                        if (y == neighbour.Y) { continue; }

                        // is this spot occupied?
                        var otherNode = NodeAt(x, y);

                        // occupied, try swapping
                        if (otherNode != null) {
                            Swap(neighbour, otherNode);
                            var candidateCrossings = Crossings(x);
                            if (candidateCrossings > crossings) {
                                // abort
                                Swap(otherNode, neighbour);
                            } else {
                                var candidateLength = edge.Length;
                                if (length - candidateLength < 0.000000001f) {
                                    // abort
                                    Swap(otherNode, neighbour);
                                } else {
                                    length = candidateLength;
                                }
                            }
                        // not occupied, try moving
                        } else {
                            var oldY = neighbour.Y;
                            neighbour.Y = y;
                            var candidateCrossings = Crossings(x);
                            if (candidateCrossings > crossings) {
                                // abort
                                neighbour.Y = oldY;
                            } else {
                                var candidateLength = edge.Length;
                                if (length - candidateLength < 0.0000000001f) {
                                    // abort
                                    neighbour.Y = oldY;
                                } else {
                                    length = candidateLength;
                                }
                            }
                        }
                    }
                }
            }
        }

        private bool BarymetricSweep(int iteration) {
            // count number of crossings before sweep
            var before = Crossings();

            // do up/down sweep on alternating iterations
            if (iteration % 2 == 0) {
                for (var i = 2; i <= Width; i++) {
                    BarymetricSweep_Layer(i, true);
                }
            } else {
                for (var i = Width - 1; i > 0; i--) {
                    BarymetricSweep_Layer(i, false);
                }
            }

            // count number of crossings after sweep
            var after = Crossings();
            return after < before;
        }

        private bool GreedySweep(int iteration) {
            // count number of crossings before sweep
            var before = Crossings();

            // do up/down sweep on aternating iterations
            if (iteration % 2 == 0) {
                for (var l = 1; l <= Width; l++) {
                    GreedySweep_Layer(l);
                }
            } else {
                for (var l = Width; l >= 1; l--) {
                    GreedySweep_Layer(l);
                }
            }

            // count number of crossings after sweep
            var after = Crossings();

            // return progress
            return after < before;
        }

        private VisualNode NodeAt(int X, int Y) {
            return nodes.FirstOrDefault(n => n.X == X && n.Y == Y);
        }

        private void GreedySweep_Layer(int l) {
            // The objective here is twofold;
            // 1: Swap nodes to reduce the number of crossings
            // 2: Swap nodes so that inner edges (edges between dummies)
            //    avoid crossings at all costs.
            //
            // If I'm reasoning this out right, both objectives should be served by
            // minimizing the amount of crossings between each pair of nodes.
            var crossings = Crossings(l);
            if (crossings == 0) { return; }

            var layer = Layer(l, true);
            for (var i = 0; i < layer.Count - 1; i++) {
                for (var j = i + 1; j < layer.Count; j++) {
                    // swap, then count crossings again. If lower, leave it. If higher, revert.
                    Swap(layer[i], layer[j]);
                    var candidateCrossings = Crossings(l);
                    if (candidateCrossings < crossings) {
                        // update current crossings
                        crossings = candidateCrossings;
                    } else {
                        // revert change
                        Swap(layer[j], layer[i]);
                    }
                }
            }
        }

        private void BarymetricSweep_Layer(int layer, bool left) {
            var means = Layer(layer)
                .ToDictionary(n => n, n => GetBarycentre(n, left ? n.NodesIn : n.NodesOut))
                .OrderBy(n => n.Value);

            // create groups of nodes at similar means
            var cur = float.MinValue;
            var groups = new Dictionary<float, List<VisualNode>>();
            foreach (var mean in means) {
                if (System.Math.Abs(mean.Value - cur) > 0.00000001f) {
                    cur = mean.Value;
                    groups[cur] = new();
                }

                groups[cur].Add(mean.Key);
            }

            // position nodes as close to their desired mean as possible
            var Y = 1;
            foreach (var group in groups) {
                var mean = group.Key;
                var N = group.Value.Count;
                Y = (int) Mathf.Max(Y, mean - (N - 1) / 2);

                foreach (var node in group.Value) {
                    node.Y = Y++;
                }
            }
        }

        private float GetBarycentre(VisualNode node, List<VisualNode> neighbours) {
            if (Arrays.IsEmpty(neighbours)) {
                return node.Y;
            }

            return neighbours.Sum(n => n.Y) / (float) neighbours.Count;
        }


        private void Swap(VisualNode A, VisualNode B) {
            if (A.X != B.X) {
                throw new("Can't swap nodes on different layers");
            }

            // swap Y positions of adjacent nodes
            var tmp = A.Y;
            A.Y = B.Y;
            B.Y = tmp;
        }

        private int Crossings(int layer) {
            if (layer == 0) { return Crossings(layer, false); }
            if (layer == Width) { return Crossings(layer, true); }
            return Crossings(layer, true) + Crossings(layer, false);
        }

        private int Crossings() {
            var crossings = 0;
            for (var layer = 1; layer < Width; layer++) {
                crossings += Crossings(layer, true);
            }
            return crossings;
        }

        private int Crossings(int layer, bool isIn) {
            // get in/out edges for layer
            var edges = Layer(layer)
                .SelectMany(n => isIn ? n.EdgesIn : n.EdgesOut)
                .OrderBy(e => e.In.Y)
                .ThenBy(e => e.Out.Y)
                .ToList();

            if ( edges.Count < 2 ) { return 0; }

            // count number of inversions
            var inversions = 0;
            for (var i = 0; i < edges.Count - 1; i++) {
                for (var j = i + 1; j < edges.Count; j++) {
                    if (edges[j].Out.Y < edges[i].Out.Y) {
                        inversions++;
                    }
                }
            }

            return inversions;
        }


        public List<VisualNode> Layer(int depth, bool ordered = false) {
            if (ordered) {
                nodes = nodes.OrderBy(n => n.X).ThenBy(n => n.Y).ToList();
            }

            return nodes.Where(n => n.X == depth).ToList();
        }

        private void RemoveEmptyRows() {
            var y = 1;
            while (y <= Height) {
                var row = Row(y);
                if (Arrays.IsEmpty(row)) {
                    foreach (var node in nodes.Where(n => n.Y > y)) {
                        node.Y--;
                    }
                } else {
                    y++;
                }
            }
        }

        public List<VisualNode> Row(int Y) {
            return nodes.Where(n => n.Y == Y).ToList();
        }
    }
}
