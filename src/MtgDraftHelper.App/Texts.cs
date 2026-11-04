using System.Windows;
using MtgDraftHelper.Core.Cards;

namespace MtgDraftHelper.App;

/// <summary>Texty UI z Resources/Strings.*.xaml.</summary>
public static class Texts
{
    public static string Get(string key, params object?[] args)
    {
        var text = Application.Current?.TryFindResource(key) as string ?? key;
        return args.Length == 0 ? text : string.Format(text, args);
    }

    public static string ColorName(ManaColors color) => Get("Color." + ManaCost.ToLetters(color));
}
