using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.Database
{
    public class HandbookChunk
    {
        [Key, Column("id")]
        public int Id { get; set; }

        // Original chunk id from the RAG dataset, e.g. "axial_0096" — keep this
        // so you can always trace a stored row back to the source JSON if you
        // need to re-import or debug.
        [Required, MaxLength(20), Column("chunk_key")]
        public string ChunkKey { get; set; } = string.Empty;

        [Column("chapter")]
        public int? Chapter { get; set; }

        [MaxLength(200), Column("chapter_title")]
        public string? ChapterTitle { get; set; }

        [MaxLength(300), Column("section")]
        public string? Section { get; set; }

        [Column("page")]
        public int? Page { get; set; }

        [MaxLength(30), Column("content_type")]
        public string? ContentType { get; set; } // narrative | figure_caption | narrative_excerpt

        [Required, Column("text")]
        public string Text { get; set; } = string.Empty;

        // The embedding vector, stored as a JSON array of floats (e.g. "[0.012,-0.034,...]").
        // MySQL has no native vector type here, so this is stored as text and
        // parsed back into a float[] in code when doing similarity search.
        [Column("embedding")]
        public string? Embedding { get; set; }

        // Set from the cleanup pass we just did: "clean" | "needs_review" | "excluded"
        [MaxLength(20), Column("quality_flag")]
        public string QualityFlag { get; set; } = "clean";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}