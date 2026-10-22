using System.Collections.Concurrent;

namespace Lapis.Editor.Mcp;

/// <summary>
/// Runs work from other threads on the editor's main thread, where the scene, the GPU and the UI may be used.
/// The server's request threads queue work with <see cref="InvokeAsync{T}"/>; the editor runs it once per frame with
/// <see cref="RunPending"/>.
/// </summary>
internal sealed class EditorDispatcher
{
    /// <summary>How long a request waits for the editor before giving up (e.g. while a modal dialog blocks frames).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<Action> _queue = new();

    /// <summary>Queues <paramref name="work"/> for the main thread and waits for its result (or exception).</summary>
    public async Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(() =>
        {
            if (completion.Task.IsCompleted)
            {
                return; // timed out or cancelled while waiting
            }

            try
            {
                completion.TrySetResult(work());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        try
        {
            return await completion.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            completion.TrySetCanceled(CancellationToken.None);
            throw new TimeoutException("The editor did not respond in time (is a dialog open?).");
        }
    }

    /// <summary>Runs everything queued so far. Call once per frame on the main thread.</summary>
    public void RunPending()
    {
        // Only what was queued before this call: work queued by the work itself waits for the next frame.
        for (var count = _queue.Count; count > 0 && _queue.TryDequeue(out var work); count--)
        {
            work();
        }
    }
}
