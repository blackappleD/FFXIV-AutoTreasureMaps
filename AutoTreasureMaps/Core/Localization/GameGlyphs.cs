using System.Threading;

namespace AutoTreasureMaps.Core.Localization;

// Names read from the game data (maps, items, places, NPCs) spell ideographs the language file never uses, and the
// font atlas bakes only the glyphs it is told about; ImGui draws the rest as its fallback mark. Every such name passes
// through Cover, and the fonts rebuild once the set of glyphs has grown.
internal static class GameGlyphs
{
    private static readonly object Gate = new();
    private static readonly bool[] Present = new bool[GlyphRanges.CodepointCount];
    private static int version;

    public static int Version => Volatile.Read(ref version);

    public static string Cover(string text)
    {
        if (string.IsNullOrEmpty(text) || !HasNewGlyph(text))
        {
            return text;
        }

        lock (Gate)
        {
            if (GlyphRanges.MarkText(Present, text))
            {
                Interlocked.Increment(ref version);
            }
        }

        return text;
    }

    public static void MarkInto(bool[] target)
    {
        lock (Gate)
        {
            for (var codepoint = GlyphRanges.FirstNonAsciiCodepoint; codepoint < GlyphRanges.CodepointCount; codepoint++)
            {
                if (Present[codepoint])
                {
                    target[codepoint] = true;
                }
            }
        }
    }

    // Names are covered on every frame they are drawn, so the common case of nothing new takes no lock.
    private static bool HasNewGlyph(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var codepoint = text[index];
            if (codepoint >= GlyphRanges.FirstNonAsciiCodepoint && !char.IsSurrogate(codepoint) && !Volatile.Read(ref Present[codepoint]))
            {
                return true;
            }
        }

        return false;
    }
}
