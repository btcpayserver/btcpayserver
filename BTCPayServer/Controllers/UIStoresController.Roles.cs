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
    public partial class UIStoresController
    {
        [Route("{storeId}/roles")]
        public async Task<IActionResult> ListRoles(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            RolesViewModel model,
            string sortOrder = null
        )
        {
            model = this.ParseListQuery(model ?? new RolesViewModel());

            var roles = await storeRepository.GetStoreRoles(storeId, false, false);
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

        [HttpGet("{storeId}/roles/{roleId}")]
        public async Task<IActionResult> CreateOrEditRole(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            string roleId )
        {
            if (roleId == "create")
                roleId = null;
            if (roleId is not null)
            {
                var role = await storeRepository.GetStoreRole(roleId, storeId);
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
        [HttpPost("{storeId}/roles/{roleId}")]
        public async Task<IActionResult> CreateOrEditRole(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            string? roleId, UpdateRoleViewModel viewModel)
        {
            if (roleId == "create")
                roleId = null;
            if (roleId is not null)
            {
                var role = await storeRepository.GetStoreRole(roleId, storeId);
                if (role == null)
                    return NotFound();
              
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var r = await storeRepository.AddOrUpdateStoreRole(roleId, viewModel.Role, storeId, viewModel.Policies);
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
                
            return RedirectToAction(nameof(ListRoles), new {storeId});
        }
        


        [HttpGet("{storeId}/roles/{roleId}/delete")]
        public async Task<IActionResult> DeleteRole(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            string roleId)
        {
            var role = await storeRepository.GetStoreRole(roleId, storeId, true);
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

        [HttpPost("{storeId}/roles/{roleId}/delete")]
        public async Task<IActionResult> DeleteRolePost(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            string roleId)
        {
            var role = await storeRepository.GetStoreRole(roleId, storeId, true);
            if (role == null)
                return NotFound();
            if (role.IsUsed is true)
            {
                return BadRequest();
            }
            await storeRepository.RemoveStoreRole(roleId, null);

            TempData[WellKnownTempData.SuccessMessage] = "Role deleted";
            return RedirectToAction(nameof(ListRoles));
        }
    }
}
