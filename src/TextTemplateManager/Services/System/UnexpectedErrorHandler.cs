using System;
using System.Threading;
using System.Threading.Tasks;

namespace TextTemplateManager.Services.System;

/// <summary>
/// What happens when an exception reaches the global handler. Every one is logged. The first one in a
/// session also saves pending work and asks whether to quit, because the app may now be in an
/// inconsistent state; later ones (including any raised while that question is open) are only logged.
/// Free of WinUI so it can be tested; App supplies the real log, save, prompt and exit.
/// </summary>
public sealed class UnexpectedErrorHandler
{
    private readonly Action<Exception> _log;
    private readonly Func<Task> _persist;
    private readonly Func<bool, Task<bool>> _askQuit;   // argument: pending work was saved; true = quit
    private readonly Func<Task> _quit;
    private int _reported;

    public UnexpectedErrorHandler(Action<Exception> log, Func<Task> persist, Func<bool, Task<bool>> askQuit, Func<Task> quit)
    {
        _log = log;
        _persist = persist;
        _askQuit = askQuit;
        _quit = quit;
    }

    public async Task HandleAsync(Exception ex)
    {
        _log(ex);
        if (Interlocked.Exchange(ref _reported, 1) == 1) return;

        bool saved = true;
        try { await _persist(); }
        catch (Exception saveError)
        {
            saved = false;
            _log(saveError);
        }

        if (await _askQuit(saved))
            await _quit();
    }
}
