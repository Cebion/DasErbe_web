using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Game.Shared.Host;
using Game.Shared.Host.Input;
using Game.Shared.Input;

namespace Game.Web;

/// <summary>
///     Polls browser mouse/keyboard input into host-neutral frames. JS pushes raw events into static state via
///     [JSExport] hooks, mirroring Game.Desktop's MonoGameInputBackend but event-driven instead of polling a
///     global keyboard/mouse snapshot.
/// </summary>
/// <remarks>
///     Every [JSExport] hook here returns <see cref="Task" /> rather than <see langword="void" />: under
///     WasmEnableThreads, the .NET runtime's execution context is not the browser's main JS thread, and calling
///     a synchronous (void-returning) [JSExport] method throws "Cannot call synchronous C# methods" at runtime
///     (confirmed on a real deployed build, not just from documentation) - only Task-returning exports can be
///     dispatched. The bodies do no actual awaiting; they return <see cref="Task.CompletedTask" />.
/// </remarks>
public sealed partial class WebInputBackend : IInputBackend
{
    private static readonly HashSet<InputKey> HeldKeys = [];
    private static readonly Queue<InputKeyStroke> QueuedKeyStrokes = new();
    private static bool _isPointerInsideCanvas;
    private static bool _isPrimaryDown;
    private static double _mouseX;
    private static double _mouseY;
    private static double _boundsWidth = 1;
    private static double _boundsHeight = 1;

    /// <inheritdoc />
    public void Reset()
    {
        HeldKeys.Clear();
        QueuedKeyStrokes.Clear();
        _isPrimaryDown = false;
        _isPointerInsideCanvas = false;
    }

    /// <inheritdoc />
    public InputFrame Poll(HostPresentationRect rect)
    {
        var boundsWidth = Math.Max(1, (int)_boundsWidth);
        var boundsHeight = Math.Max(1, (int)_boundsHeight);
        var pointer = new InputPointerState(_isPointerInsideCanvas,
            Math.Clamp((int)_mouseX, 0, boundsWidth - 1),
            Math.Clamp((int)_mouseY, 0, boundsHeight - 1),
            boundsWidth,
            boundsHeight);

        var mouseButtons = InputMouseButtons.None;
        if (_isPointerInsideCanvas && _isPrimaryDown)
        {
            mouseButtons |= InputMouseButtons.Primary;
        }

        InputKeyStroke[] strokes;
        lock (QueuedKeyStrokes)
        {
            strokes = [.. QueuedKeyStrokes];
            QueuedKeyStrokes.Clear();
        }

        return new InputFrame(pointer, mouseButtons, strokes, [.. HeldKeys]);
    }

    [JSExport]
    internal static Task OnMouseMove(double x, double y, double boundsWidth, double boundsHeight)
    {
        _mouseX = x;
        _mouseY = y;
        _boundsWidth = boundsWidth;
        _boundsHeight = boundsHeight;
        _isPointerInsideCanvas = true;
        return Task.CompletedTask;
    }

    [JSExport]
    internal static Task OnMouseLeave()
    {
        _isPointerInsideCanvas = false;
        return Task.CompletedTask;
    }

    [JSExport]
    internal static Task OnMouseButton(bool isPrimaryDown)
    {
        _isPrimaryDown = isPrimaryDown;
        return Task.CompletedTask;
    }

    [JSExport]
    internal static Task OnKeyDown(string code)
    {
        if (TryMapHeldKey(code, out var heldKey))
        {
            HeldKeys.Add(heldKey);
        }

        if (TryMapKeyStroke(code, out var stroke))
        {
            lock (QueuedKeyStrokes)
            {
                QueuedKeyStrokes.Enqueue(stroke);
            }
        }

        return Task.CompletedTask;
    }

    [JSExport]
    internal static Task OnKeyUp(string code)
    {
        if (TryMapHeldKey(code, out var heldKey))
        {
            HeldKeys.Remove(heldKey);
        }

        return Task.CompletedTask;
    }

    private static bool TryMapHeldKey(string code, out InputKey key)
    {
        if (code == "Space")
        {
            key = InputKey.Space;
            return true;
        }

        key = InputKey.None;
        return false;
    }

    private static bool TryMapKeyStroke(string code, out InputKeyStroke stroke)
    {
        switch (code)
        {
            case "Enter" or "NumpadEnter":
                stroke = new InputKeyStroke(InputKey.Enter, null);
                return true;
            case "Backspace":
                stroke = new InputKeyStroke(InputKey.Backspace, null);
                return true;
            case "Tab":
                stroke = new InputKeyStroke(InputKey.Tab, null);
                return true;
        }

        if (code.StartsWith("Digit", StringComparison.Ordinal) && code.Length == 6)
        {
            stroke = new InputKeyStroke(InputKey.None, code[5]);
            return true;
        }

        if (code.StartsWith("Numpad", StringComparison.Ordinal) && code.Length == 7 && char.IsDigit(code[6]))
        {
            stroke = new InputKeyStroke(InputKey.None, code[6]);
            return true;
        }

        stroke = default;
        return false;
    }
}
