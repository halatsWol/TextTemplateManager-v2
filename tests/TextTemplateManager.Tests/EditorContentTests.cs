using TextTemplateManager.Controls;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>Reading the editor's content: a script that couldn't run (WebView2 answers JSON null, e.g. while the app
/// closes) must never become an empty template that is then saved over the real one.</summary>
public class EditorContentTests
{
    [Fact]
    public void The_editors_html_is_returned_as_it_is()
    {
        Assert.Equal("<p>Dear APPROVER</p>", TemplateEditor.ContentFromScriptResult("\"<p>Dear APPROVER<\\/p>\""));
    }

    [Fact]
    public void An_empty_editor_still_gives_an_empty_paragraph()
    {
        Assert.Equal("<p></p>", TemplateEditor.ContentFromScriptResult("\"<p><\\/p>\""));
    }

    [Theory]
    [InlineData("null")]       // the script failed: the page is gone or not loaded
    [InlineData("\"\"")]       // never a real editor state
    [InlineData("")]
    [InlineData(null)]
    [InlineData("{not json")]
    public void An_unreadable_editor_gives_no_content(string? result)
    {
        Assert.Null(TemplateEditor.ContentFromScriptResult(result));
    }
}
