using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AxialFanMVC.Database;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IOllamaChatRepository
    {
        /// <summary>
        /// Answers a user question using relevant handbook chunks as context (RAG),
        /// generated locally via Ollama. No design context — used by the standalone
        /// Handbook search page.
        /// </summary>
        Task<string> AskAsync(string userMessage);

        /// <summary>
        /// Answers a user question about a SPECIFIC design result — the actual
        /// computed input/output values are injected into the prompt alongside
        /// retrieved handbook chunks, so answers are grounded in this design's
        /// real numbers rather than generic advice. The model is instructed to
        /// only ever restate/explain values it's given, never compute or invent
        /// new ones.
        /// </summary>
        Task<string> AskAboutDesignAsync(string userMessage, DesignResult result);
    }
}