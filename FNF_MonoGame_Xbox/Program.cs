namespace FNF_MonoGame_Xbox;

/// <summary>
/// Entry point for FNF MonoGame Xbox Test
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var game = new FNFGame();
        game.Run();
    }
}
