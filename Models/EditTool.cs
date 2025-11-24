using System;

namespace MiniScheditor.Models;

public abstract class EditTool
{
    public abstract string Name { get; }

    /// <summary>
    /// Whether pressing ESC should revert this tool back to <see cref="SelectTool"/>.
    /// </summary>
    public virtual bool ResetToSelectOnEscape => true;
}

public sealed class SelectTool : EditTool
{
    private SelectTool()
    {
    }

    public static SelectTool Instance { get; } = new SelectTool();

    public override string Name => "Select";

    public override bool ResetToSelectOnEscape => false;
}

public sealed class WireTool : EditTool
{
    public override string Name => "Wire";
}

public sealed class ComponentPlacementTool : EditTool
{
    public Symbol Symbol { get; }

    public ComponentPlacementTool(Symbol symbol)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
    }

    public override string Name => Symbol.Name;
}
