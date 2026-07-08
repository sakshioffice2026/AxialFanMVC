using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Repositories.Inteface
{
        public interface IOllamaChatRepository
        {
            /// <summary>
            /// Answers a user question using relevant handbook chunks as context (RAG),
            /// generated locally via Ollama.
            /// </summary>
            Task<string> AskAsync(string userMessage);
        }
    }

