using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Infrastructure.Notifications;

/// Renders the embedded HTML templates: {{Key}} placeholders (HTML-encoded) and
/// {{#Key}}...{{/Key}} sections that disappear when the value is empty.
/// Each template starts with "&lt;!-- subject: ... --&gt;" and is wrapped in _layout.html.
internal sealed partial class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private const string LayoutKey = "_layout";
    private readonly ConcurrentDictionary<string, string> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _layout;

    public EmailTemplateRenderer()
    {
        var assembly = typeof(EmailTemplateRenderer).Assembly;
        const string prefix = "WonderFleet.Infrastructure.Notifications.Templates.";
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var key = name[prefix.Length..^".html".Length];
            _templates[key] = Read(assembly, name);
        }
        _layout = _templates.TryGetValue(LayoutKey, out var layout)
            ? layout
            : "<html><body>{{Content}}</body></html>";
    }

    [GeneratedRegex(@"\{\{#(?<key>[A-Za-z0-9_]+)\}\}(?<body>.*?)\{\{/\k<key>\}\}", RegexOptions.Singleline)]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"\{\{(?<key>[A-Za-z0-9_]+)\}\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^\s*<!--\s*subject:(?<subject>.*?)-->\s*", RegexOptions.Singleline)]
    private static partial Regex SubjectPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinePattern();

    public RenderedEmail Render(string templateKey, IReadOnlyDictionary<string, string?> model)
    {
        if (!_templates.TryGetValue(templateKey, out var template))
            throw new InvalidOperationException($"Email template '{templateKey}' was not found.");

        var subjectMatch = SubjectPattern().Match(template);
        var subjectTemplate = subjectMatch.Success ? subjectMatch.Groups["subject"].Value.Trim() : "WonderFleet notification";
        var body = subjectMatch.Success ? template[subjectMatch.Length..] : template;

        var content = Apply(body, model);
        var html = Apply(_layout.Replace("{{Content}}", content, StringComparison.Ordinal), model);
        var subject = WebUtility.HtmlDecode(Apply(subjectTemplate, model)).Trim();
        return new RenderedEmail(subject, html, ToText(content));
    }

    private static string Apply(string template, IReadOnlyDictionary<string, string?> model)
    {
        var withSections = SectionPattern().Replace(template, m =>
        {
            var has = model.TryGetValue(m.Groups["key"].Value, out var v) && !string.IsNullOrWhiteSpace(v);
            return has ? m.Groups["body"].Value : string.Empty;
        });

        return PlaceholderPattern().Replace(withSections, m =>
        {
            var key = m.Groups["key"].Value;
            if (key == "Content") return m.Value;
            return model.TryGetValue(key, out var value) && value is not null ? WebUtility.HtmlEncode(value) : string.Empty;
        });
    }

    /// Plain-text alternative so the message is not spam-scored as HTML-only.
    internal static string ToText(string html)
    {
        var text = html
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</tr>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</td><td", " : <td", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);
        text = TagPattern().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n').Select(l => l.Trim());
        return BlankLinePattern().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
