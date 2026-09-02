using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Accounts;
using Sallvat.Web.Models.Account;

namespace Sallvat.Web.Controllers;

[Authorize]
[Route("conta/enderecos")]
public sealed class AddressController(IAccountService accountService) :
    Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var addresses = await accountService.ListAddressesAsync(
            CurrentUserId(),
            cancellationToken);

        return View(addresses);
    }

    [HttpGet("novo")]
    public IActionResult Create() => View(new AddressViewModel());

    [HttpPost("novo")]
    public async Task<IActionResult> Create(
        AddressViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accountService.CreateAddressAsync(
            CurrentUserId(),
            ToInput(model),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        TempData["StatusMessage"] = "Endereço adicionado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:long}/editar")]
    public async Task<IActionResult> Edit(
        long id,
        CancellationToken cancellationToken)
    {
        var address = await accountService.GetAddressAsync(
            CurrentUserId(),
            id,
            cancellationToken);

        return address is null
            ? NotFound()
            : View(ToViewModel(address));
    }

    [HttpPost("{id:long}/editar")]
    public async Task<IActionResult> Edit(
        long id,
        AddressViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accountService.UpdateAddressAsync(
            CurrentUserId(),
            id,
            ToInput(model),
            cancellationToken);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Endereço atualizado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:long}/excluir")]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken cancellationToken)
    {
        var succeeded = await accountService.DeactivateAddressAsync(
            CurrentUserId(),
            id,
            cancellationToken);
        if (!succeeded)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = "Endereço removido.";
        return RedirectToAction(nameof(Index));
    }

    private Guid CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException(
                "Authenticated user has no valid identifier.");
    }

    private void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    private static AddressInput ToInput(AddressViewModel model) =>
        new(
            model.Label,
            model.RecipientName,
            model.PostalCode,
            model.Street,
            model.Number,
            model.Complement,
            model.District,
            model.City,
            model.StateCode);

    private static AddressViewModel ToViewModel(AccountAddress address) =>
        new()
        {
            Id = address.Id,
            Label = address.Label,
            RecipientName = address.RecipientName,
            PostalCode = address.PostalCode,
            Street = address.Street,
            Number = address.Number,
            Complement = address.Complement,
            District = address.District,
            City = address.City,
            StateCode = address.StateCode,
        };
}
