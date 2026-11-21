using System.Text.RegularExpressions;

namespace Botticelli.Framework.ShortP2P.Commands;

/// <summary>
/// Same command shape as <c>Botticelli.Framework.Commands.Utils.CommandUtils</c>:
/// <c>/command</c> or <c>/command args</c>. The name is ASCII letters and digits.
/// </summary>
public static class BotticelliCommandText
{
    private static readonly Regex SimpleCommand = new(
        @"\/([a-zA-Z0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CommandWithArguments = new(
        @"\/([a-zA-Z0-9]*) (.*)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsCommand(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return false;

        return SimpleCommand.IsMatch(body) || CommandWithArguments.IsMatch(body);
    }
}
