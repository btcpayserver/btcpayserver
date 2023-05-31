using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
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
            model ??= new RolesViewModel();

            model.DefaultRole = (await storeRepository.GetDefaultRole()).Role;
            var roles = await storeRepository.GetStoreRoles(storeId, false, false);

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

        [HttpGet("{storeId}/roles/{role}")]
        public async Task<IActionResult> CreateOrEditRole(
            string storeId,
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
                var roleData = await storeRepository.GetStoreRole(new StoreRoleId(storeId, role));
                if (roleData == null)
                    return NotFound();
                return View(new UpdateRoleViewModel()
                {
                    Policies = roleData.Permissions,
                    Role = roleData.Role
                });
            }
        } 
        [HttpPost("{storeId}/roles/{role}")]
        public async Task<IActionResult> CreateOrEditRole(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            [FromRoute] string role, UpdateRoleViewModel viewModel)
        {
            string successMessage = null;
            StoreRoleId roleId;
            if (role == "create")
            {
                successMessage = "Role created";
                role = viewModel.Role;
                roleId = new StoreRoleId(storeId, role);
            }
            else
            {
                successMessage = "Role updated";
                roleId = new StoreRoleId(storeId, role);
                var storeRole = await storeRepository.GetStoreRole(roleId);
                if (storeRole == null)
                    return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var r = await storeRepository.AddOrUpdateStoreRole(roleId, viewModel.Policies);
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

            return RedirectToAction(nameof(ListRoles), new { storeId });
        }
        


        [HttpGet("{storeId}/roles/{role}/delete")]
        public async Task<IActionResult> DeleteRole(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            string role)
        {
            var roleData = await storeRepository.GetStoreRole(new StoreRoleId(storeId, role), true);;
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

        [HttpPost("{storeId}/roles/{role}/delete")]
        public async Task<IActionResult> DeleteRolePost(
            string storeId,
            [FromServices] StoreRepository storeRepository,
            string role)
        {
            var roleId = new StoreRoleId(storeId, role);
            var roleData = await storeRepository.GetStoreRole(roleId, true);
            if (roleData == null)
                return NotFound();
            if (roleData.IsUsed is true)
            {
                return BadRequest();
            }
            await storeRepository.RemoveStoreRole(roleId);

            TempData[WellKnownTempData.SuccessMessage] = "Role deleted";
            return RedirectToAction(nameof(ListRoles), new { storeId });
        }
    }
}
