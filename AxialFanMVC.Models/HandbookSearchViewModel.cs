using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Models
{
   
        public class HandbookSearchViewModel
        {
            public string? Query { get; set; }
            public List<HandbookSearchResult> Results { get; set; } = new();
        }

        public class HandbookSearchResult
        {
            public string ChunkKey { get; set; } = "";
            public int? Chapter { get; set; }
            public string? ChapterTitle { get; set; }
            public string? Section { get; set; }
            public int? Page { get; set; }
            public string Text { get; set; } = "";
        }
    }