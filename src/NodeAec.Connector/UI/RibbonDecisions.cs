using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeAec.Connector.UI;

/// <summary>
/// Pure ribbon idempotency decisions: which buttons to add given the items
/// already on the panel. No Revit/WPF types so they run in the headless test
/// project — <c>App.AddButtonIfMissing</c> consumes only the result here and executes the UI commands.
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
}
