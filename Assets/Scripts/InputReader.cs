using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace BlockDrop
{
    public enum Btn
    {
        Left,
        Right,
        Down,
        RotateCw,
        RotateCcw,
        HardDrop,
        Hold,
        Pause,
        Restart
    }

    /// <summary>
    /// One place that reads the keyboard, so the game works whichever Active Input Handling the project
    /// uses (Input Manager (Old), Input System Package (New) or Both). Two keys can map to one button.
    /// </summary>
    public static class InputReader
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        private static readonly KeyCode[][] Map =
        {
            new[] { KeyCode.LeftArrow, KeyCode.A },          // Left
            new[] { KeyCode.RightArrow, KeyCode.D },         // Right
            new[] { KeyCode.DownArrow, KeyCode.S },          // Down
            new[] { KeyCode.UpArrow, KeyCode.X },            // RotateCw
            new[] { KeyCode.Z, KeyCode.Z },                  // RotateCcw
            new[] { KeyCode.Space, KeyCode.Space },          // HardDrop
            new[] { KeyCode.C, KeyCode.LeftShift },          // Hold
            new[] { KeyCode.P, KeyCode.Escape },             // Pause
            new[] { KeyCode.R, KeyCode.Return }              // Restart
        };

        public static bool Held(Btn b)
        {
            KeyCode[] keys = Map[(int)b];
            return Input.GetKey(keys[0]) || Input.GetKey(keys[1]);
        }

        public static bool Pressed(Btn b)
        {
            KeyCode[] keys = Map[(int)b];
            return Input.GetKeyDown(keys[0]) || Input.GetKeyDown(keys[1]);
        }
#elif ENABLE_INPUT_SYSTEM
        public static bool Held(Btn b)
        {
            return Check(b, false);
        }

        public static bool Pressed(Btn b)
        {
            return Check(b, true);
        }

        private static bool Check(Btn b, bool pressedThisFrame)
        {
            Keyboard k = Keyboard.current;
            if (k == null) return false;
            UnityEngine.InputSystem.Controls.KeyControl a, c;
            switch (b)
            {
                case Btn.Left: a = k.leftArrowKey; c = k.aKey; break;
                case Btn.Right: a = k.rightArrowKey; c = k.dKey; break;
                case Btn.Down: a = k.downArrowKey; c = k.sKey; break;
                case Btn.RotateCw: a = k.upArrowKey; c = k.xKey; break;
                case Btn.RotateCcw: a = k.zKey; c = k.zKey; break;
                case Btn.HardDrop: a = k.spaceKey; c = k.spaceKey; break;
                case Btn.Hold: a = k.cKey; c = k.leftShiftKey; break;
                case Btn.Pause: a = k.pKey; c = k.escapeKey; break;
                default: a = k.rKey; c = k.enterKey; break;
            }
            return pressedThisFrame
                ? (a.wasPressedThisFrame || c.wasPressedThisFrame)
                : (a.isPressed || c.isPressed);
        }
#else
        public static bool Held(Btn b) { return false; }
        public static bool Pressed(Btn b) { return false; }
#endif
    }
}
