using Microsoft.Extensions.Logging;

namespace Ipfs.Engine;

/// <summary>
/// The <see cref="TaskExtensions"/> class provides extension methods for the <see cref="Task"/> class.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Forgets the specified task.
    /// </summary>
    /// <param name="task">The task.</param>
    public static void Forget(this Task task)
    {
        _ = task.ContinueWith(
            t =>
            {
                ILogger logger = IpfsEngine.LoggerFactory.CreateLogger("TaskExtensions");
                logger.LogError(t.Exception, "Unobserved task exception");
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Forgets the specified handler.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="handler">The handler.</param>
    public static void Forget(this Task task, Action<Exception> handler)
    {
        _ = task.ContinueWith(
            (t) =>
            {
                if (t.Exception is not null)
                {
                    handler(t.Exception.InnerException!);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}