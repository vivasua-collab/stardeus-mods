using System.Collections.Generic;
using Game.Data;
using KL.Utils;
using Microsoft.Msagl.Core.Layout;
using UnityEngine;

namespace Game.UI.Research {
    public class ResearchTreeNode {
        public bool IsEmpty => Def == null;
        public string Id => Def.Id;
        public Vector2 Pos;
        public readonly int NodeSize = 1;
        public readonly float SiblingDist = 0;
        public readonly float TreeDist = 0;
        public float Mod;
        public Def Def;
        public GeometryGraph Graph;
        public Node GraphNode;
        public Node TierNode;
        // Set on a category root only, in layout units: its category's width, and how far
        // right of the root the category's middle is (the category title spans the category)
        public float LaneWidth;
        public float LaneCenterOffset;
        public string[] Deps => Def.Researchable.Prerequisites;
        public ResearchTreeNode Parent;
        public readonly List<ResearchTreeNode> Children = new();

        public bool IsLeaf => Children.Count == 0;
        public bool IsLeftMost => Parent != null && Parent.Children[0] == this;
        public bool IsRightMost => Parent != null && Parent.Children[^1] == this;

        public ResearchTreeNode SiblingPrev {
            get {
                if (Parent == null || IsLeftMost) { return null; }
                return Parent.Children[Parent.Children.IndexOf(this) - 1];
            }
        }

        public ResearchTreeNode SiblingNext {
            get {
                if (Parent == null || IsRightMost) { return null; }
                return Parent.Children[Parent.Children.IndexOf(this) + 1];
            }
        }

        public ResearchTreeNode SiblingLeftMost {
            get {
                if (Parent == null) { return null; }
                if (IsLeftMost) { return this; }
                return Parent.Children[0];
            }
        }
        public ResearchTreeNode ChildLeftMost {
            get {
                if (Children.Count == 0) { return null; }
                return Children[0];
            }
        }

        public ResearchTreeNode ChildRightMost {
            get {
                if (Children.Count == 0) { return null; }
                return Children[Children.Count - 1];
            }
        }

        public bool IsSubChild(ResearchTreeNode n) {
            if (Children.Contains(n)) {
                return true;
            }
            foreach (var child in Children) {
                if (child.IsSubChild(n)) {
                    return true;
                }
            }
            return false;
        }

        public override string ToString() {
            return $"{Id} {Pos}";
        }
    }
}
