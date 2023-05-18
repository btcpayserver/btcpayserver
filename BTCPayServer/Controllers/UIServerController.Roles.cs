using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3.Transfer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [Route("server/roles")]
        public async Task<IActionResult> ListRoles(
            [FromServices] StoreRepository storeRepository,
            RolesViewModel model,
            string sortOrder = null
        )
        {
            model = this.ParseListQuery(model ?? new RolesViewModel());

            var roles = await storeRepository.GetStoreRoles(null);
            if (!string.IsNullOrWhiteSpace(model.SearchTerm))
            {
                roles = roles.Where(r => r.Role.Contains(model.SearchTerm, StringComparison.InvariantCultureIgnoreCase))
                    .ToArray();
            }

            if (sortOrder != null)
            {
                switch (sortOrder)
                {
                    case "desc":
                        ViewData["NextRoleSortOrder"] = "asc";
                        roles = roles.OrderByDescending(user => user.Role).ToArray();
                        break;
                    case "asc":
                        roles = roles.OrderBy(user => user.Role).ToArray();
                        ViewData["NextRoleSortOrder"] = "desc";
                        break;
                }
            }

            model.Roles = roles.Skip(model.Skip).Take(model.Count).ToList();

            return View(model);
        }

        [HttpGet("server/roles/{roleId}")]
        public async Task<IActionResult> CreateOrEditRole(
            
            [FromServices] StoreRepository storeRepository,
            string roleId )
        {
            if (roleId == "create")
                roleId = null;
            if (roleId is not null)
            {
                var role = await storeRepository.GetStoreRole(roleId, null);
                if (role == null)
                    return NotFound();
                return View(new UpdateRoleViewModel()
                {
                    Policies = role.Policies,
                    Role = role.Role
                });
            }
            else
            {
                return View(new UpdateRoleViewModel());
            }
        } 
        [HttpPost("server/roles/{roleId}")]
        public async Task<IActionResult> CreateOrEditRole(
            
            [FromServices] StoreRepository storeRepository,
            string? roleId, UpdateRoleViewModel viewModel)
        {
            if (roleId == "create")
                roleId = null;
            if (roleId is not null)
            {
                var role = await storeRepository.GetStoreRole(roleId, null);
                if (role == null)
                    return NotFound();
              
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var r = await storeRepository.AddOrUpdateStoreRole(roleId, viewModel.Role, null, viewModel.Policies);
            if (r is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "Role could not be updated"
                });
                
                return View(viewModel);
            }

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = roleId is null? "Role created" : "Role updated"
            });
                
            return RedirectToAction(nameof(ListRoles));
        }
        


        [HttpGet("server/roles/{roleId}/delete")]
        public async Task<IActionResult> DeleteRole(
            [FromServices] StoreRepository storeRepository,
            string roleId)
        {
            var role = await storeRepository.GetStoreRole(roleId, null, true);
            if (role == null)
                return NotFound();

            return View("Confirm",
                role.IsUsed is true
                    ? new ConfirmModel("Delete role",
                        $"Unable to proceed: The role <strong>{Html.Encode(role.Role)}</strong> is currently assigned to one or more users, it cannot be removed.")
                    : new ConfirmModel("Delete role",
                        $"The role <strong>{Html.Encode(role.Role)}</strong> will be permanently deleted. Are you sure?",
                        "Delete"));
        }

        [HttpPost("server/roles/{roleId}/delete")]
        public async Task<IActionResult> DeleteRolePost(
            [FromServices] StoreRepository storeRepository,
            string roleId)
        {
            var role = await storeRepository.GetStoreRole(roleId, null, true);
            if (role == null)
                return NotFound();
            if (role.IsUsed is true)
            {
                return BadRequest();
            }

            if (await storeRepository.RemoveStoreRole(roleId, null))
            {
                
                TempData[WellKnownTempData.SuccessMessage] = "Role deleted";
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = "Role could not be deleted (Is it the last permission for store administrator?)";
            }

            return RedirectToAction(nameof(ListRoles));
        }
    }
}
public class UpdateRoleViewModel
{
    [Required]
    [Display(Name = "Role")]
    public string Role { get; set; }

    [Display(Name = "Policies")] public List<string> Policies { get; set; } = new();
}
