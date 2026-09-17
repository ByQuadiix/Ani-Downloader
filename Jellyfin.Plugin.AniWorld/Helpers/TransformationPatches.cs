using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AniWorld.Helpers;

/// <summary>
/// Static callbacks for the File Transformation plugin.
/// </summary>
public static class TransformationPatches
{
    private const string PluginName = "AniWorld Downloader";
    private const string ScriptSrc = "../AniWorld/InjectionScript";

    /// <summary>
    /// Patches index.html to inject the AniWorld sidebar script.
    /// </summary>
    public static string IndexHtml(PatchRequestPayload content)
    {
        if (string.IsNullOrEmpty(content.Contents))
        {
            return content.Contents ?? string.Empty;
        }

        var scriptTag = $"<script plugin=\"{PluginName}\" src=\"{ScriptSrc}\" defer></script>";

        // Remove any existing AniWorld script tags (idempotent, handles varying attribute order/spacing)
        var updatedContent = RemoveScript(content.Contents);

        // Inject before </body>
        if (updatedContent.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            var bodyIndex = updatedContent.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            return updatedContent.Insert(bodyIndex, $"{scriptTag}\n");
        }

        return updatedContent;
    }

    /// <summary>
    /// Removes the AniWorld script tag from HTML content.
    /// </summary>
    public static string RemoveScript(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        // Matches by plugin name attribute or by the AniWorld InjectionScript src path
        var regex = new Regex(
            $"<script[^>]*(?:plugin=[\"']{Regex.Escape(PluginName)}[\"']|src=[\"'][^\"']*{Regex.Escape(ScriptSrc)}[\"'])[^>]*>\\s*</script>\\n?",
            RegexOptions.IgnoreCase);

        return regex.Replace(content, string.Empty);
    }
}
