using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PutDoc.Services;

public class InitializationService
{
    public string expect = "putdoc.js [2025-11-13-F]";
    public string stamp = "NOT FOUND";

    public bool ? FailCondition = null;

    private NavigationManager Nav;
    private IJSRuntime JS;
    
    public InitializationService(NavigationManager nav, IJSRuntime js)
    {
        Nav = nav;
        JS = js;
    }
    
    public async Task CheckInit()
    {
        if (FailCondition == false) return;

        if (FailCondition == null)
        {

            bool error = true;
            try
            {
                if ((stamp = await JS.InvokeAsync<String>("window.getTimeStamp")) == expect) error = false;
            }
            catch (Exception e)
            {
            }

            FailCondition = error;
        }

        if (FailCondition.Value) Nav.NavigateTo("/stale");
    }
}