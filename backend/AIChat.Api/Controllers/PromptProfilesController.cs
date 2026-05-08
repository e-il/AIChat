using Microsoft.AspNetCore.Mvc;
using AIChat.Api.Models;
using AIChat.Api.Services;

namespace AIChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PromptProfilesController : ControllerBase
{
    private readonly IPromptProfileRegistry _promptProfiles;

    public PromptProfilesController(IPromptProfileRegistry promptProfiles)
    {
        _promptProfiles = promptProfiles;
    }

    [HttpGet]
    public ActionResult<PromptProfilesResponse> GetPromptProfiles()
    {
        return Ok(new PromptProfilesResponse
        {
            Profiles = _promptProfiles.GetBuiltIns().ToList(),
            MaxCustomSystemPromptLength = _promptProfiles.MaxCustomSystemPromptLength
        });
    }
}
