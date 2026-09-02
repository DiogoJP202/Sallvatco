using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sallvat.Application.Authorization;
using Sallvat.Application.Catalog;
using Sallvat.Web.Models.Catalog;

namespace Sallvat.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = RoleNames.Admin)]
[Route("Admin/Produtos")]
public sealed class ProductsController(ICatalogService catalogService) :
    Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken) =>
        View(await catalogService.ListAdminAsync(cancellationToken));

    [HttpGet("novo")]
    public IActionResult Create() => View(new ProductFormViewModel());

    [HttpPost("novo")]
    public async Task<IActionResult> Create(
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await catalogService.CreateProductAsync(
            model.ToInput(),
            Operation(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["StatusMessage"] = "Produto criado como rascunho.";
        return RedirectToAction(nameof(Edit), new { id = result.EntityId });
    }

    [HttpGet("{id:long}/editar")]
    public async Task<IActionResult> Edit(
        long id,
        CancellationToken cancellationToken)
    {
        var product = await catalogService.GetAdminAsync(
            id,
            cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        ViewData["ProductDetails"] = product;
        return View(ProductFormViewModel.From(product));
    }

    [HttpPost("{id:long}/editar")]
    public async Task<IActionResult> Edit(
        long id,
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await LoadProductDetailsAsync(id, cancellationToken);
            return View(model);
        }

        var result = await catalogService.UpdateProductAsync(
            id,
            model.ConcurrencyVersion,
            model.ToInput(),
            Operation(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            await LoadProductDetailsAsync(id, cancellationToken);
            return View(model);
        }

        TempData["StatusMessage"] = "Produto atualizado.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("{productId:long}/variantes/nova")]
    public async Task<IActionResult> CreateVariant(
        long productId,
        CancellationToken cancellationToken)
    {
        if (await catalogService.GetAdminAsync(productId, cancellationToken)
            is null)
        {
            return NotFound();
        }

        return View(new VariantFormViewModel { ProductId = productId });
    }

    [HttpPost("{productId:long}/variantes/nova")]
    public async Task<IActionResult> CreateVariant(
        long productId,
        VariantFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (productId != model.ProductId)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await catalogService.AddVariantAsync(
            productId,
            model.ToInput(),
            Operation(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["StatusMessage"] = "Variante adicionada.";
        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    [HttpGet("{productId:long}/variantes/{variantId:long}/editar")]
    public async Task<IActionResult> EditVariant(
        long productId,
        long variantId,
        CancellationToken cancellationToken)
    {
        var product = await catalogService.GetAdminAsync(
            productId,
            cancellationToken);
        var variant = product?.Variants.SingleOrDefault(
            item => item.Id == variantId);

        return variant is null
            ? NotFound()
            : View(VariantFormViewModel.From(productId, variant));
    }

    [HttpPost("{productId:long}/variantes/{variantId:long}/editar")]
    public async Task<IActionResult> EditVariant(
        long productId,
        long variantId,
        VariantFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (productId != model.ProductId || variantId != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await catalogService.UpdateVariantAsync(
            productId,
            variantId,
            model.ConcurrencyVersion,
            model.ToInput(),
            Operation(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["StatusMessage"] = "Variante atualizada.";
        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    [HttpGet("{productId:long}/variantes/{variantId:long}/estoque")]
    public async Task<IActionResult> Stock(
        long productId,
        long variantId,
        CancellationToken cancellationToken)
    {
        var product = await catalogService.GetAdminAsync(
            productId,
            cancellationToken);
        var variant = product?.Variants.SingleOrDefault(
            item => item.Id == variantId);
        if (variant is null)
        {
            return NotFound();
        }

        return View(new StockAdjustmentViewModel
        {
            ProductId = productId,
            VariantId = variantId,
            Sku = variant.Sku,
            CurrentOnHand = variant.OnHand,
            Reserved = variant.Reserved,
            NewOnHand = variant.OnHand,
            ConcurrencyVersion = variant.ConcurrencyVersion,
            Movements = await catalogService.ListMovementsAsync(
                variantId,
                cancellationToken),
        });
    }

    [HttpPost("{productId:long}/variantes/{variantId:long}/estoque")]
    public async Task<IActionResult> Stock(
        long productId,
        long variantId,
        StockAdjustmentViewModel model,
        CancellationToken cancellationToken)
    {
        if (productId != model.ProductId || variantId != model.VariantId)
        {
            return BadRequest();
        }

        if (ModelState.IsValid)
        {
            var result = await catalogService.AdjustStockAsync(
                productId,
                variantId,
                model.ConcurrencyVersion,
                model.NewOnHand,
                model.Reason,
                Operation(),
                cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = "Estoque ajustado e auditado.";
                return RedirectToAction(
                    nameof(Stock),
                    new { productId, variantId });
            }

            AddErrors(result);
        }

        model.Movements = await catalogService.ListMovementsAsync(
            variantId,
            cancellationToken);
        return View(model);
    }

    [HttpPost("{id:long}/publicar")]
    public Task<IActionResult> Publish(
        long id,
        Guid concurrencyVersion,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(
            id,
            catalogService.PublishAsync(
                id,
                concurrencyVersion,
                Operation(),
                cancellationToken),
            "Produto publicado.");

    [HttpPost("{id:long}/arquivar")]
    public Task<IActionResult> Archive(
        long id,
        Guid concurrencyVersion,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(
            id,
            catalogService.ArchiveAsync(
                id,
                concurrencyVersion,
                Operation(),
                cancellationToken),
            "Produto arquivado.");

    [HttpPost("{id:long}/destaque")]
    public Task<IActionResult> SetFeatured(
        long id,
        Guid concurrencyVersion,
        bool isFeatured,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(
            id,
            catalogService.SetFeaturedAsync(
                id,
                concurrencyVersion,
                isFeatured,
                Operation(),
                cancellationToken),
            isFeatured
                ? "Produto destacado."
                : "Produto removido dos destaques.");

    private AdminOperationContext Operation()
    {
        var actorValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(actorValue, out var actorUserId))
        {
            throw new InvalidOperationException(
                "The authenticated administrator has no valid identifier.");
        }

        return new AdminOperationContext(
            actorUserId,
            HttpContext.TraceIdentifier);
    }

    private async Task<IActionResult> ChangeStateAsync(
        long productId,
        Task<CatalogMutationResult> operation,
        string successMessage)
    {
        var result = await operation;
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] =
            result.Succeeded
                ? successMessage
                : string.Join(" ", result.Errors);

        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    private async Task LoadProductDetailsAsync(
        long id,
        CancellationToken cancellationToken) =>
        ViewData["ProductDetails"] = await catalogService.GetAdminAsync(
            id,
            cancellationToken);

    private void AddErrors(CatalogMutationResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
