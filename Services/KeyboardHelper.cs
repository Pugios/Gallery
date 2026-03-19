namespace app.Services
{
    internal static class KeyboardHelper
    {
        public static bool IsShiftPressed()
        {
#if WINDOWS
            return (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Shift) &
                Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
#else
            return false;
#endif
        }

        public static bool IsControlPressed()
        {
#if WINDOWS
            return (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control) &
                Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
#else
            return false;
#endif
        }
    }
}
