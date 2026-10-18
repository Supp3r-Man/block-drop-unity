using System;

namespace BlockDrop.Core
{
    /// <summary>The playfield: 10 columns by 20 rows, row 0 at the top. Cells hold 0 (empty) or piece type + 1.</summary>
    public sealed class Board
    {
        public const int Width = 10;
        public const int Height = 20;

        /// <summary>Returned by DropY when the piece does not fit at the starting position.</summary>
        public const int NoFit = int.MinValue;

        private readonly int[] cells = new int[Width * Height];

        public int Get(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return 0;
            return cells[y * Width + x];
        }

        public void SetCell(int x, int y, int value)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) throw new ArgumentOutOfRangeException("x/y");
            cells[y * Width + x] = value;
        }

        /// <summary>Walls and floor block; the space above the top does not (pieces spawn and kick up there).</summary>
        public bool IsBlocked(int x, int y)
        {
            if (x < 0 || x >= Width || y >= Height) return true;
            if (y < 0) return false;
            return cells[y * Width + x] != 0;
        }

        public bool Fits(PieceType type, int rotation, int px, int py)
        {
            Cell[] shape = Shapes.Cells(type, rotation);
            for (int i = 0; i < shape.Length; i++)
                if (IsBlocked(px + shape[i].X, py + shape[i].Y)) return false;
            return true;
        }

        /// <summary>Row the piece comes to rest on if dropped straight down from (px, py); NoFit if it does not fit there.</summary>
        public int DropY(PieceType type, int rotation, int px, int py)
        {
            if (!Fits(type, rotation, px, py)) return NoFit;
            while (Fits(type, rotation, px, py + 1)) py++;
            return py;
        }

        /// <summary>Write a piece into the grid. Returns false (and writes nothing) if any cell is above the top.</summary>
        public bool Lock(PieceType type, int rotation, int px, int py)
        {
            Cell[] shape = Shapes.Cells(type, rotation);
            for (int i = 0; i < shape.Length; i++)
                if (py + shape[i].Y < 0) return false;
            for (int i = 0; i < shape.Length; i++)
                cells[(py + shape[i].Y) * Width + px + shape[i].X] = (int)type + 1;
            return true;
        }

        public bool IsRowFull(int y)
        {
            for (int x = 0; x < Width; x++)
                if (cells[y * Width + x] == 0) return false;
            return true;
        }

        /// <summary>Remove every full row and let the rows above fall. Returns how many rows were removed.</summary>
        public int ClearLines()
        {
            int cleared = 0;
            int write = Height - 1;
            for (int read = Height - 1; read >= 0; read--)
            {
                if (IsRowFull(read))
                {
                    cleared++;
                    continue;
                }
                if (write != read)
                    Array.Copy(cells, read * Width, cells, write * Width, Width);
                write--;
            }
            for (int y = write; y >= 0; y--)
                for (int x = 0; x < Width; x++) cells[y * Width + x] = 0;
            return cleared;
        }

        public int FilledCount()
        {
            int n = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != 0) n++;
            return n;
        }

        public bool IsEmpty
        {
            get { return FilledCount() == 0; }
        }

        /// <summary>Height of a column: rows from the floor to its highest filled cell (0 if empty).</summary>
        public int ColumnHeight(int x)
        {
            for (int y = 0; y < Height; y++)
                if (cells[y * Width + x] != 0) return Height - y;
            return 0;
        }

        /// <summary>Empty cells that have a filled cell somewhere above them in the same column.</summary>
        public int CountHoles()
        {
            int holes = 0;
            for (int x = 0; x < Width; x++)
            {
                bool seenFilled = false;
                for (int y = 0; y < Height; y++)
                {
                    if (cells[y * Width + x] != 0) seenFilled = true;
                    else if (seenFilled) holes++;
                }
            }
            return holes;
        }

        public Board Clone()
        {
            Board copy = new Board();
            Array.Copy(cells, copy.cells, cells.Length);
            return copy;
        }

        public void Clear()
        {
            Array.Clear(cells, 0, cells.Length);
        }
    }
}
