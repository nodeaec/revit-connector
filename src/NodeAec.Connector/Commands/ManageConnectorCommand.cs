using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.UI;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Revit command that opens the Node.aec Connector "Minha Conta" window.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ManageConnectorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ConnectorWindow.Open();
        return Result.Succeeded;
    }
}
