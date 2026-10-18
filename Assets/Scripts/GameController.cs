using System;
using UnityEngine;
using BlockDrop.Core;

namespace BlockDrop
{
    /// <summary>
    /// Unity side of Block Drop: builds the camera and sprites, reads the keyboard, calls into
    /// BlockDrop.Core.BlockGame (which holds every rule) and draws the result plus an IMGUI HUD.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private const float DasDelay = 0.17f;    // hold a direction this long before it auto-repeats
        private const float DasRate = 0.05f;     // then repeat at this interval
        private const float MaxFrameTime = 0.05f;
        private const string BestKey = "blockdrop.best";
        private const float BoardCenterToPanel = 8.5f;

        private static readonly Color[] PieceColors =
        {
            new Color(0.25f, 0.85f, 0.90f), // I
            new Color(0.98f, 0.80f, 0.20f), // O
            new Color(0.70f, 0.35f, 0.90f), // T
            new Color(0.35f, 0.85f, 0.40f), // S
            new Color(0.95f, 0.35f, 0.35f), // Z
            new Color(0.30f, 0.45f, 0.95f), // J
            new Color(0.98f, 0.60f, 0.20f)  // L
        };

        private static readonly Color EmptyColor = new Color(0.11f, 0.12f, 0.20f);

        private readonly SpriteRenderer[,] boardCells = new SpriteRenderer[Board.Width, Board.Height];
        private readonly SpriteRenderer[] currentCells = new SpriteRenderer[4];
        private readonly SpriteRenderer[] ghostCells = new SpriteRenderer[4];
        private readonly SpriteRenderer[] nextCells = new SpriteRenderer[4];
        private readonly SpriteRenderer[] holdCells = new SpriteRenderer[4];

        private BlockGame game;
        private Sfx sfx;
        private Camera cam;
        private int best;
        private bool newBest;
        private bool paused;
        private int dasDir;
        private float dasTimer;
        private int lastLock;
        private int lastLevel;
        private GUIStyle bigStyle, midStyle, smallStyle;

        private void Awake()
        {
            game = new BlockGame(Environment.TickCount);
            sfx = new Sfx(gameObject);
            SetupCamera();
            SetupSprites();
            StartGame();
            Refresh();
        }

