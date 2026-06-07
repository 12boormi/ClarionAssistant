using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ClarionAssistant.Tools.IdeApiExtractor
{
    /// <summary>
    /// Writes the 'Clarion IDE API' library into a DocGraph SQLite DB using the SAME schema/DDL as
    /// ClarionAssistant.Services.DocGraphService (libraries / doc_chunks / standalone FTS5 doc_fts).
    /// Only touches our own library row + chunks; other libraries in the DB are preserved.
    /// </summary>
    internal sealed class DocGraphWriter : IDisposable
    {
        readonly SqliteConnection _conn;

        public DocGraphWriter(string dbPath)
        {
            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();
            Exec("PRAGMA foreign_keys=ON");
            CreateSchema();
        }

        void CreateSchema()
        {
            Exec(@"CREATE TABLE IF NOT EXISTS libraries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL, vendor TEXT, version TEXT,
                source_path TEXT, source_format TEXT,
                ingested_at TEXT DEFAULT (datetime('now')),
                UNIQUE(vendor, name))");
            // tags column (DocGraphService adds it via migration ALTER)
            try { Exec("ALTER TABLE libraries ADD COLUMN tags TEXT"); } catch (SqliteException) { }

            Exec(@"CREATE TABLE IF NOT EXISTS doc_chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
                class_name TEXT, method_name TEXT, topic TEXT, heading TEXT,
                content TEXT, code_example TEXT, signature TEXT, anchor TEXT,
                UNIQUE(library_id, class_name, method_name, topic, heading))");

            Exec(@"CREATE VIRTUAL TABLE IF NOT EXISTS doc_fts USING fts5(
                chunk_id, class_name, method_name, heading, content, code_example, signature,
                tokenize='porter unicode61')");

            Exec("CREATE INDEX IF NOT EXISTS idx_chunks_library ON doc_chunks(library_id)");
            Exec("CREATE INDEX IF NOT EXISTS idx_chunks_class ON doc_chunks(class_name)");
            Exec("CREATE INDEX IF NOT EXISTS idx_chunks_method ON doc_chunks(method_name)");
        }

        public long EnsureLibrary(string name, string vendor, string version, string sourcePath, string sourceFormat, string tags)
        {
            using (var tx = _conn.BeginTransaction())
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO libraries(name,vendor,version,source_path,source_format,tags)
                        VALUES($n,$v,$ver,$sp,$sf,$tg)
                        ON CONFLICT(vendor,name) DO UPDATE SET
                            version=excluded.version, source_path=excluded.source_path,
                            source_format=excluded.source_format, tags=excluded.tags,
                            ingested_at=datetime('now')";
                    Bind(cmd, "$n", name); Bind(cmd, "$v", vendor); Bind(cmd, "$ver", version);
                    Bind(cmd, "$sp", sourcePath); Bind(cmd, "$sf", sourceFormat); Bind(cmd, "$tg", tags);
                    cmd.ExecuteNonQuery();
                }
                long id;
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT id FROM libraries WHERE vendor=$v AND name=$n";
                    Bind(cmd, "$v", vendor); Bind(cmd, "$n", name);
                    id = Convert.ToInt64(cmd.ExecuteScalar());
                }
                tx.Commit();
                return id;
            }
        }

        public void DeleteLibraryChunks(long libraryId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM doc_chunks WHERE library_id=$id";
                Bind(cmd, "$id", libraryId);
                cmd.ExecuteNonQuery();
            }
        }

        public int InsertChunks(long libraryId, IEnumerable<DocChunk> chunks)
        {
            int n = 0;
            using (var tx = _conn.BeginTransaction())
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT OR IGNORE INTO doc_chunks
                        (library_id,class_name,method_name,topic,heading,content,code_example,signature,anchor)
                        VALUES($lib,$cn,$mn,$tp,$hd,$ct,$ce,$sg,$an)";
                    var pLib = cmd.CreateParameter(); pLib.ParameterName = "$lib"; pLib.Value = libraryId; cmd.Parameters.Add(pLib);
                    var pCn = Add(cmd, "$cn"); var pMn = Add(cmd, "$mn"); var pTp = Add(cmd, "$tp");
                    var pHd = Add(cmd, "$hd"); var pCt = Add(cmd, "$ct"); var pCe = Add(cmd, "$ce");
                    var pSg = Add(cmd, "$sg"); var pAn = Add(cmd, "$an");
                    foreach (var c in chunks)
                    {
                        pCn.Value = (object)c.ClassName ?? DBNull.Value;
                        pMn.Value = (object)c.MethodName ?? DBNull.Value;
                        pTp.Value = (object)c.Topic ?? DBNull.Value;
                        pHd.Value = (object)c.Heading ?? DBNull.Value;
                        pCt.Value = (object)c.Content ?? DBNull.Value;
                        pCe.Value = (object)c.CodeExample ?? DBNull.Value;
                        pSg.Value = (object)c.Signature ?? DBNull.Value;
                        pAn.Value = (object)c.Anchor ?? DBNull.Value;
                        n += cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
            return n;
        }

        /// <summary>Drops and rebuilds the standalone FTS5 index from ALL doc_chunks (mirrors DocGraphService.RebuildFtsIndex).</summary>
        public int RebuildFtsIndex()
        {
            Exec("DROP TABLE IF EXISTS doc_fts");
            Exec(@"CREATE VIRTUAL TABLE doc_fts USING fts5(
                chunk_id, class_name, method_name, heading, content, code_example, signature,
                tokenize='porter unicode61')");
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO doc_fts(chunk_id,class_name,method_name,heading,content,code_example,signature)
                    SELECT CAST(id AS TEXT),class_name,method_name,heading,content,code_example,signature FROM doc_chunks";
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Run an FTS5 MATCH and return up to `limit` (class_name, heading, topic) rows — verification helper.</summary>
        public List<(string cls, string heading, string topic)> QueryFts(string match, int limit = 5)
        {
            var rows = new List<(string, string, string)>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT f.class_name, f.heading, c.topic
                    FROM doc_fts f JOIN doc_chunks c ON c.id = CAST(f.chunk_id AS INTEGER)
                    WHERE doc_fts MATCH $m LIMIT $lim";
                Bind(cmd, "$m", match); Bind(cmd, "$lim", limit);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        rows.Add((r.IsDBNull(0) ? "" : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2)));
            }
            return rows;
        }

        SqliteParameter Add(SqliteCommand cmd, string name)
        {
            var p = cmd.CreateParameter(); p.ParameterName = name; cmd.Parameters.Add(p); return p;
        }
        static void Bind(SqliteCommand cmd, string name, object val)
        {
            var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = val ?? DBNull.Value; cmd.Parameters.Add(p);
        }
        void Exec(string sql) { using (var c = _conn.CreateCommand()) { c.CommandText = sql; c.ExecuteNonQuery(); } }

        public void Dispose() { _conn?.Dispose(); }
    }
}
