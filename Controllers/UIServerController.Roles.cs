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
            RolesViewModel model,
            string sortOrder = null
        )
        {
            var roles = await _StoreRepository.GetStoreRoles(null);
            var defaultRole = (await _StoreRepository.GetDefaultRole()).Role;
            model ??= new RolesViewModel();
            model.DefaultRole = defaultRole;

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

            model.Roles = roles.Skip(model.Skip).Take(model.Count).ToList();

            return View(model);
        }

        [HttpGet("server/roles/{role}")]
        public async Task<IActionResult> CreateOrEditRole(string role)
        {
            if (role == "create")
            {
                ModelState.Remove(nameof(role));
                return View(new UpdateRoleViewModel());
            }

            var roleData = await _StoreRepository.GetStoreRole(new StoreRoleId(role));
            if (roleData == null)
                return NotFound();

            return View(new UpdateRoleViewModel
            {
                Policies = roleData.Permissions,
                Role = roleData.Role
            });
        } 

        [HttpPost("server/roles/{role}")]
        public async Task<IActionResult> CreateOrEditRole([FromRoute] string role, UpdateRoleViewModel viewModel)
        {
            string successMessage = null;
            if (role == "create")
            {
                successMessage = StringLocalizer["Role created"];
                role = viewModel.Role;
            }
            else
            {
                successMessage = StringLocalizer["Role updated"];
                var storeRole = await _StoreRepository.GetStoreRole(new StoreRoleId(role));
                if (storeRole == null)
                    return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var r = await _StoreRepository.AddOrUpdateStoreRole(new StoreRoleId(role), viewModel.Policies);
            if (r is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = StringLocalizer["Role could not be updated"].Value
                });
                return View(viewModel);
            }

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = successMessage
            });
                
            return RedirectToAction(nameof(ListRoles));
        }
        


        [HttpGet("server/roles/{role}/delete")]
        public async Task<IActionResult> DeleteRole(string role)
        {
            var roleData = await _StoreRepository.GetStoreRole(new StoreRoleId(role), true);
            if (roleData == null)
                return NotFound();

            return View("Confirm",
                roleData.IsUsed is true
                    ? new ConfirmModel(StringLocalizer["Delete role"],
                        $"Unable to proceed: The role <strong>{Html.Encode(roleData.Role)}</strong> is currently assigned to one or more users, it cannot be removed.")
                    : new ConfirmModel(StringLocalizer["Delete role"],
                        $"The role <strong>{Html.Encode(roleData.Role)}</strong> will be permanently deleted. Are you sure?",
                        StringLocalizer["Delete"]));
        }

        [HttpPost("server/roles/{role}/delete")]
        public async Task<IActionResult> DeleteRolePost(string role)
        {
            var roleId = new StoreRoleId(role);
            var roleData = await _StoreRepository.GetStoreRole(roleId, true);
            if (roleData == null)
                return NotFound();
            if (roleData.IsUsed is true)
            {
                return BadRequest();
            }

            var errorMessage = await _StoreRepository.RemoveStoreRole(roleId);
            if (errorMessage is null)
            {
                
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Role deleted"].Value;
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = errorMessage;
            }

            return RedirectToAction(nameof(ListRoles));
        }

        [HttpGet("server/roles/{role}/default")]
        public async Task<IActionResult> SetDefaultRole(string role)
        {
            var resolved = await _StoreRepository.ResolveStoreRoleId(null, role);
            if (resolved is null)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Role could not be set as default"].Value;
            }
            else
            {
                await _StoreRepository.SetDefaultRole(role);
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Role set default"].Value;
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
