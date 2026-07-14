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
            private readonly IExceptionHandlerRepository _exceptionHandlerRepository;

            public HandbookController(
                IHandbookChunkRepository handbookRepo,
                IExceptionHandlerRepository exceptionHandlerRepository)
            {
                _handbookRepo = handbookRepo;
                _exceptionHandlerRepository = exceptionHandlerRepository;
            }

        public async Task<IActionResult> Index(string? query)
        {
            try
            {
                var vm = new HandbookSearchViewModel
                {
                    Query = query
                };

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
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(HandbookController),
                    nameof(Index),
                    ex.ToString());

                TempData["Error"] = "Unable to search handbook.";

                return RedirectToAction("Index", "Home");
            }
        }
        // ── TEMPORARY — ONE-TIME OPERATION ──────────────────────────────
        // Embeds every handbook chunk that doesn't have an embedding yet
        // (via Ollama's /api/embed) so SearchBySimilarityAsync has vectors
        // to compare against. There's no admin role in this app yet, so
        // this is deliberately just [Authorize] (any logged-in user) rather
        // than a permanent feature — visit /Handbook/BackfillEmbeddings once
        // after pulling the embedding model (`ollama pull nomic-embed-text`),
        // confirm the chunk count in the response, then delete this action.
        // GET rather than POST purely so it can be triggered by navigating
        // to the URL directly; safe to hit more than once since it only
        // processes rows still missing an embedding.
        public async Task<IActionResult> BackfillEmbeddings()
        {
            var count = await _handbookRepo.BackfillEmbeddingsAsync();
            return Content($"Embedded {count} chunk(s). Remaining chunks already had embeddings.");
        }

    }
    }
