using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeAec.Connector.UI;

/// <summary>
/// Planned action for the two small stacked buttons of the "Conector" panel
/// (M7): translates the set of names already on the panel into a single command.
/// </summary>
internal enum StackedInsertion
{
    /// <summary>Both already exist — nothing to do (idempotency).</summary>
    None,

    /// <summary>Neither exists — stack both with <c>AddStackedItems</c>.</summary>
    StackBoth,

    /// <summary>Only the first is missing — add it standalone.</summary>
    AddFirstOnly,

    /// <summary>Only the second is missing — add it standalone.</summary>
    AddSecondOnly,
}

/// <summary>
/// Pure ribbon idempotency decisions (M7): which buttons to add given the items
/// already on the panel. No Revit/WPF types so they run in the headless test
/// project — <c>App.AddButtonIfMissing</c>/<c>AddStackedButtonsIfMissing</c> consume
/// only the result here and execute the UI commands.
/// </summary>
internal static class RibbonDecisions
{
    /// <summary>
    /// Checks for a name on the panel with the same ribbon semantics
    /// (<see cref="StringComparison.OrdinalIgnoreCase"/>), used in idempotency tests.
    /// </summary>
    /// <param name="existingNames">Names of the items already on the panel.</param>
    /// <param name="candidate">Name being looked up.</param>
    /// <returns><c>true</c> when it already exists (must not be recreated).</returns>
    internal static bool ContainsName(IEnumerable<string> existingNames, string candidate)
    {
        return existingNames.Any(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Plans the insertion of the two stacked buttons: both missing → stack;
    /// only one missing → add it standalone; none missing → nothing. Name
    /// comparison is case-insensitive, like the Revit panel.
    /// </summary>
    /// <param name="existingNames">Names of the items already on the panel.</param>
    /// <param name="firstName">First (stacked) button name.</param>
    /// <param name="secondName">Second (stacked) button name.</param>
    /// <returns>Command to run on the panel.</returns>
    internal static StackedInsertion PlanStackedInsertion(
        IEnumerable<string> existingNames,
        string firstName,
        string secondName)
    {
        bool firstExists = ContainsName(existingNames, firstName);
        bool secondExists = ContainsName(existingNames, secondName);

        if (firstExists && secondExists) return StackedInsertion.None;
        if (!firstExists && !secondExists) return StackedInsertion.StackBoth;
        return firstExists ? StackedInsertion.AddSecondOnly : StackedInsertion.AddFirstOnly;
    }
}
