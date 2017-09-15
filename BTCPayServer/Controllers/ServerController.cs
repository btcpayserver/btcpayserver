using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
	[Authorize(Roles=Roles.ServerAdmin)]
	public class ServerController : Controller
	{
		private UserManager<ApplicationUser> _UserManager;

		public ServerController(UserManager<ApplicationUser> userManager)
		{
			_UserManager = userManager;
		}

		[Route("server/users")]
		public IActionResult ListUsers()
		{
			var users = new UsersViewModel();
			users.Users
				= _UserManager.Users.Select(u => new UsersViewModel.UserViewModel()
				{
					Name = u.UserName,
					Email = u.Email
				}).ToList();
			return View(users);
		}
	}
}
