using System.Collections.Generic;
using UnityEngine;

namespace Game.Systems.UIUX {
    public static class GridLayoutOptimizer {
        public static int[] Optimize(float mainContainerWidth, Vector2 itemSize,
                float itemSpacing, float subContainerSpacing, List<int> itemCounts,
                bool[] injectRowSpacers = null) {
            int count = itemCounts.Count;
            int[] columns = new int[count];

            for (int i = 0; i < count; i++) {
                columns[i] = itemCounts[i];
            }

            while (CalculateTotalWidth(columns, itemSize.x, itemSpacing, subContainerSpacing) > mainContainerWidth) {
                int bestIndex = -1;
                int bestMaxRows = int.MaxValue;
                int bestMultiRowCount = int.MaxValue;
                int bestHighestMultiRowIndex = int.MaxValue;
                float bestFillRatio = float.MaxValue;

                for (int i = 0; i < count; i++) {
                    int minColumns = itemCounts[i] > 1 ? 2 : 1;
                    if (columns[i] <= minColumns) continue;

                    int originalColumns = columns[i];
                    columns[i] = originalColumns - 1;

                    int maxRows = CalculateMaxRows(itemCounts, columns, injectRowSpacers);
                    CountMultiRowContainers(itemCounts, columns, out int multiRowCount, out int highestMultiRowIndex, injectRowSpacers);
                    float fillRatio = CalculateLastRowFillRatio(itemCounts[i], columns[i]);

                    bool isBetter = false;
                    if (maxRows < bestMaxRows) {
                        isBetter = true;
                    } else if (maxRows == bestMaxRows) {
                        if (multiRowCount < bestMultiRowCount) {
                            isBetter = true;
                        } else if (multiRowCount == bestMultiRowCount) {
                            if (highestMultiRowIndex < bestHighestMultiRowIndex) {
                                isBetter = true;
                            } else if (highestMultiRowIndex == bestHighestMultiRowIndex) {
                                if (fillRatio > bestFillRatio) {
                                    isBetter = true;
                                }
                            }
                        }
                    }

                    if (isBetter) {
                        bestMaxRows = maxRows;
                        bestMultiRowCount = multiRowCount;
                        bestHighestMultiRowIndex = highestMultiRowIndex;
                        bestFillRatio = fillRatio;
                        bestIndex = i;
                    }

                    columns[i] = originalColumns;
                }

                if (bestIndex == -1) break;

                columns[bestIndex]--;
            }

            return columns;
        }

        // Rows when column 0 is reserved (group button row 1, spacer rows 2+).
        // Row 1 holds cols items; each subsequent row holds (cols-1) avatars + 1 spacer.
        public static int RowsWithSpacers(int itemCount, int columns) {
            if (columns <= 1) return itemCount;
            if (itemCount <= columns) return 1;
            return 1 + Mathf.CeilToInt((float)(itemCount - columns) / (columns - 1));
        }

        static float CalculateContainerWidth(int columns, float itemWidth, float itemSpacing) {
            if (columns <= 0) return 0f;
            return columns * itemWidth + (columns - 1) * itemSpacing;
        }

        static float CalculateTotalWidth(int[] columns, float itemWidth, float itemSpacing, float subContainerSpacing) {
            float totalWidth = 0f;
            int nonEmptyCount = 0;

            for (int i = 0; i < columns.Length; i++) {
                if (columns[i] > 0) {
                    totalWidth += CalculateContainerWidth(columns[i], itemWidth, itemSpacing);
                    nonEmptyCount++;
                }
            }

            if (nonEmptyCount > 1) {
                totalWidth += (nonEmptyCount - 1) * subContainerSpacing;
            }

            return totalWidth;
        }

        static int CalculateRows(int itemCount, int columns, bool spacers = false) {
            if (columns <= 0) return 0;
            if (spacers) return RowsWithSpacers(itemCount, columns);
            return Mathf.CeilToInt(itemCount / (float)columns);
        }

        static int CalculateMaxRows(List<int> itemCounts, int[] columns, bool[] injectRowSpacers = null) {
            int maxRows = 0;
            for (int i = 0; i < itemCounts.Count; i++) {
                bool spacers = injectRowSpacers != null && i < injectRowSpacers.Length && injectRowSpacers[i];
                int rows = CalculateRows(itemCounts[i], columns[i], spacers);
                if (rows > maxRows) maxRows = rows;
            }
            return maxRows;
        }

        static float CalculateLastRowFillRatio(int itemCount, int columns) {
            if (columns <= 0 || itemCount <= 0) return 1f;
            int remainder = itemCount % columns;
            if (remainder == 0) return 1f;
            return remainder / (float)columns;
        }

        static void CountMultiRowContainers(List<int> itemCounts, int[] columns,
                out int multiRowCount, out int highestMultiRowIndex, bool[] injectRowSpacers = null) {
            multiRowCount = 0;
            highestMultiRowIndex = -1;
            for (int i = 0; i < itemCounts.Count; i++) {
                bool spacers = injectRowSpacers != null && i < injectRowSpacers.Length && injectRowSpacers[i];
                int rows = CalculateRows(itemCounts[i], columns[i], spacers);
                if (rows > 1) {
                    multiRowCount++;
                    highestMultiRowIndex = i;
                }
            }
        }
    }
}
