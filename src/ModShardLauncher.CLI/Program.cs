using System.Text;
using ModShardLauncher.CLI;

try
{
    Console.OutputEncoding = new UTF8Encoding(false);
}
catch
{
    // Redirected stdio may not support encoding changes.
}

return await CliApplication.RunAsync(args);
