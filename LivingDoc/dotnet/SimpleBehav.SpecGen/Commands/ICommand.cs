namespace SimpleBehav.SpecGen.Commands;

internal interface ICommand
{
    Task<int> ExecuteAsync();
}
