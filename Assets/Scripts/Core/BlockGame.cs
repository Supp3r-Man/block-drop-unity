using System;

namespace BlockDrop.Core
{
    /// <summary>The piece currently falling: type, rotation 0..3, and the box's top-left cell on the board.</summary>
    public struct ActivePiece
    {
        public readonly PieceType Type;
        public readonly int Rotation;
        public readonly int X;
        public readonly int Y;

        public ActivePiece(PieceType type, int rotation, int x, int y)
        {
            Type = type;
            Rotation = rotation;
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// All the rules of Block Drop with no engine types: movement, rotation with wall kicks, gravity, soft and
    /// hard drop, lock delay, line clears, scoring, levels, hold, next-piece preview and game over.
    /// The Unity layer calls these methods and draws the result.
    /// </summary>
    public sealed class BlockGame
    {
        public const float LockDelay = 0.5f;
        public const int MaxLockResets = 15;
        public const float SoftDropInterval = 0.04f;

        // Tried in order when a rotation does not fit in place: stay, left 1-2, right 1-2, up 1.
        private static readonly int[][] Kicks =
        {
            new[] { 0, 0 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -2, 0 }, new[] { 2, 0 }, new[] { 0, -1 }
        };

        private PieceBag bag;
        private float gravityTimer;
        private float lockTimer;
        private int lockResets;

        public Board Board { get; private set; }
        public ActivePiece Current { get; private set; }
        public PieceType Next { get; private set; }
        public PieceType Held { get; private set; }
        public bool HasHeld { get; private set; }
        public bool CanHold { get; private set; }
        public int Score { get; private set; }
        public int Lines { get; private set; }
        public int Level { get; private set; }
        public bool IsOver { get; private set; }

        /// <summary>Number of pieces locked so far. The view compares it between frames to play lock sounds.</summary>
        public int LockCount { get; private set; }

        /// <summary>Rows cleared by the most recent lock (0 if none).</summary>
        public int LastLinesCleared { get; private set; }

        public BlockGame(int seed)
        {
            Reset(seed);
        }

        public void Reset(int seed)
        {
            Board = new Board();
            bag = new PieceBag(new Rng(seed));
            Score = 0;
            Lines = 0;
            Level = Speed.LevelFromLines(0);
            IsOver = false;
            HasHeld = false;
            CanHold = true;
            LockCount = 0;
            LastLinesCleared = 0;
            gravityTimer = 0f;
            lockTimer = 0f;
            lockResets = 0;
            PieceType first = bag.Next();
            Next = bag.Next();
            Spawn(first);
        }

        public float GravityInterval
        {
            get { return Speed.GravityInterval(Level); }
        }

        public bool IsGrounded
        {
            get { return !Board.Fits(Current.Type, Current.Rotation, Current.X, Current.Y + 1); }
        }

        /// <summary>Row the current piece would land on if hard-dropped now.</summary>
        public int GhostY
        {
            get { return Board.DropY(Current.Type, Current.Rotation, Current.X, Current.Y); }
        }

        /// <summary>Put a specific piece in play (tests, and restoring a saved position). Returns whether it fits.</summary>
        public bool ForceCurrent(PieceType type, int rotation, int x, int y)
        {
            Current = new ActivePiece(type, rotation, x, y);
            gravityTimer = 0f;
            lockTimer = 0f;
            lockResets = 0;
            return Board.Fits(type, rotation, x, y);
        }

        private void Spawn(PieceType type)
        {
            int x = (Board.Width - Shapes.Size(type)) / 2;
            Current = new ActivePiece(type, 0, x, 0);
            gravityTimer = 0f;
            lockTimer = 0f;
            lockResets = 0;
            if (!Board.Fits(type, 0, x, 0)) IsOver = true;
        }

        private void SpawnNext()
        {
            PieceType t = Next;
            Next = bag.Next();
            Spawn(t);
        }

        /// <summary>Slide one column (-1 left, +1 right). False if blocked or the game is over.</summary>
        public bool Move(int dx)
        {
            if (IsOver) return false;
            if (!Board.Fits(Current.Type, Current.Rotation, Current.X + dx, Current.Y)) return false;
            Current = new ActivePiece(Current.Type, Current.Rotation, Current.X + dx, Current.Y);
            OnAdjusted();
            return true;
        }

        /// <summary>Rotate clockwise (dir = +1) or counter-clockwise (-1), trying the wall kicks in order.</summary>
        public bool Rotate(int dir)
        {
            if (IsOver) return false;
            int rotation = ((Current.Rotation + dir) % 4 + 4) % 4;
            for (int i = 0; i < Kicks.Length; i++)
            {
                int nx = Current.X + Kicks[i][0];
                int ny = Current.Y + Kicks[i][1];
                if (Board.Fits(Current.Type, rotation, nx, ny))
                {
                    Current = new ActivePiece(Current.Type, rotation, nx, ny);
                    OnAdjusted();
                    return true;
                }
            }
            return false;
        }

        /// <summary>A successful slide or rotation while resting on something restarts the lock timer, a limited number of times.</summary>
        private void OnAdjusted()
        {
            if (IsGrounded && lockResets < MaxLockResets)
            {
                lockTimer = 0f;
                lockResets++;
            }
        }

        private bool TryMoveDown()
        {
            if (!Board.Fits(Current.Type, Current.Rotation, Current.X, Current.Y + 1)) return false;
            Current = new ActivePiece(Current.Type, Current.Rotation, Current.X, Current.Y + 1);
            // A new lowest row: the lock timer and its reset budget start over.
            lockTimer = 0f;
            lockResets = 0;
            return true;
        }

        /// <summary>One manual soft-drop step: one row down for one point. False if it cannot move down.</summary>
        public bool SoftDropStep()
        {
            if (IsOver) return false;
            if (!TryMoveDown()) return false;
            Score += Scoring.SoftDropPointsPerCell;
            return true;
        }

        /// <summary>Drop to the floor and lock at once, two points per row fallen. Returns the rows fallen.</summary>
        public int HardDrop()
        {
            if (IsOver) return 0;
            int y = GhostY;
            int fallen = y - Current.Y;
            Score += Scoring.HardDropPointsPerCell * fallen;
            Current = new ActivePiece(Current.Type, Current.Rotation, Current.X, y);
            LockCurrent();
            return fallen;
        }

        /// <summary>Swap the piece in play with the held one (or stash it on first use). Once per piece.</summary>
        public bool Hold()
        {
            if (IsOver || !CanHold) return false;
            PieceType outgoing = Current.Type;
            if (HasHeld)
            {
                PieceType incoming = Held;
                Held = outgoing;
                Spawn(incoming);
            }
            else
            {
                Held = outgoing;
                HasHeld = true;
                SpawnNext();
            }
            CanHold = false;
            return true;
        }

        /// <summary>Advance time: gravity (or the faster soft-drop rate) and the lock delay.</summary>
        public void Tick(float dt, bool softDrop)
        {
            if (IsOver || dt <= 0f) return;

            float gravity = GravityInterval;
            float interval = softDrop ? Math.Min(gravity, SoftDropInterval) : gravity;
            gravityTimer += dt;
            int guard = 0;
            while (gravityTimer >= interval && guard < 200)
            {
                guard++;
                gravityTimer -= interval;
                if (TryMoveDown())
                {
                    if (softDrop) Score += Scoring.SoftDropPointsPerCell;
                }
                else
                {
                    gravityTimer = 0f;
                    break;
                }
            }
            if (guard >= 200) gravityTimer = 0f;

            if (IsGrounded)
            {
                lockTimer += dt;
                if (lockTimer >= LockDelay) LockCurrent();
            }
            else
            {
                lockTimer = 0f;
            }
        }

        private void LockCurrent()
        {
            bool placed = Board.Lock(Current.Type, Current.Rotation, Current.X, Current.Y);
            LockCount++;
            if (!placed)
            {
                // Locked while part of the piece is still above the visible board.
                IsOver = true;
                LastLinesCleared = 0;
                return;
            }

            int cleared = Board.ClearLines();
            LastLinesCleared = cleared;
            if (cleared > 0)
            {
                // Scored at the level the rows were cleared on, then the level may rise.
                Score += Scoring.LineScore(cleared, Level);
                Lines += cleared;
                Level = Speed.LevelFromLines(Lines);
            }
            CanHold = true;
            SpawnNext();
        }
    }
}
