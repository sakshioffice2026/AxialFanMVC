using AxialFanMVC.Models;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxialFanMVC.Controllers
{
  
    
        // ─────────────────────────────────────────────────────────────────────────
        // HandbookController
        //
        // Full-text search over the Fan Handbook (Bleier) reference chunks.
        //
        // Route summary
        // ─────────────────────────────────────────────────────────────────────────
        //  GET  /Handbook                  → Index (search form, empty results)
        //  GET  /Handbook?query={text}     → Index (search form + ranked results)
        // ─────────────────────────────────────────────────────────────────────────
        [Authorize]
        public class HandbookController : Controller
        {
            private readonly IHandbookChunkRepository _handbookRepo;

            public HandbookController(IHandbookChunkRepository handbookRepo)
            {
                _handbookRepo = handbookRepo;
            }

            public async Task<IActionResult> Index(string? query)
            {
                var vm = new HandbookSearchViewModel { Query = query };

                if (!string.IsNullOrWhiteSpace(query))
                {
                    var chunks = await _handbookRepo.SearchAsync(query, maxResults: 15);
                    vm.Results = chunks.Select(c => new HandbookSearchResult
                    {
                        ChunkKey = c.ChunkKey,
                        Chapter = c.Chapter,
                        ChapterTitle = c.ChapterTitle,
                        Section = c.Section,
                        Page = c.Page,
                        Text = c.Text
                    }).ToList();
                }

                return View(vm);
            }
        }
    }
