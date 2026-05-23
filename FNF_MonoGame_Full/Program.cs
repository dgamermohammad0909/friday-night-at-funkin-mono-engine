namespace FNF_MonoGame;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var game = new FNFGame();
        game.Run();
    }
}
