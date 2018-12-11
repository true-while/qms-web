using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QuoteManagment.Data;
using QuoteManagment.Models;

namespace QuoteManagment.Controllers
{
    public class SettingsController : Controller
    {
        private readonly SettingsRepo _context;
        private IConfiguration _configuration;

        public SettingsController(SettingsRepo context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // GET: Settings
        public IActionResult Index()
        {
            return View(_context.GetSettings());
        }


        // GET: Settings/Edit/5
        public IActionResult Edit(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var setting = _context.GetSetting(id);
            if (setting == null)
            {
                return NotFound();
            }
            return View(setting);
        }

        // POST: Settings/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(string id, [Bind("Name,Value,Source")] Setting setting)
        {
            if (id != setting.Name)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                _context.UpdateSettings(setting.Name,setting.Value,setting.Source.ToString());
                return RedirectToAction(nameof(Index));
            }
            return View(setting);
        }


    }
}
