using SimpleBehav.SpecGen.Commands;

var command = CommandLine.TryParse(args, out var error);
if (command is null)
{
    if (error is not null) Console.Error.WriteLine(error);
    else CommandLine.PrintUsage();
    return 1;
}

return await command.ExecuteAsync();
