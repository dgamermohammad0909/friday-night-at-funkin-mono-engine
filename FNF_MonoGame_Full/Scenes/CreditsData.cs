using System.Collections.Generic;

namespace FNF_MonoGame.Scenes;

public class CreditsData
{
    public List<CreditsEntryData> Entries { get; set; } = new();

    public static CreditsData CreateFallback()
    {
        return new CreditsData
        {
            Entries = new List<CreditsEntryData>
            {
                new CreditsEntryData
                {
                    Header = "Founders",
                    Body = new List<CreditsEntryLine>
                    {
                        new() { Line = "ninjamuffin99" },
                        new() { Line = "PhantomArcade" },
                        new() { Line = "Kawai Sprite" },
                        new() { Line = "evilsk8r" }
                    }
                }
            }
        };
    }
}

public class CreditsEntryData
{
    public string Header { get; set; }
    public List<CreditsEntryLine> Body { get; set; } = new();
}

public class CreditsEntryLine
{
    public string Line { get; set; }
}
