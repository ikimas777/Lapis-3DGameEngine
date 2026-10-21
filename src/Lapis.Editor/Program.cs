namespace Lapis.Editor;

internal static class Program
{
    private static int Main(string[] args)
    {
        EditorLaunchOptions? options;
        try
        {
            options = EditorLaunchOptions.Parse(args);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(EditorLaunchOptions.Usage);
            return 2;
        }

        if (options is null)
        {
            Console.WriteLine(EditorLaunchOptions.Usage);
            return 0;
        }

        try
        {
            using var app = new EditorApplication(options);
            app.Run();
            return 0;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "The editor stopped because of an unhandled error.");
            return 1;
        }
    }
}
