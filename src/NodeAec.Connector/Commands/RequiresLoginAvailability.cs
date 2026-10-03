using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Auth;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Command availability: enables the button only when an account is connected.
/// Used by the "Meus Plugins" button to stay disabled before login.
/// Local read, no network (safe for frequent ribbon calls).
/// </summary>
public class RequiresLoginAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        return LoginRequirement.IsLoggedIn();
    }
}
