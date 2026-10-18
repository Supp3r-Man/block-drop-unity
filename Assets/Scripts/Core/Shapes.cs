// Plain C# on purpose: no UnityEngine in this folder, so the rules compile and test outside the editor.
namespace BlockDrop.Core
{
    public enum PieceType
    {
        I = 0,
        O = 1,
        T = 2,
        S = 3,
        Z = 4,
        J = 5,
        L = 6
    }

    /// <summary>A cell offset inside a piece's bounding box. X grows right, Y grows DOWN (row index).</summary>
    public struct Cell
    {
        public readonly int X;
        public readonly int Y;

        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// The seven shapes in four rotations each. Each piece lives in a square box (4x4 for I, 2x2 for O,
    /// 3x3 for the rest) and rotates clockwise about the box centre: (x, y) becomes (size - 1 - y, x).
    /// </summary>
    public static class Shapes
    {
        public const int PieceCount = 7;
        public const int Rotations = 4;

        // Spawn orientation as x,y pairs.
        private static readonly int[][] BaseCells =
        {
            new[] { 0, 1, 1, 1, 2, 1, 3, 1 }, // I
            new[] { 0, 0, 1, 0, 0, 1, 1, 1 }, // O
            new[] { 1, 0, 0, 1, 1, 1, 2, 1 }, // T
            new[] { 1, 0, 2, 0, 0, 1, 1, 1 }, // S
            new[] { 0, 0, 1, 0, 1, 1, 2, 1 }, // Z
            new[] { 0, 0, 0, 1, 1, 1, 2, 1 }, // J
            new[] { 2, 0, 0, 1, 1, 1, 2, 1 }  // L
        };

        private static readonly int[] Sizes = { 4, 2, 3, 3, 3, 3, 3 };

        private static readonly Cell[][][] Table = Build();

        private static Cell[][][] Build()
        {
            Cell[][][] table = new Cell[PieceCount][][];
            for (int t = 0; t < PieceCount; t++)
            {
                table[t] = new Cell[Rotations][];
                int size = Sizes[t];
                int[] current = (int[])BaseCells[t].Clone();
                for (int r = 0; r < Rotations; r++)
                {
                    Cell[] cells = new Cell[4];
                    for (int i = 0; i < 4; i++) cells[i] = new Cell(current[i * 2], current[i * 2 + 1]);
                    table[t][r] = cells;

                    int[] rotated = new int[8];
                    for (int i = 0; i < 4; i++)
                    {
                        int x = current[i * 2];
                        int y = current[i * 2 + 1];
                        rotated[i * 2] = size - 1 - y;
                        rotated[i * 2 + 1] = x;
                    }
                    current = rotated;
                }
            }
            return table;
        }

        /// <summary>Side of the square bounding box.</summary>
        public static int Size(PieceType type)
        {
            return Sizes[(int)type];
        }

        /// <summary>The four cells for a rotation (any integer; wraps mod 4). The array is shared: do not modify it.</summary>
        public static Cell[] Cells(PieceType type, int rotation)
        {
            int r = ((rotation % Rotations) + Rotations) % Rotations;
            return Table[(int)type][r];
        }
    }
}
