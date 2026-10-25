using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>Routes OpenGL debug output (KHR_debug) to the <see cref="Log"/>.</summary>
internal static class GLDebug
{
    // Kept in a field so the delegate is not collected while the driver holds a pointer to it.
    private static DebugProc? _callback;

    public static unsafe void Enable(GL gl)
    {
        _callback = OnDebugMessage;
        gl.Enable(EnableCap.DebugOutput);
        gl.Enable(EnableCap.DebugOutputSynchronous);
        gl.DebugMessageCallback(_callback, null);
    }

    private static void OnDebugMessage(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        // Notifications and driver performance hints (e.g. "shader recompiled due to state change") are not actionable.
        if (severity == GLEnum.DebugSeverityNotification || type == GLEnum.DebugTypePerformance)
        {
            return;
        }

        var text = $"OpenGL [{type}] {Marshal.PtrToStringAnsi(message, length)}";
        if (severity == GLEnum.DebugSeverityHigh || type == GLEnum.DebugTypeError)
        {
            Diagnostics.EngineMetrics.GLError();
            Log.Error(text);
        }
        else
        {
            Log.Warning(text);
        }
    }
}
