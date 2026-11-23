namespace Chat.Server;

/// <summary>极简控制台日志。</summary>
internal static class Log
{
    private static readonly Lock Gate = new();

    public static bool Verbose { get; set; }

    public static void Info(string message) => Write(ConsoleColor.Gray, "INFO ", message);

    public static void Warn(string message) => Write(ConsoleColor.Yellow, "WARN ", message);

    public static void Error(string message) => Write(ConsoleColor.Red, "ERROR", message);

    public static void Debug(string message)
    {
        if (Verbose)
        {
            Write(ConsoleColor.DarkGray, "DEBUG", message);
        }
    }

    private static void Write(ConsoleColor color, string level, string message)
    {
        lock (Gate)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write($"{DateTime.Now:HH:mm:ss} {level} ");
            Console.ForegroundColor = previous;
            Console.WriteLine(message);
        }
    }
}
