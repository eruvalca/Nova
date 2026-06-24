using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Displays a status or error message from a cookie or parameter. Messages are removed from the cookie after display.
/// </summary>
public partial class StatusMessage
{
    /// <summary>
    /// Stores the status message read from the Identity status cookie, if present.
    /// </summary>
    private string? messageFromCookie;

    /// <summary>
    /// Gets or sets the status message to display.
    /// </summary>
    [Parameter]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the cascading HTTP context used to access cookies.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets the display message from either the parameter or the cookie.
    /// </summary>
    protected string? DisplayMessage => Message ?? messageFromCookie;

    /// <summary>
    /// Initializes the component by reading the status message from the cookie and clearing it from the response.
    /// </summary>
    protected override void OnInitialized()
    {
        messageFromCookie = HttpContext.Request.Cookies[IdentityRedirectManager.StatusCookieName];

        if (messageFromCookie is not null)
        {
            HttpContext.Response.Cookies.Delete(IdentityRedirectManager.StatusCookieName);
        }
    }
}
