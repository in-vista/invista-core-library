using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Constants = GeeksCoreLibrary.Components.Account.Models.Constants;

namespace GeeksCoreLibrary.Components.Account.Controllers;

[Area("Templates")]
public class AccountsController : Controller
{
   public AccountsController()
    {
    }
    
   /* [Route(Constants.CXmlPunchOutLoginUrl)]
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<Task> CXmlPunchOutLogin()
    {
        return await HandleControllerAction(Account.ComponentModes.CXmlPunchOutLogin);
        var account = new Account();
        return (await account.HandleCXmlPunchOutLoginModeAsync());
        
        var context = HttpContext;
        var orderProcessIdString = HttpContextHelpers.GetRequestValue(context, Constants.OrderProcessIdRequestKey);

        if (!UInt64.TryParse(orderProcessIdString, out var orderProcessId) || orderProcessId == 0)
        {
            return NotFound();
        }

        var html = await RenderAndExecuteComponentAsync(Account.ComponentModes.CXmlPunchOutLogin, orderProcessId);
        return Content(html, "text/html");
    }*/
    
}