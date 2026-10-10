using TextTemplateManager.Services.Platform;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The global error handler used to log and silently keep going. It now saves, tells the user
/// once per session and lets them quit.</summary>
public class UnexpectedErrorHandlerTests
{
    private readonly List<string> _calls = new();
    private readonly List<Exception> _logged = new();
    private bool _quitAnswer;
    private bool? _savedFlag;
    private Func<Task> _persist;
    private Func<Task<bool>>? _ask;

    public UnexpectedErrorHandlerTests()
    {
        _persist = () => { _calls.Add("persist"); return Task.CompletedTask; };
    }

    private UnexpectedErrorHandler Create() => new(
        ex => { _logged.Add(ex); _calls.Add("log"); },
        () => _persist(),
        saved =>
        {
            _savedFlag = saved;
            _calls.Add("ask");
            return _ask?.Invoke() ?? Task.FromResult(_quitAnswer);
        },
        () => { _calls.Add("quit"); return Task.CompletedTask; });

    [Fact]
    public async Task First_error_is_logged_saved_then_asked()
    {
        await Create().HandleAsync(new InvalidOperationException("boom"));

        Assert.Equal(new[] { "log", "persist", "ask" }, _calls);
        Assert.True(_savedFlag);
    }

    [Fact]
    public async Task Quit_answer_quits()
    {
        _quitAnswer = true;

        await Create().HandleAsync(new InvalidOperationException());

        Assert.Equal("quit", _calls[^1]);
    }

    [Fact]
    public async Task Keep_running_answer_does_not_quit()
    {
        _quitAnswer = false;

        await Create().HandleAsync(new InvalidOperationException());

        Assert.DoesNotContain("quit", _calls);
    }

    [Fact]
    public async Task Later_errors_are_only_logged()
    {
        var sut = Create();

        await sut.HandleAsync(new InvalidOperationException("first"));
        await sut.HandleAsync(new InvalidOperationException("second"));

        Assert.Equal(2, _logged.Count);
        Assert.Single(_calls, c => c == "ask");
        Assert.Single(_calls, c => c == "persist");
    }

    [Fact]
    public async Task Error_while_the_question_is_open_is_only_logged()
    {
        var answer = new TaskCompletionSource<bool>();
        _ask = () => answer.Task;
        var sut = Create();

        var first = sut.HandleAsync(new InvalidOperationException("first"));
        await sut.HandleAsync(new InvalidOperationException("during prompt"));
        answer.SetResult(false);
        await first;

        Assert.Equal(2, _logged.Count);
        Assert.Single(_calls, c => c == "ask");
    }

    [Fact]
    public async Task Failed_save_is_logged_and_still_asks()
    {
        var saveError = new IOException("disk gone");
        _persist = () => throw saveError;

        await Create().HandleAsync(new InvalidOperationException());

        Assert.Contains(saveError, _logged);
        Assert.False(_savedFlag);
        Assert.Contains("ask", _calls);
    }
}
