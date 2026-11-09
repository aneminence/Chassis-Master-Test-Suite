using System.Globalization;
using System.Text.RegularExpressions;

namespace Chassis_Master_Test_Suite.Communication.Remote;

/// <summary>
/// Parses Android-copied pairing strings and legacy paste shortcuts for CMTS Remote.
/// Full URI: <c>cmts://&lt;ipv4&gt;:8787/cmts/v1?token=&lt;pairingToken&gt;</c>
/// Also accepts IP-only or token-only paste (fills only that field).
/// </summary>
public static class CMTSRemoteConnectionUri
{
    public const string Scheme = "cmts";
    public const int DefaultPort = 8787;
    public const string DefaultPath = "/cmts/v1";

    private static readonly Regex Ipv4Regex = new(
        @"^(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TokenOnlyRegex = new(
        @"^[A-Za-z0-9_\-]{6,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record Parsed(
        string? Host,
        int? Port,
        string? Path,
        string? Token,
        ConnectionPasteKind Kind);

    public enum ConnectionPasteKind
    {
        Empty,
        FullUri,
        HostOnly,
        TokenOnly,
        Invalid
    }

    /// <summary>
    /// Tries to interpret <paramref name="input"/> as a connection paste.
    /// Returns false only when the input is whitespace/empty or clearly invalid.
    /// Host-only / token-only return true with partial fields.
    /// </summary>
    public static bool TryParse(string? input, out Parsed parsed)
    {
        parsed = new Parsed(null, null, null, null, ConnectionPasteKind.Empty);
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        // Strip accidental wrapping quotes / BOM from clipboard managers.
        text = text.Trim('"', '\'', '\uFEFF', '\u200B');

        if (text.StartsWith("cmts://", StringComparison.OrdinalIgnoreCase))
            return TryParseCmtsUri(text, out parsed);

        if (text.StartsWith("cmts:", StringComparison.OrdinalIgnoreCase))
        {
            parsed = new Parsed(null, null, null, null, ConnectionPasteKind.Invalid);
            return false;
        }

        // Bare ws:// is not the Android copy format; still accept for convenience.
        if (text.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var wsUri) &&
                !string.IsNullOrWhiteSpace(wsUri.Host))
            {
                var token = ExtractToken(wsUri.Query);
                var path = string.IsNullOrWhiteSpace(wsUri.AbsolutePath) || wsUri.AbsolutePath == "/"
                    ? DefaultPath
                    : wsUri.AbsolutePath;
                var port = wsUri.IsDefaultPort || wsUri.Port < 0 ? DefaultPort : wsUri.Port;
                parsed = new Parsed(wsUri.Host, port, path, token, ConnectionPasteKind.FullUri);
                return true;
            }

            parsed = new Parsed(null, null, null, null, ConnectionPasteKind.Invalid);
            return false;
        }

        if (text.StartsWith("token=", StringComparison.OrdinalIgnoreCase))
        {
            var token = text["token=".Length..].Trim();
            if (token.Length == 0)
            {
                parsed = new Parsed(null, null, null, null, ConnectionPasteKind.Invalid);
                return false;
            }

            parsed = new Parsed(null, null, null, Uri.UnescapeDataString(token), ConnectionPasteKind.TokenOnly);
            return true;
        }

        if (Ipv4Regex.IsMatch(text))
        {
            parsed = new Parsed(text, null, null, null, ConnectionPasteKind.HostOnly);
            return true;
        }

        // Dotted hostnames (e.g. phone.local) before token-only, so "a.b" is host.
        if (LooksLikeDottedHostname(text))
        {
            parsed = new Parsed(text, null, null, null, ConnectionPasteKind.HostOnly);
            return true;
        }

        // Pairing tokens are hex-ish labels without dots — prefer token over bare hostname.
        if (TokenOnlyRegex.IsMatch(text))
        {
            parsed = new Parsed(null, null, null, text, ConnectionPasteKind.TokenOnly);
            return true;
        }

        if (LooksLikeSingleLabelHostname(text))
        {
            parsed = new Parsed(text, null, null, null, ConnectionPasteKind.HostOnly);
            return true;
        }

        parsed = new Parsed(null, null, null, null, ConnectionPasteKind.Invalid);
        return false;
    }

    /// <summary>
    /// Applies a successful parse onto editable field defaults (non-null parts win).
    /// </summary>
    public static void ApplyTo(
        Parsed parsed,
        ref string host,
        ref string port,
        ref string path,
        ref string token)
    {
        if (!string.IsNullOrWhiteSpace(parsed.Host))
            host = parsed.Host!;
        if (parsed.Port is int p && p > 0 && p <= 65535)
            port = p.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(parsed.Path))
            path = parsed.Path!;
        if (parsed.Token is not null)
            token = parsed.Token;
    }

    private static bool TryParseCmtsUri(string text, out Parsed parsed)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            parsed = new Parsed(null, null, null, null, ConnectionPasteKind.Invalid);
            return false;
        }

        // Unknown schemes often report Port=-1 when unspecified.
        var port = uri.Port < 0 || uri.IsDefaultPort ? DefaultPort : uri.Port;

        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            path = DefaultPath;

        var token = ExtractToken(uri.Query);
        parsed = new Parsed(uri.Host, port, path, token, ConnectionPasteKind.FullUri);
        return true;
    }

    private static string? ExtractToken(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = part[..eq];
            if (!key.Equals("token", StringComparison.OrdinalIgnoreCase))
                continue;
            return Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        return null;
    }

    private static bool LooksLikeDottedHostname(string text)
    {
        if (!text.Contains('.', StringComparison.Ordinal))
            return false;
        if (text.Contains('/') || text.Contains('?') || text.Contains(' ') || text.Contains(':'))
            return false;
        if (text.Length is < 3 or > 253)
            return false;
        return text.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');
    }

    private static bool LooksLikeSingleLabelHostname(string text)
    {
        if (text.Contains('.') || text.Contains('/') || text.Contains('?') ||
            text.Contains(' ') || text.Contains(':'))
            return false;
        if (text.Length is < 1 or > 63)
            return false;
        // Require a hyphen so we don't steal short alnum tokens already handled above.
        return text.Contains('-') &&
               text.Any(char.IsLetter) &&
               text.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
    }
}
