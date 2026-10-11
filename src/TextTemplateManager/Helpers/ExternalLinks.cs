using System;
using System.Diagnostics;

namespace TextTemplateManager.Helpers
{
    public static class ExternalLinks
    {
        /// <summary>Opens a URL or local file with the shell's default handler.</summary>
        public static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"[Links] open failed: {ex.Message}"); }
        }

        /// <summary>Opens a link from template content in the system browser. Restricted to web/mail/tel
        /// schemes so a template can't launch file:// or a custom protocol handler via a click.</summary>
        public static void OpenWebLink(string? href)
        {
            if (string.IsNullOrWhiteSpace(href)) return;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme is "http" or "https" or "mailto" or "tel" or "ftp" or "ftps")
                Open(uri.AbsoluteUri);
        }
    }
}
