// Dependency-free test runner for the engine-free game rules (Assets/Scripts/Core).
// Exit code 0 = all passed, 1 = at least one failed. Pass --stats to print the bot measurements quoted in the README.
using System;
using System.Collections.Generic;
using System.Text;
using BlockDrop.Core;

namespace LogicTests
{
    public static class Program
    {
        private static int passed;
        private static int failed;

        private static void Run(string name, Action body)
        {
            try
            {
                body();
                passed++;
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + e.Message);
            }
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(what + ": expected " + expected + " but got " + actual);
        }

        private static void Near(float expected, float actual, float eps, string what)
        {
            if (Math.Abs(expected - actual) > eps)
                throw new Exception(what + ": expected " + expected + " but got " + actual);
        }

        // ---------------- helpers ----------------

        private static string Key(Cell[] cells)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < cells.Length; i++) parts.Add(cells[i].X + "," + cells[i].Y);
            parts.Sort();
            return string.Join(";", parts.ToArray());
        }

        /// <summary>Fill row y completely except the listed columns.</summary>
        private static void FillRow(Board b, int y, params int[] except)
        {
            for (int x = 0; x < Board.Width; x++)
            {
                if (Array.IndexOf(except, x) >= 0) continue;
                b.SetCell(x, y, 1);
            }
        }

        private static int RowCount(Board b, int y)
        {
            int n = 0;
            for (int x = 0; x < Board.Width; x++) if (b.Get(x, y) != 0) n++;
            return n;
        }

        private static bool Fits(BlockGame g)
        {
            return g.Board.Fits(g.Current.Type, g.Current.Rotation, g.Current.X, g.Current.Y);
        }

        private static int TotalPieceCells(BlockGame g)
        {
            return 4 * g.LockCount - 10 * g.Lines;
        }

        private static bool HasFullRow(Board b)
        {
            for (int y = 0; y < Board.Height; y++) if (b.IsRowFull(y)) return true;
            return false;
        }

        /// <summary>Greedy bot: tries every rotation and column, scores the result (height, holes, bumpiness, lines), drops the best.</summary>
        private static void BotPlaceBest(BlockGame g)
        {
            PieceType t = g.Current.Type;
            double best = double.MinValue;
            int bestRot = g.Current.Rotation;
            int bestX = g.Current.X;
            for (int rot = 0; rot < 4; rot++)
            {
                for (int x = -2; x <= Board.Width; x++)
                {
                    int y = g.Board.DropY(t, rot, x, 0);
                    if (y == Board.NoFit) continue;
                    Board b = g.Board.Clone();
                    if (!b.Lock(t, rot, x, y)) continue;
                    int lines = b.ClearLines();
                    int agg = 0, bump = 0;
                    for (int c = 0; c < Board.Width; c++)
                    {
                        agg += b.ColumnHeight(c);
                        if (c > 0) bump += Math.Abs(b.ColumnHeight(c) - b.ColumnHeight(c - 1));
                    }
                    double score = -0.51 * agg + 0.76 * lines - 0.36 * b.CountHoles() - 0.18 * bump;
                    if (score > best)
                    {
                        best = score;
                        bestRot = rot;
                        bestX = x;
                    }
                }
            }
            g.ForceCurrent(t, bestRot, bestX, 0);
            g.HardDrop();
        }

        private static BlockGame NewGame()
        {
            return new BlockGame(1);
        }

        public static int Main(string[] args)
        {
            bool stats = args != null && Array.IndexOf(args, "--stats") >= 0;

            // ---- Rng ----
            Run("rng same seed same sequence", delegate
            {
                Rng a = new Rng(42), b = new Rng(42);
                for (int i = 0; i < 100; i++) Eq(a.NextUInt(), b.NextUInt(), "value " + i);
            });
            Run("rng different seeds differ", delegate
            {
                Rng a = new Rng(1), b = new Rng(2);
                bool differ = false;
                for (int i = 0; i < 10; i++) if (a.NextUInt() != b.NextUInt()) differ = true;
                True(differ, "sequences identical");
            });
            Run("rng survives the one seed that mixes to a zero state", delegate
            {
                // state = seed * K + C (mod 2^32) is zero for exactly one seed; solve it with the modular inverse of K.
                uint k = 2654435761u, c = 0x9E3779B9u, inv = k;
                for (int i = 0; i < 6; i++) inv = inv * (2u - k * inv);
                uint badSeed = (0u - c) * inv;
                True(badSeed * k + c == 0u, "test setup: seed does not mix to zero");
                Rng r = new Rng((int)badSeed);
                uint a = r.NextUInt(), b = r.NextUInt();
                True(a != 0u && b != 0u && a != b, "generator is stuck at zero");
            });
            Run("rng int range covers all values and stays in bounds", delegate
            {
                Rng r = new Rng(9);
                bool[] seen = new bool[7];
                for (int i = 0; i < 1000; i++)
                {
                    int v = r.Range(0, 7);
                    True(v >= 0 && v < 7, "out of range " + v);
                    seen[v] = true;
                }
                for (int i = 0; i < 7; i++) True(seen[i], "never produced " + i);
            });
            Run("rng float is in [0,1) and roughly uniform", delegate
            {
                Rng r = new Rng(10);
                double sum = 0;
                int n = 20000;
                for (int i = 0; i < n; i++)
                {
                    float f = r.NextFloat();
                    True(f >= 0f && f < 1f, "out of range " + f);
                    sum += f;
                }
                Near(0.5f, (float)(sum / n), 0.02f, "mean");
            });

            // ---- Shapes ----
            Run("every piece has four distinct cells inside its box in every rotation", delegate
            {
                for (int t = 0; t < Shapes.PieceCount; t++)
                {
                    PieceType type = (PieceType)t;
                    int size = Shapes.Size(type);
                    for (int r = 0; r < 4; r++)
                    {
                        Cell[] cells = Shapes.Cells(type, r);
                        Eq(4, cells.Length, type + " cell count");
                        HashSet<string> seen = new HashSet<string>();
                        for (int i = 0; i < 4; i++)
                        {
                            True(cells[i].X >= 0 && cells[i].X < size && cells[i].Y >= 0 && cells[i].Y < size,
                                type + " rot " + r + " cell outside box");
                            seen.Add(cells[i].X + "," + cells[i].Y);
                        }
                        Eq(4, seen.Count, type + " rot " + r + " distinct cells");
                    }
                }
            });
            Run("rotation index wraps modulo four", delegate
            {
                for (int t = 0; t < Shapes.PieceCount; t++)
                {
                    PieceType type = (PieceType)t;
                    Eq(Key(Shapes.Cells(type, 3)), Key(Shapes.Cells(type, -1)), type + " -1");
                    Eq(Key(Shapes.Cells(type, 1)), Key(Shapes.Cells(type, 5)), type + " 5");
                    Eq(Key(Shapes.Cells(type, 0)), Key(Shapes.Cells(type, 8)), type + " 8");
                }
            });
            Run("the seven pieces are all different shapes", delegate
            {
                HashSet<string> keys = new HashSet<string>();
                for (int t = 0; t < Shapes.PieceCount; t++) keys.Add(Key(Shapes.Cells((PieceType)t, 0)));
                Eq(7, keys.Count, "distinct spawn shapes");
            });
            Run("O never changes shape when rotated", delegate
            {
                string k0 = Key(Shapes.Cells(PieceType.O, 0));
                for (int r = 1; r < 4; r++) Eq(k0, Key(Shapes.Cells(PieceType.O, r)), "O rot " + r);
            });
            Run("I spawns horizontal and rotates to a vertical bar", delegate
            {
                Eq("0,1;1,1;2,1;3,1", Key(Shapes.Cells(PieceType.I, 0)), "I rot 0");
                Eq("2,0;2,1;2,2;2,3", Key(Shapes.Cells(PieceType.I, 1)), "I rot 1");
            });
            Run("T rotates clockwise: pointing up, then right", delegate
            {
                Eq("0,1;1,0;1,1;2,1", Key(Shapes.Cells(PieceType.T, 0)), "T rot 0");
                Eq("1,0;1,1;1,2;2,1", Key(Shapes.Cells(PieceType.T, 1)), "T rot 1");
            });
            Run("S turned half way round is the 180-degree image", delegate
            {
                Eq("0,2;1,1;1,2;2,1", Key(Shapes.Cells(PieceType.S, 2)), "S rot 2");
            });
            Run("J and L are mirror images at spawn", delegate
            {
                Cell[] j = Shapes.Cells(PieceType.J, 0);
                List<string> mirrored = new List<string>();
                for (int i = 0; i < 4; i++) mirrored.Add((2 - j[i].X) + "," + j[i].Y);
                mirrored.Sort();
                Eq(Key(Shapes.Cells(PieceType.L, 0)), string.Join(";", mirrored.ToArray()), "L = mirrored J");
            });
            Run("S and Z are mirror images at spawn", delegate
            {
                Cell[] s = Shapes.Cells(PieceType.S, 0);
                List<string> mirrored = new List<string>();
                for (int i = 0; i < 4; i++) mirrored.Add((2 - s[i].X) + "," + s[i].Y);
                mirrored.Sort();
                Eq(Key(Shapes.Cells(PieceType.Z, 0)), string.Join(";", mirrored.ToArray()), "Z = mirrored S");
            });

            // ---- Board ----
            Run("board starts empty with open space above and blocking walls and floor", delegate
            {
                Board b = new Board();
                True(b.IsEmpty, "not empty");
                True(!b.IsBlocked(0, 0) && !b.IsBlocked(9, 19), "inside cells blocked");
                True(b.IsBlocked(-1, 5), "left wall");
                True(b.IsBlocked(Board.Width, 5), "right wall");
                True(b.IsBlocked(3, Board.Height), "floor");
                True(!b.IsBlocked(3, -4), "space above the top should be free");
            });
            Run("board cells block once filled", delegate
            {
                Board b = new Board();
                b.SetCell(3, 7, 4);
                True(b.IsBlocked(3, 7), "filled cell not blocking");
                Eq(4, b.Get(3, 7), "value");
                Eq(0, b.Get(-1, 0), "out of range reads empty");
            });
            Run("fits respects walls, floor and stack", delegate
            {
                Board b = new Board();
                True(b.Fits(PieceType.O, 0, 0, 0), "O at left wall");
                True(!b.Fits(PieceType.O, 0, -1, 0), "O through left wall");
                True(!b.Fits(PieceType.O, 0, 9, 0), "O through right wall");
                True(b.Fits(PieceType.O, 0, 4, 18), "O on floor");
                True(!b.Fits(PieceType.O, 0, 4, 19), "O through floor");
                b.SetCell(4, 10, 1);
                True(!b.Fits(PieceType.O, 0, 4, 9), "O onto a block");
            });
            Run("drop lands on the floor and on a stack", delegate
            {
                Board b = new Board();
                Eq(18, b.DropY(PieceType.O, 0, 4, 0), "O on floor");
                b.SetCell(4, 19, 1);
                b.SetCell(5, 19, 1);
                Eq(17, b.DropY(PieceType.O, 0, 4, 0), "O on stack");
            });
            Run("drop reports NoFit when the start position is blocked", delegate
            {
                Board b = new Board();
                b.SetCell(4, 0, 1);
                Eq(Board.NoFit, b.DropY(PieceType.O, 0, 4, 0), "blocked start");
            });
            Run("lock writes the piece type plus one", delegate
            {
                Board b = new Board();
                True(b.Lock(PieceType.T, 0, 3, 10), "lock failed");
                Eq(4, b.FilledCount(), "cells written");
                Eq((int)PieceType.T + 1, b.Get(4, 10), "top of the T");
            });
            Run("lock refuses a piece sticking out of the top and writes nothing", delegate
            {
                Board b = new Board();
                True(!b.Lock(PieceType.I, 1, 3, -1), "lock above the top succeeded");
                True(b.IsEmpty, "cells were written anyway");
            });
            Run("clear removes a full row and leaves partial rows alone", delegate
            {
                Board b = new Board();
                FillRow(b, 19);
                FillRow(b, 18, 3);
                Eq(1, b.ClearLines(), "cleared");
                Eq(9, RowCount(b, 19), "partial row fell into place");
                Eq(0, b.Get(3, 19), "gap preserved");
            });
            Run("clear with nothing full does nothing", delegate
            {
                Board b = new Board();
                FillRow(b, 19, 0);
                Eq(0, b.ClearLines(), "cleared");
                Eq(9, b.FilledCount(), "cells");
            });
            Run("non-adjacent full rows clear together and the rest compacts in order", delegate
            {
                Board b = new Board();
                FillRow(b, 19);
                FillRow(b, 18, 0);   // partial, 9 cells
                FillRow(b, 17);
                FillRow(b, 16, 1, 2); // partial, 8 cells
                Eq(2, b.ClearLines(), "cleared");
                Eq(9, RowCount(b, 19), "old row 18 now at the bottom");
                Eq(8, RowCount(b, 18), "old row 16 now above it");
                Eq(0, RowCount(b, 17), "rest empty");
            });
            Run("four full rows clear at once", delegate
            {
                Board b = new Board();
                for (int y = 16; y < 20; y++) FillRow(b, y);
                Eq(4, b.ClearLines(), "cleared");
                True(b.IsEmpty, "board not empty");
            });
            Run("clear keeps everything above shifted down by exactly the rows removed", delegate
            {
                Board b = new Board();
                FillRow(b, 19);
                b.SetCell(5, 15, 3);
                Eq(1, b.ClearLines(), "cleared");
                Eq(3, b.Get(5, 16), "block moved down one row");
                Eq(0, b.Get(5, 15), "old spot is empty");
            });
            Run("clone is independent of the original", delegate
            {
                Board a = new Board();
                a.SetCell(1, 1, 1);
                Board b = a.Clone();
                b.SetCell(2, 2, 1);
                Eq(1, a.FilledCount(), "original changed");
                Eq(2, b.FilledCount(), "clone");
            });
            Run("column height and holes", delegate
            {
                Board b = new Board();
                Eq(0, b.ColumnHeight(3), "empty column");
                b.SetCell(3, 15, 1);
                Eq(5, b.ColumnHeight(3), "height");
                Eq(4, b.CountHoles(), "holes below the block");
                b.SetCell(3, 19, 1);
                Eq(3, b.CountHoles(), "one hole filled");
            });

            // ---- PieceBag ----
            Run("bag deals every piece once per seven draws", delegate
            {
                PieceBag bag = new PieceBag(new Rng(5));
                for (int round = 0; round < 100; round++)
                {
                    HashSet<PieceType> seen = new HashSet<PieceType>();
                    for (int i = 0; i < 7; i++) seen.Add(bag.Next());
                    Eq(7, seen.Count, "round " + round);
                }
            });
            Run("bag is deterministic for a seed and varies between seeds", delegate
            {
                PieceBag a = new PieceBag(new Rng(7)), b = new PieceBag(new Rng(7)), c = new PieceBag(new Rng(8));
                bool differ = false;
                for (int i = 0; i < 70; i++)
                {
                    PieceType x = a.Next();
                    Eq(x, b.Next(), "same seed at " + i);
                    if (x != c.Next()) differ = true;
                }
                True(differ, "different seeds gave the same sequence");
            });
            Run("same piece is never more than twelve draws apart", delegate
            {
                PieceBag bag = new PieceBag(new Rng(11));
                int[] last = new int[7];
                for (int i = 0; i < 7; i++) last[i] = -1;
                for (int i = 0; i < 2000; i++)
                {
                    int t = (int)bag.Next();
                    if (last[t] >= 0) True(i - last[t] <= 13, "piece " + t + " gap " + (i - last[t]));
                    last[t] = i;
                }
            });
            Run("bag order is actually shuffled", delegate
            {
                HashSet<string> orders = new HashSet<string>();
                for (int seed = 0; seed < 20; seed++)
                {
                    PieceBag bag = new PieceBag(new Rng(seed));
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 7; i++) sb.Append((int)bag.Next());
                    orders.Add(sb.ToString());
                }
                True(orders.Count >= 15, "only " + orders.Count + " distinct orders from 20 seeds");
            });

            // ---- Scoring and speed ----
            Run("line scores follow the table and scale with level", delegate
            {
                Eq(100, Scoring.LineScore(1, 1), "single");
                Eq(300, Scoring.LineScore(2, 1), "double");
                Eq(500, Scoring.LineScore(3, 1), "triple");
                Eq(800, Scoring.LineScore(4, 1), "four");
                Eq(2400, Scoring.LineScore(4, 3), "four at level 3");
            });
            Run("line score ignores impossible inputs", delegate
            {
                Eq(0, Scoring.LineScore(0, 1), "zero lines");
                Eq(0, Scoring.LineScore(5, 1), "five lines");
                Eq(0, Scoring.LineScore(2, 0), "level zero");
                Eq(0, Scoring.LineScore(2, -1), "negative level");
            });
            Run("level rises every ten lines and caps", delegate
            {
                Eq(1, Speed.LevelFromLines(0), "0");
                Eq(1, Speed.LevelFromLines(9), "9");
                Eq(2, Speed.LevelFromLines(10), "10");
                Eq(3, Speed.LevelFromLines(25), "25");
                Eq(Speed.MaxLevel, Speed.LevelFromLines(10000), "cap");
                Eq(1, Speed.LevelFromLines(-5), "negative");
            });
            Run("gravity gets faster every level and has a floor", delegate
            {
                Near(1.0f, Speed.GravityInterval(1), 1e-6f, "level 1");
                for (int l = 1; l < 30; l++)
                    True(Speed.GravityInterval(l + 1) <= Speed.GravityInterval(l), "interval rose at level " + l);
                True(Speed.GravityInterval(2) < Speed.GravityInterval(1), "level 2 not faster");
                Near(0.3552f, Speed.GravityInterval(5), 1e-4f, "level 5");
                Near(0.06415f, Speed.GravityInterval(10), 1e-4f, "level 10");
                Near(0.02f, Speed.GravityInterval(999), 1e-6f, "floor");
                Near(1.0f, Speed.GravityInterval(-3), 1e-6f, "level below one");
            });

            // ---- HighScore ----
            Run("highscore parse handles junk", delegate
            {
                Eq(0, HighScore.Parse(null), "null");
                Eq(0, HighScore.Parse(""), "empty");
                Eq(0, HighScore.Parse("abc"), "text");
                Eq(0, HighScore.Parse("-5"), "negative");
                Eq(0, HighScore.Parse("99999999999999"), "overflow");
                Eq(1234, HighScore.Parse("1234"), "valid");
            });
            Run("highscore update only on strictly higher", delegate
            {
                bool isNew;
                Eq(100, HighScore.Update(100, 100, out isNew), "tie keeps best");
                True(!isNew, "tie flagged as new");
                Eq(150, HighScore.Update(100, 150, out isNew), "higher replaces");
                True(isNew, "higher not flagged");
                Eq(777, HighScore.Parse(HighScore.Format(777)), "round trip");
            });

            // ---- BlockGame: start ----
            Run("new game starts clean", delegate
            {
                BlockGame g = NewGame();
                Eq(0, g.Score, "score");
                Eq(0, g.Lines, "lines");
                Eq(1, g.Level, "level");
                True(!g.IsOver && g.CanHold && !g.HasHeld, "flags");
                Eq(0, g.Current.Rotation, "rotation");
                Eq(0, g.Current.Y, "spawn row");
                True(g.Board.IsEmpty, "board");
            });
            Run("pieces spawn centred at the top for every seed", delegate
            {
                HashSet<PieceType> types = new HashSet<PieceType>();
                for (int seed = 0; seed < 60; seed++)
                {
                    BlockGame g = new BlockGame(seed);
                    types.Add(g.Current.Type);
                    Eq((Board.Width - Shapes.Size(g.Current.Type)) / 2, g.Current.X, "x for " + g.Current.Type);
                    Eq(0, g.Current.Y, "y");
                    True(Fits(g), "spawn does not fit");
                }
                Eq(7, types.Count, "piece types seen across seeds");
            });

            // ---- BlockGame: movement ----
            Run("moves left and right and stops at the walls", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.O, 0, 4, 5);
                for (int i = 0; i < 4; i++) True(g.Move(-1), "left " + i);
                True(!g.Move(-1), "through left wall");
                Eq(0, g.Current.X, "x at left wall");
                for (int i = 0; i < 8; i++) True(g.Move(1), "right " + i);
                True(!g.Move(1), "through right wall");
                Eq(8, g.Current.X, "x at right wall");
            });
            Run("a block in the way stops a slide", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.O, 0, 4, 4);
                g.Board.SetCell(3, 5, 1);
                True(!g.Move(-1), "slid into a block");
                True(g.Move(1), "right should be free");
            });

            // ---- BlockGame: rotation ----
            Run("rotates both ways and wraps", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.T, 0, 3, 5);
                True(g.Rotate(1), "cw");
                Eq(1, g.Current.Rotation, "after cw");
                True(g.Rotate(-1), "ccw");
                Eq(0, g.Current.Rotation, "after ccw");
                True(g.Rotate(-1), "ccw again");
                Eq(3, g.Current.Rotation, "wrapped");
            });
            Run("wall kick pushes a rotated piece back inside", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.I, 1, 7, 5); // vertical bar in column 9
                True(g.Rotate(1), "kick failed");
                Eq(2, g.Current.Rotation, "rotation");
                Eq(6, g.Current.X, "shifted one column left");
                True(Fits(g), "kicked piece does not fit");
            });
            Run("rotation that fits nowhere is refused and changes nothing", delegate
            {
                BlockGame g = NewGame();
                for (int y = 0; y <= 10; y++) FillRow(g.Board, y);
                g.Board.SetCell(4, 5, 0);
                g.Board.SetCell(3, 6, 0);
                g.Board.SetCell(4, 6, 0);
                g.Board.SetCell(5, 6, 0);
                True(g.ForceCurrent(PieceType.T, 0, 3, 5), "setup does not fit");
                True(!g.Rotate(1), "rotated through solid blocks");
                Eq(0, g.Current.Rotation, "rotation changed");
                Eq(3, g.Current.X, "x changed");
                Eq(5, g.Current.Y, "y changed");
            });

            // ---- BlockGame: gravity and drops ----
            Run("gravity waits for the full interval", delegate
            {
                BlockGame g = NewGame();
                g.Tick(g.GravityInterval - 0.01f, false);
                Eq(0, g.Current.Y, "moved early");
                g.Tick(0.02f, false);
                Eq(1, g.Current.Y, "did not move");
            });
            Run("a long frame moves several rows", delegate
            {
                BlockGame g = NewGame();
                g.Tick(3.05f, false);
                Eq(3, g.Current.Y, "rows fallen");
                Eq(0, g.Score, "natural gravity must not score soft-drop points");
            });
            Run("holding soft drop falls fast and scores a point per row", delegate
            {
                BlockGame g = NewGame();
                g.Tick(0.1f, true);
                Eq(2, g.Current.Y, "rows fallen");
                Eq(2, g.Score, "score");
            });
            Run("soft drop step scores one and stops at the floor", delegate
            {
                BlockGame g = NewGame();
                True(g.SoftDropStep(), "step");
                Eq(1, g.Score, "score");
                Eq(1, g.Current.Y, "row");
                g.ForceCurrent(PieceType.O, 0, 4, 18);
                True(!g.SoftDropStep(), "moved through the floor");
                Eq(1, g.Score, "score changed at floor");
            });
            Run("hard drop lands, locks, scores two a row and spawns the next piece", delegate
            {
                BlockGame g = NewGame();
                PieceType expectedNext = g.Next;
                g.ForceCurrent(PieceType.O, 0, 4, 0);
                Eq(18, g.HardDrop(), "rows fallen");
                Eq(36, g.Score, "score");
                Eq(1, g.LockCount, "lock count");
                Eq((int)PieceType.O + 1, g.Board.Get(4, 19), "locked cell");
                Eq(4, g.Board.FilledCount(), "cells");
                Eq(expectedNext, g.Current.Type, "next piece spawned");
                Eq(0, g.Current.Y, "spawned at the top");
            });
            Run("hard drop stops on the stack", delegate
            {
                BlockGame g = NewGame();
                g.Board.SetCell(4, 19, 1);
                g.Board.SetCell(5, 19, 1);
                g.ForceCurrent(PieceType.O, 0, 4, 0);
                g.HardDrop();
                Eq((int)PieceType.O + 1, g.Board.Get(4, 17), "O sits on the stack");
            });
            Run("ghost row matches where a hard drop lands", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.O, 0, 4, 0);
                Eq(18, g.GhostY, "ghost on empty board");
                g.Board.SetCell(4, 19, 1);
                int ghost = g.GhostY;
                Eq(17, ghost, "ghost on stack");
                Eq(ghost, g.Current.Y + g.HardDrop(), "hard drop landed somewhere else");
            });
            Run("next piece preview becomes the current piece", delegate
            {
                BlockGame g = NewGame();
                for (int i = 0; i < 10; i++)
                {
                    PieceType preview = g.Next;
                    g.HardDrop();
                    Eq(preview, g.Current.Type, "preview at piece " + i);
                    g.Board.Clear();
                }
            });

            // ---- BlockGame: lock delay ----
            Run("a resting piece locks only after the lock delay", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.O, 0, 4, 18);
                g.Tick(0.3f, false);
                Eq(0, g.LockCount, "locked early");
                g.Tick(0.3f, false);
                Eq(1, g.LockCount, "did not lock");
            });
            Run("sliding while resting restarts the lock timer", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.O, 0, 4, 18);
                g.Tick(0.4f, false);
                True(g.Move(-1), "slide");
                g.Tick(0.4f, false);
                Eq(0, g.LockCount, "timer was not restarted");
                g.Tick(0.2f, false);
                Eq(1, g.LockCount, "did not lock after the restarted delay");
            });
            Run("lock timer restarts are limited so a piece cannot stall forever", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.O, 0, 4, 18);
                g.Tick(0.4f, false);
                for (int i = 0; i < BlockGame.MaxLockResets; i++)
                {
                    True(g.Move(i % 2 == 0 ? -1 : 1), "slide " + i);
                    g.Tick(0.4f, false);
                    Eq(0, g.LockCount, "locked during reset " + i);
                }
                True(g.Move(1), "final slide");
                g.Tick(0.4f, false);
                Eq(1, g.LockCount, "stalled past the reset limit");
            });
            Run("a piece that can still fall does not accumulate lock time", delegate
            {
                BlockGame g = NewGame();
                g.ForceCurrent(PieceType.O, 0, 4, 10);
                for (int i = 0; i < 10; i++) g.Tick(0.1f, false);
                Eq(0, g.LockCount, "locked in mid-air");
            });
            Run("time with a non-positive step changes nothing", delegate
            {
                BlockGame g = NewGame();
                g.Tick(0f, false);
                g.Tick(-1f, true);
                Eq(0, g.Current.Y, "moved");
                Eq(0, g.Score, "score");
                // A negative step must not have been banked as negative gravity time.
                g.Tick(1.0f, false);
                Eq(1, g.Current.Y, "gravity timer was corrupted by the negative step");
            });
            Run("an enormous step terminates and leaves a valid state", delegate
            {
                BlockGame g = NewGame();
                g.Tick(1000f, false);
                True(g.IsOver || Fits(g), "invalid state after a huge step");
                Eq(TotalPieceCells(g), g.Board.FilledCount(), "cells");
            });

            // ---- BlockGame: line clears and scoring ----
            Run("one row cleared scores a hundred plus the drop", delegate
            {
                BlockGame g = NewGame();
                FillRow(g.Board, 19, 4, 5);
                g.ForceCurrent(PieceType.O, 0, 4, 0);
                g.HardDrop();
                Eq(1, g.Lines, "lines");
                Eq(1, g.LastLinesCleared, "last clear");
                Eq(136, g.Score, "score");
                Eq(2, g.Board.FilledCount(), "the top half of the O is left");
            });
            Run("two rows cleared score three hundred", delegate
            {
                BlockGame g = NewGame();
                FillRow(g.Board, 18, 4, 5);
                FillRow(g.Board, 19, 4, 5);
                g.ForceCurrent(PieceType.O, 0, 4, 0);
                g.HardDrop();
                Eq(2, g.Lines, "lines");
                Eq(336, g.Score, "score");
                True(g.Board.IsEmpty, "board should be empty");
            });
            Run("four rows with a vertical I score eight hundred", delegate
            {
                BlockGame g = NewGame();
                for (int y = 16; y < 20; y++) FillRow(g.Board, y, 9);
                True(g.ForceCurrent(PieceType.I, 1, 7, 0), "setup");
                Eq(16, g.HardDrop(), "rows fallen");
                Eq(4, g.Lines, "lines");
                Eq(832, g.Score, "score");
                True(g.Board.IsEmpty, "board should be empty");
            });
            Run("non-adjacent rows clear together and the rest falls correctly", delegate
            {
                BlockGame g = NewGame();
                FillRow(g.Board, 19, 9);
                FillRow(g.Board, 18, 0, 9);
                FillRow(g.Board, 17, 9);
                g.ForceCurrent(PieceType.I, 1, 7, 0);
                g.HardDrop();
                Eq(2, g.Lines, "lines");
                // Rows 19 and 17 were completed. The partial row 18 (now with the I cell) falls to the bottom,
                // and the lone I cell that stuck out at row 16 falls to row 18.
                Eq(9, RowCount(g.Board, 19), "old row 18 now at the bottom");
                Eq(0, g.Board.Get(0, 19), "its gap is preserved");
                Eq(1, RowCount(g.Board, 18), "the lone I cell");
                True(g.Board.Get(9, 18) != 0, "I cell in column 9");
                Eq(0, RowCount(g.Board, 17), "nothing above");
            });
            Run("clearing rows raises the level every ten and scores at the old level", delegate
            {
                BlockGame g = NewGame();
                for (int i = 0; i < 5; i++)
                {
                    FillRow(g.Board, 18, 4, 5);
                    FillRow(g.Board, 19, 4, 5);
                    g.ForceCurrent(PieceType.O, 0, 4, 0);
                    g.HardDrop();
                }
                Eq(10, g.Lines, "lines");
                Eq(2, g.Level, "level");
                Eq(5 * 336, g.Score, "all five clears scored at level one");
                int before = g.Score;
                FillRow(g.Board, 18, 4, 5);
                FillRow(g.Board, 19, 4, 5);
                g.ForceCurrent(PieceType.O, 0, 4, 0);
                g.HardDrop();
                Eq(before + 36 + 600, g.Score, "next clear scored at level two");
                True(g.GravityInterval < Speed.GravityInterval(1), "gravity did not speed up");
            });

            // ---- BlockGame: hold ----
            Run("first hold stashes the piece and brings in the next one", delegate
            {
                BlockGame g = NewGame();
                PieceType first = g.Current.Type, next = g.Next;
                True(g.Hold(), "hold refused");
                Eq(first, g.Held, "held piece");
                True(g.HasHeld, "flag");
                Eq(next, g.Current.Type, "new current");
                True(!g.CanHold, "can hold again too soon");
            });
            Run("hold is refused twice in a row", delegate
            {
                BlockGame g = NewGame();
                g.Hold();
                PieceType cur = g.Current.Type;
                True(!g.Hold(), "second hold allowed");
                Eq(cur, g.Current.Type, "piece changed");
            });
            Run("after a lock the held piece can be swapped in at the top, unrotated", delegate
            {
                BlockGame g = NewGame();
                PieceType first = g.Current.Type;
                g.Hold();
                g.HardDrop();
                True(g.CanHold, "hold not re-enabled by the lock");
                PieceType current = g.Current.Type;
                g.Move(1);
                g.Rotate(1);
                True(g.Hold(), "swap refused");
                Eq(first, g.Current.Type, "swapped-in piece");
                Eq(current, g.Held, "now held");
                Eq(0, g.Current.Rotation, "rotation reset");
                Eq(0, g.Current.Y, "back at the top");
                Eq((Board.Width - Shapes.Size(first)) / 2, g.Current.X, "centred");
            });

            // ---- BlockGame: game over ----
            Run("game over when the next piece has no room", delegate
            {
                BlockGame g = NewGame();
                for (int y = 0; y < 2; y++)
                    for (int x = 3; x <= 6; x++) g.Board.SetCell(x, y, 1);
                g.ForceCurrent(PieceType.O, 0, 0, 10);
                g.HardDrop();
                True(g.IsOver, "not over");
            });
            Run("game over when a piece locks above the visible board", delegate
            {
                BlockGame g = NewGame();
                for (int y = 3; y < Board.Height; y++) g.Board.SetCell(5, y, 1);
                True(g.ForceCurrent(PieceType.I, 1, 3, -1), "setup");
                g.Tick(0.6f, false);
                True(g.IsOver, "lock-out not detected");
                Eq(1, g.LockCount, "lock count");
            });
            Run("nothing works after game over", delegate
            {
                BlockGame g = NewGame();
                for (int y = 0; y < 2; y++)
                    for (int x = 3; x <= 6; x++) g.Board.SetCell(x, y, 1);
                g.ForceCurrent(PieceType.O, 0, 0, 10);
                g.HardDrop();
                int score = g.Score, locks = g.LockCount;
                True(!g.Move(1) && !g.Move(-1), "move");
                True(!g.Rotate(1), "rotate");
                True(!g.Hold(), "hold");
                True(!g.SoftDropStep(), "soft drop step");
                Eq(0, g.HardDrop(), "hard drop");
                g.Tick(5f, true);
                Eq(score, g.Score, "score changed");
                Eq(locks, g.LockCount, "lock count changed");
            });
            Run("reset gives a fresh game with the new seed", delegate
            {
                BlockGame g = new BlockGame(3);
                g.HardDrop();
                g.Hold();
                g.Reset(4);
                BlockGame fresh = new BlockGame(4);
                Eq(fresh.Current.Type, g.Current.Type, "current");
                Eq(fresh.Next, g.Next, "next");
                Eq(0, g.Score, "score");
                Eq(0, g.LockCount, "locks");
                True(g.Board.IsEmpty && !g.HasHeld && g.CanHold && !g.IsOver, "flags");
            });

            // ---- BlockGame: determinism and the bag ----
            Run("same seed gives the same game", delegate
            {
                BlockGame a = new BlockGame(21), b = new BlockGame(21);
                for (int i = 0; i < 40; i++)
                {
                    Eq(a.Current.Type, b.Current.Type, "piece " + i);
                    Eq(a.Next, b.Next, "next " + i);
                    a.HardDrop();
                    b.HardDrop();
                    a.Board.Clear();
                    b.Board.Clear();
                }
            });
            Run("the pieces dealt in play follow the 7-bag", delegate
            {
                BlockGame g = new BlockGame(33);
                List<PieceType> seq = new List<PieceType>();
                seq.Add(g.Current.Type);
                seq.Add(g.Next);
                for (int i = 0; i < 12; i++)
                {
                    g.HardDrop();
                    g.Board.Clear();
                    seq.Add(g.Next);
                }
                for (int block = 0; block < 2; block++)
                {
                    HashSet<PieceType> seen = new HashSet<PieceType>();
                    for (int i = 0; i < 7; i++) seen.Add(seq[block * 7 + i]);
                    Eq(7, seen.Count, "bag " + block);
                }
            });

            // ---- Simulations ----
            int dumbPieces = 0;
            Run("a bot that only hard-drops in place loses without clearing a row", delegate
            {
                BlockGame g = new BlockGame(2);
                while (!g.IsOver && dumbPieces < 200)
                {
                    g.HardDrop();
                    dumbPieces++;
                }
                True(g.IsOver, "never lost");
                Eq(0, g.Lines, "lines");
                True(dumbPieces < 40, "took " + dumbPieces + " pieces to lose");
            });
            int[] botLines = new int[3];
            Run("a greedy bot clears many rows, keeps the board consistent and never leaves a full row", delegate
            {
                for (int s = 0; s < 3; s++)
                {
                    BlockGame g = new BlockGame(s + 1);
                    for (int i = 0; i < 300 && !g.IsOver; i++)
                    {
                        BotPlaceBest(g);
                        Eq(TotalPieceCells(g), g.Board.FilledCount(), "cell conservation, seed " + (s + 1) + " piece " + i);
                        True(!HasFullRow(g.Board), "a full row was left behind");
                    }
                    botLines[s] = g.Lines;
                    True(!g.IsOver, "bot lost with seed " + (s + 1));
                    True(g.Lines >= 80, "only " + g.Lines + " lines with seed " + (s + 1));
                    Eq(Speed.LevelFromLines(g.Lines), g.Level, "level");
                }
            });
            Run("random play never breaks an invariant", delegate
            {
                for (int seed = 0; seed < 6; seed++)
                {
                    BlockGame g = new BlockGame(seed);
                    Rng r = new Rng(1000 + seed);
                    int lastScore = 0;
                    for (int step = 0; step < 6000; step++)
                    {
                        switch (r.Range(0, 10))
                        {
                            case 0: g.Move(-1); break;
                            case 1: g.Move(1); break;
                            case 2: g.Rotate(1); break;
                            case 3: g.Rotate(-1); break;
                            case 4: g.SoftDropStep(); break;
                            case 5: if (r.Range(0, 4) == 0) g.HardDrop(); break;
                            case 6: g.Hold(); break;
                            default: g.Tick(r.Range(0f, 0.4f), r.Range(0, 2) == 0); break;
                        }
                        if (g.IsOver)
                        {
                            g.Reset(seed * 7919 + step);
                            lastScore = 0;
                            continue;
                        }
                        True(Fits(g), "current piece overlaps something, seed " + seed + " step " + step);
                        Eq(TotalPieceCells(g), g.Board.FilledCount(), "cell conservation, seed " + seed + " step " + step);
                        True(!HasFullRow(g.Board), "full row left behind");
                        True(g.Score >= lastScore, "score went down");
                        lastScore = g.Score;
                        Eq(Speed.LevelFromLines(g.Lines), g.Level, "level");
                    }
                }
            });

            Console.WriteLine(passed + " passed, " + failed + " failed");
            if (stats)
            {
                Console.WriteLine("stats: dumb bot lost after " + dumbPieces + " pieces; greedy bot lines in 300 pieces: "
                    + botLines[0] + ", " + botLines[1] + ", " + botLines[2]);
            }
            return failed == 0 ? 0 : 1;
        }
    }
}
