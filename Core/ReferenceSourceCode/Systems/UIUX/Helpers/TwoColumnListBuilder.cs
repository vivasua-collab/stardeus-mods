using System.Collections.Generic;
using System.Linq;
using UnityEngine.Pool;

namespace Game.Systems.UIUX {
    public class TwoColumnListBuilder<TItem> {
        public class Node {
            public TwoColumnNodeKind Type;
            public string Label;
            public TItem Item;
        }

        public List<Node> Build(Dictionary<string, List<TItem>> groups) {
            var blocks = groups
                .OrderBy(g => g.Key)
                .Select(g => new Block { Label = g.Key, Items = g.Value })
                .ToList();

            // todo use ListPool<Node>.Get()

            var left = new List<Node>();
            var right = new List<Node>();

            bool leftEmpty = true;
            bool rightEmpty = true;

            int leftHeight = 0;
            int rightHeight = 0;

            foreach (var b in blocks) {
                int blockSize = b.Size;

                bool putLeft = leftHeight <= rightHeight;

                if (putLeft) {
                    AddBlock(left, b, !leftEmpty);
                    leftEmpty = false;
                    leftHeight += blockSize + (!leftEmpty ? 1 : 0);
                } else {
                    AddBlock(right, b, !rightEmpty);
                    rightEmpty = false;
                    rightHeight += blockSize + (!rightEmpty ? 1 : 0);
                }
            }

            EnsureLeftIsTallest(left, right);

            return Interleave(left, right);
        }

        class Block {
            public string Label;
            public List<TItem> Items;
            public int Size => 1 + Items.Count;
        }

        void AddBlock(List<Node> col, Block b, bool needsPlaceholder) {
            if (needsPlaceholder)
                col.Add(new Node { Type = TwoColumnNodeKind.Placeholder });

            col.Add(new Node { Type = TwoColumnNodeKind.Label, Label = b.Label });

            foreach (var item in b.Items)
                col.Add(new Node { Type = TwoColumnNodeKind.Item, Item = item });
        }

        void EnsureLeftIsTallest(List<Node> left, List<Node> right) {
            if (right.Count <= left.Count)
                return;

            int diff = right.Count - left.Count;

            for (int i = 0; i < diff; i++)
                left.Add(new Node { Type = TwoColumnNodeKind.Placeholder });
        }

        List<Node> Interleave(List<Node> left, List<Node> right) {
            int rows = left.Count;

            var merged = new List<Node>(rows * 2);

            for (int i = 0; i < rows; i++) {
                merged.Add(left[i]);
                merged.Add(i < right.Count ? right[i] : new Node {
                    Type = TwoColumnNodeKind.Placeholder
                });
            }

            return merged;
        }
    }

}
