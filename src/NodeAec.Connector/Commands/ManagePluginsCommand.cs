using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.UI;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Revit command that opens the "Meus Plugins" window with the account-linked products.
/// Unavailable on the ribbon until login (see <see cref="RequiresLoginAvailability"/>).
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ManagePluginsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        PluginsWindow.Open();
        return Result.Succeeded;
    }
}
