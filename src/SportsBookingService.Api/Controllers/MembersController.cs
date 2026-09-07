using Microsoft.AspNetCore.Mvc;
using SportsBookingService.Api.DTOs;
using SportsBookingService.Api.Services;

namespace SportsBookingService.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/members/{memberId:guid}")]
public class MembersController(DebtService debtService) : ControllerBase
{
    [HttpGet("debts-owed")]
    public async Task<ActionResult<List<DebtResponse>>> GetDebtsOwed(Guid groupId, Guid memberId)
    {
        var result = await debtService.GetOwedToMemberAsync(groupId, memberId);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpPost("debts-owed/settle")]
    public async Task<ActionResult<DebtResponse>> SettleDebt(Guid groupId, Guid memberId, SettleDebtRequest request)
    {
        var result = await debtService.SettleAsync(groupId, memberId, request);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }
}