        private void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.orthographicSize = 12f; // 24 world units tall; the board is 20
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.09f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
        }

        private void SetupSprites()
        {
            // Backdrop behind the board.
            var back = MakeSprite("Backdrop", SpriteFactory.Square(), new Color(0.02f, 0.02f, 0.05f), -20);
            back.transform.localScale = new Vector3(Board.Width + 0.4f, Board.Height + 0.4f, 1f);

            for (int x = 0; x < Board.Width; x++)
            {
                for (int y = 0; y < Board.Height; y++)
                {
                    var sr = MakeSprite("Cell", SpriteFactory.Block(), EmptyColor, 0);
                    sr.transform.position = CellPos(x, y);
                    sr.transform.localScale = Vector3.one * 0.94f;
                    boardCells[x, y] = sr;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                currentCells[i] = MakeSprite("Piece", SpriteFactory.Block(), Color.white, 5);
                ghostCells[i] = MakeSprite("Ghost", SpriteFactory.Block(), Color.white, 3);
                nextCells[i] = MakeSprite("Next", SpriteFactory.Block(), Color.white, 5);
                holdCells[i] = MakeSprite("Hold", SpriteFactory.Block(), Color.white, 5);
                currentCells[i].transform.localScale = Vector3.one * 0.94f;
                ghostCells[i].transform.localScale = Vector3.one * 0.94f;
            }
        }

        private SpriteRenderer MakeSprite(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>World position of a board cell. Row 0 is the top row.</summary>
        private static Vector3 CellPos(int x, int y)
        {
            return new Vector3(x - (Board.Width - 1) / 2f, (Board.Height - 1) / 2f - y, 0f);
        }

        private void StartGame()
        {
            game.Reset(Environment.TickCount);
            best = HighScore.Parse(PlayerPrefs.GetString(BestKey, ""));
            newBest = false;
            paused = false;
            dasDir = 0;
            dasTimer = 0f;
            lastLock = game.LockCount;
            lastLevel = game.Level;
        }

        private void Update()
        {
            if (!game.IsOver && InputReader.Pressed(Btn.Pause)) paused = !paused;
            if ((game.IsOver || paused) && InputReader.Pressed(Btn.Restart))
            {
                StartGame();
                Refresh();
                return;
            }

            if (!paused && !game.IsOver) Step(Mathf.Min(Time.deltaTime, MaxFrameTime));
            Refresh();
        }

        private void Step(float dt)
        {
            HandleHorizontal(dt);
            if (InputReader.Pressed(Btn.RotateCw) && game.Rotate(1)) sfx.Rotate();
            if (InputReader.Pressed(Btn.RotateCcw) && game.Rotate(-1)) sfx.Rotate();
            if (InputReader.Pressed(Btn.Hold) && game.Hold()) sfx.Hold();
            if (InputReader.Pressed(Btn.HardDrop)) game.HardDrop();
            game.Tick(dt, InputReader.Held(Btn.Down));
            AfterStep();
        }

        /// <summary>Move once on the first press, then auto-repeat after a short delay.</summary>
        private void HandleHorizontal(float dt)
        {
            bool left = InputReader.Held(Btn.Left), right = InputReader.Held(Btn.Right);
            int dir = left == right ? 0 : (left ? -1 : 1);
            if (dir != dasDir)
            {
                dasDir = dir;
                dasTimer = 0f;
                if (dir != 0 && game.Move(dir)) sfx.Move();
                return;
            }
            if (dir == 0) return;
            dasTimer += dt;
            while (dasTimer >= DasDelay)
            {
                if (game.Move(dir)) sfx.Move();
                dasTimer -= DasRate;
            }
        }

        private void AfterStep()
        {
            if (game.LockCount != lastLock)
            {
                lastLock = game.LockCount;
                if (game.LastLinesCleared > 0) sfx.Clear(game.LastLinesCleared);
                else sfx.Lock();
            }
            if (game.Level != lastLevel)
            {
                if (game.Level > lastLevel) sfx.LevelUp();
                lastLevel = game.Level;
            }
            if (game.IsOver)
            {
                sfx.GameOver();
                SaveBest();
            }
        }

        private void SaveBest()
        {
            bool isNew;
            best = HighScore.Update(best, game.Score, out isNew);
            newBest = isNew;
            PlayerPrefs.SetString(BestKey, HighScore.Format(best));
            PlayerPrefs.Save();
        }

        // ---- drawing ----

        private void Refresh()
        {
            for (int x = 0; x < Board.Width; x++)
            {
                for (int y = 0; y < Board.Height; y++)
                {
                    int v = game.Board.Get(x, y);
                    boardCells[x, y].color = v == 0 ? EmptyColor : PieceColors[v - 1];
                }
            }

            ActivePiece cur = game.Current;
            Color pc = PieceColors[(int)cur.Type];
            ShowOnBoard(currentCells, cur.Type, cur.Rotation, cur.X, cur.Y, pc);

            if (game.IsOver)
            {
                HideAll(ghostCells);
            }
            else
            {
                int gy = game.GhostY;
                if (gy == cur.Y) HideAll(ghostCells);
                else ShowOnBoard(ghostCells, cur.Type, cur.Rotation, cur.X, gy, new Color(pc.r, pc.g, pc.b, 0.28f));
            }

            ShowPreview(nextCells, game.Next, true, BoardCenterToPanel, 7.5f);
            ShowPreview(holdCells, game.Held, game.HasHeld, -BoardCenterToPanel, 7.5f);
        }

        private static void ShowOnBoard(SpriteRenderer[] sprites, PieceType type, int rotation, int px, int py, Color color)
        {
            Cell[] shape = Shapes.Cells(type, rotation);
            for (int i = 0; i < 4; i++)
            {
                int cx = px + shape[i].X, cy = py + shape[i].Y;
                SpriteRenderer sr = sprites[i];
                // Cells above the top edge exist while a piece is spawning or kicked up; they are not drawn.
                sr.enabled = cy >= 0;
                sr.color = color;
                sr.transform.position = CellPos(cx, cy);
            }
        }

        private static void HideAll(SpriteRenderer[] sprites)
        {
            for (int i = 0; i < sprites.Length; i++) sprites[i].enabled = false;
        }

        /// <summary>Draw a piece centred on a world position (the next-piece and hold boxes).</summary>
        private static void ShowPreview(SpriteRenderer[] sprites, PieceType type, bool visible, float centerX, float centerY)
        {
            if (!visible)
            {
                HideAll(sprites);
                return;
            }
            Cell[] shape = Shapes.Cells(type, 0);
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < 4; i++)
            {
                minX = Math.Min(minX, shape[i].X);
                maxX = Math.Max(maxX, shape[i].X);
                minY = Math.Min(minY, shape[i].Y);
                maxY = Math.Max(maxY, shape[i].Y);
            }
            float midX = (minX + maxX) / 2f, midY = (minY + maxY) / 2f;
            for (int i = 0; i < 4; i++)
            {
                SpriteRenderer sr = sprites[i];
                sr.enabled = true;
                sr.color = PieceColors[(int)type];
                sr.transform.localScale = Vector3.one * 0.94f;
                sr.transform.position = new Vector3(centerX + shape[i].X - midX, centerY - (shape[i].Y - midY), 0f);
            }
        }

        // ---- HUD (IMGUI: no UI package or canvas needed) ----

        private void EnsureStyles()
        {
            int h = Screen.height;
            if (bigStyle == null)
            {
                bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                midStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                smallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            }
            bigStyle.fontSize = Mathf.Max(22, h / 12);
            midStyle.fontSize = Mathf.Max(14, h / 30);
            smallStyle.fontSize = Mathf.Max(11, h / 42);
        }

        /// <summary>Screen rectangle for a label centred on a world position, sized in world units.</summary>
        private Rect WorldRect(float cx, float cy, float widthUnits, float heightUnits)
        {
            Vector3 p = cam.WorldToScreenPoint(new Vector3(cx, cy, 0f));
            float ppu = Screen.height / (cam.orthographicSize * 2f);
            float w = widthUnits * ppu, h = heightUnits * ppu;
            return new Rect(p.x - w / 2f, Screen.height - p.y - h / 2f, w, h);
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        private void OnGUI()
        {
            if (game == null || cam == null) return;
            EnsureStyles();

            Shadowed(WorldRect(-BoardCenterToPanel, 10.4f, 6f, 1.2f), "HOLD", midStyle, Color.white);
            Shadowed(WorldRect(BoardCenterToPanel, 10.4f, 6f, 1.2f), "NEXT", midStyle, Color.white);

            int shownBest = Math.Max(best, game.Score);
            string stats = "SCORE\n" + game.Score + "\n\nLINES  " + game.Lines + "\nLEVEL  " + game.Level + "\n\nBEST\n" + shownBest;
            Shadowed(WorldRect(BoardCenterToPanel, 0.5f, 6f, 9f), stats, midStyle, new Color(1f, 0.95f, 0.8f));

            Shadowed(WorldRect(-BoardCenterToPanel, -5.5f, 6.5f, 8f),
                "Left / Right  move\nUp or X  rotate\nZ  rotate back\nDown  soft drop\nSpace  hard drop\nC or Shift  hold\nP  pause", smallStyle,
                new Color(1f, 1f, 1f, 0.8f));

            if (paused)
            {
                Shadowed(WorldRect(0f, 1.5f, 10f, 3f), "PAUSED", bigStyle, Color.white);
                Shadowed(WorldRect(0f, -0.8f, 10f, 1.2f), "P to resume   R to restart", smallStyle, Color.white);
            }

            if (game.IsOver)
            {
                Shadowed(WorldRect(0f, 2.5f, 10f, 3f), "GAME OVER", bigStyle, new Color(1f, 0.4f, 0.4f));
                Shadowed(WorldRect(0f, 0.2f, 10f, 1.2f),
                    "Score " + game.Score + (newBest ? "   NEW BEST!" : "   Best " + best), smallStyle, Color.white);
                Shadowed(WorldRect(0f, -1.2f, 10f, 1.2f), "Press R or Enter to play again", smallStyle, Color.white);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && game != null && !game.IsOver) paused = true;
        }
    }
}
