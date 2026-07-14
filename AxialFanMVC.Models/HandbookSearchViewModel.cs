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

        // Cleaned presentation of Text: split into lines, exact
        // duplicates removed, lines that are pure OCR noise dropped.
        // Populated by HandbookController — Text is kept as-is
        // alongside this for anyone who needs the raw source.
        public List<string> Bullets { get; set; } = new();

        // Set by HandbookController from TextQuality.Assess — true
        // only when the chunk is STILL mostly noise after per-line
        // cleanup (i.e. Bullets ended up empty or near-empty), so the
        // view can show a caveat instead of presenting scan noise as
        // if it were reliable reference content.
        public bool MayContainScanArtifacts { get; set; }
    }
}