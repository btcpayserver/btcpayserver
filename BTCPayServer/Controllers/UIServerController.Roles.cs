using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
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
            model ??= new RolesViewModel();

            model.DefaultRole = (await storeRepository.GetDefaultRole()).Role;
            var roles = await storeRepository.GetStoreRoles(null);

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

        [HttpGet("server/roles/{role}")]
        public async Task<IActionResult> CreateOrEditRole(
            [FromServices] StoreRepository storeRepository,
            string role)
        {
            if (role == "create")
            {
                ModelState.Remove(nameof(role));
                return View(new UpdateRoleViewModel());
            }
            else
            {
                var roleData = await storeRepository.GetStoreRole(new StoreRoleId(role));
                if (roleData == null)
                    return NotFound();
                return View(new UpdateRoleViewModel()
                {
                    Policies = roleData.Permissions,
                    Role = roleData.Role
                });
            }
        } 
        [HttpPost("server/roles/{role}")]
        public async Task<IActionResult> CreateOrEditRole(
            
            [FromServices] StoreRepository storeRepository,
            [FromRoute] string role, UpdateRoleViewModel viewModel)
        {
            string successMessage = null;
            if (role == "create")
            {
                successMessage = "Role created";
                role = viewModel.Role;
            }
            else
            {
                successMessage = "Role updated";
                var storeRole = await storeRepository.GetStoreRole(new StoreRoleId(role));
                if (storeRole == null)
                    return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var r = await storeRepository.AddOrUpdateStoreRole(new StoreRoleId(role), viewModel.Policies);
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
                Message = successMessage
            });
                
            return RedirectToAction(nameof(ListRoles));
        }
        


        [HttpGet("server/roles/{role}/delete")]
        public async Task<IActionResult> DeleteRole(
            [FromServices] StoreRepository storeRepository,
            string role)
        {
            var roleData = await storeRepository.GetStoreRole(new StoreRoleId(role), true);
            if (roleData == null)
                return NotFound();

            return View("Confirm",
                roleData.IsUsed is true
                    ? new ConfirmModel("Delete role",
                        $"Unable to proceed: The role <strong>{Html.Encode(roleData.Role)}</strong> is currently assigned to one or more users, it cannot be removed.")
                    : new ConfirmModel("Delete role",
                        $"The role <strong>{Html.Encode(roleData.Role)}</strong> will be permanently deleted. Are you sure?",
                        "Delete"));
        }

        [HttpPost("server/roles/{role}/delete")]
        public async Task<IActionResult> DeleteRolePost(
            [FromServices] StoreRepository storeRepository,
            string role)
        {
            var roleId = new StoreRoleId(role);
            var roleData = await storeRepository.GetStoreRole(roleId, true);
            if (roleData == null)
                return NotFound();
            if (roleData.IsUsed is true)
            {
                return BadRequest();
            }

            var errorMessage = await storeRepository.RemoveStoreRole(roleId);
            if (errorMessage is null)
            {
                
                TempData[WellKnownTempData.SuccessMessage] = "Role deleted";
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = errorMessage;
            }

            return RedirectToAction(nameof(ListRoles));
        }

        [HttpGet("server/roles/{role}/default")]
        public async Task<IActionResult> SetDefaultRole(
            [FromServices] StoreRepository storeRepository, 
            string role)
        {
            var resolved = await storeRepository.ResolveStoreRoleId(null, role);
            if (resolved is null)
            {
                TempData[WellKnownTempData.ErrorMessage] = "Role could not be set as default";
            }
            else
            {

                await storeRepository.SetDefaultRole(role);
                TempData[WellKnownTempData.SuccessMessage] = "Role set default";
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
