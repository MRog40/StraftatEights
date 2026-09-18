using System;
using System.Collections.Generic;
using System.Text;

namespace StraftatEightsPlugin;

internal static class PlayerNameMarkup
{
    private static readonly HashSet<string> PairedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "alpha",
        "allcaps",
        "b",
        "cspace",
        "color",
        "font",
        "gradient",
        "g",
        "i",
        "indent",
        "line-height",
        "line-indent",
        "link",
        "lowercase",
        "margin",
        "mark",
        "material",
        "mspace",
        "noparse",
        "nobr",
        "rotate",
        "s",
        "size",
        "smallcaps",
        "space",
        "style",
        "sub",
        "sup",
        "u",
        "uppercase",
        "voffset",
        "width"
    };

    internal static string Truncate(string text, int maxVisibleCharacters)
    {
        if (string.IsNullOrEmpty(text) || maxVisibleCharacters <= 0)
        {
            return string.Empty;
        }

        StringBuilder result = new(text.Length);
        Stack<string> openTags = new();
        int visibleCharacters = 0;
        int index = 0;
        while (index < text.Length && visibleCharacters < maxVisibleCharacters)
        {
            if (TryReadTag(text, index, out int tagEnd, out string tagName,
                    out bool isClosing, out bool isSelfClosing))
            {
                result.Append(text, index, tagEnd - index + 1);
                if (PairedTags.Contains(tagName))
                {
                    if (isClosing)
                    {
                        if (openTags.Count > 0 && string.Equals(openTags.Peek(), tagName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            openTags.Pop();
                        }
                    }
                    else if (!isSelfClosing)
                    {
                        openTags.Push(tagName);
                    }
                }

                index = tagEnd + 1;
                continue;
            }

            result.Append(text[index]);
            visibleCharacters++;
            index++;
        }

        while (openTags.Count > 0)
        {
            result.Append("</").Append(openTags.Pop()).Append('>');
        }

        return result.ToString();
    }

    internal static int VisibleLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int visibleCharacters = 0;
        int index = 0;
        while (index < text.Length)
        {
            if (TryReadTag(text, index, out int tagEnd, out _, out _, out _))
            {
                index = tagEnd + 1;
                continue;
            }

            visibleCharacters++;
            index++;
        }

        return visibleCharacters;
    }

    private static bool TryReadTag(string text, int start, out int tagEnd,
        out string tagName, out bool isClosing, out bool isSelfClosing)
    {
        tagEnd = -1;
        tagName = string.Empty;
        isClosing = false;
        isSelfClosing = false;
        if (text[start] != '<')
        {
            return false;
        }

        tagEnd = text.IndexOf('>', start + 1);
        if (tagEnd < 0)
        {
            return false;
        }

        int contentStart = start + 1;
        int contentEnd = tagEnd;
        while (contentStart < contentEnd && char.IsWhiteSpace(text[contentStart]))
        {
            contentStart++;
        }

        if (contentStart == contentEnd)
        {
            return false;
        }

        if (text[contentStart] == '/')
        {
            isClosing = true;
            contentStart++;
            while (contentStart < contentEnd && char.IsWhiteSpace(text[contentStart]))
            {
                contentStart++;
            }
        }

        if (contentStart == contentEnd)
        {
            return false;
        }

        int nameEnd = contentStart;
        while (nameEnd < contentEnd && !char.IsWhiteSpace(text[nameEnd])
            && text[nameEnd] != '=' && text[nameEnd] != '/')
        {
            nameEnd++;
        }

        if (nameEnd == contentStart)
        {
            return false;
        }

        tagName = text.Substring(contentStart, nameEnd - contentStart);
        int lastContentCharacter = contentEnd - 1;
        while (lastContentCharacter >= contentStart
            && char.IsWhiteSpace(text[lastContentCharacter]))
        {
            lastContentCharacter--;
        }

        isSelfClosing = !isClosing && lastContentCharacter >= contentStart
            && text[lastContentCharacter] == '/';
        return PairedTags.Contains(tagName);
    }
}