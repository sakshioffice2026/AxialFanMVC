using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Repositories
{
    
        public class HandbookChunkRepository : IHandbookChunkRepository
        {
            private readonly AxialFanDbContext _db;

            public HandbookChunkRepository(AxialFanDbContext db)
            {
                _db = db;
            }

            public async Task<List<HandbookChunk>> SearchAsync(string query, int maxResults = 10)
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new List<HandbookChunk>();

                // Natural language mode: MySQL ranks by relevance automatically,
                // no need for boolean operators (+/-) from the caller.
                return await _db.handbook_chunks
                    .FromSqlInterpolated($@"
                    SELECT *
                    FROM handbook_chunks
                    WHERE MATCH(text) AGAINST({query} IN NATURAL LANGUAGE MODE)
                    ORDER BY MATCH(text) AGAINST({query} IN NATURAL LANGUAGE MODE) DESC
                    LIMIT {maxResults}")
                    .ToListAsync();
            }
        }
    }

