namespace ClipBoard.Core;

public sealed class WinVKeyFilter
{
    private bool swallowingV;

    public bool Process(uint virtualKey, bool keyDown, bool windowsDown, bool otherModifierDown, out bool activate)
    {
        activate = false;
        if (virtualKey != 0x56)
            return false;
        if (!keyDown)
        {
            var handled = swallowingV;
            swallowingV = false;
            return handled;
        }
        if (swallowingV)
            return true;
        if (!windowsDown || otherModifierDown)
            return false;
        swallowingV = true;
        activate = true;
        return true;
    }

    public void Reset() => swallowingV = false;
}