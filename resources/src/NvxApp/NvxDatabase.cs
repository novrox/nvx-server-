using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

sealed class NvxRowSet
{
    public List<string> Columns = new List<string>();
    public List<string[]> Rows = new List<string[]>();
    public int Affected;
    public bool Truncated;
    public bool HasRows;
}

sealed class NvxDatabase
{
    const int Limit = 100;
    readonly string _folder;
    static readonly Regex NamePattern = new Regex("^[A-Za-z][A-Za-z0-9_-]{0,62}$");
    static readonly Regex IdPattern = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,62}$");
    static readonly Regex SqlStart = new Regex("^\\s*(SELECT|INSERT|UPDATE|DELETE|CREATE|DROP|ALTER|WITH)\\b", RegexOptions.IgnoreCase);
    static readonly Regex Blocked = new Regex("\\b(ATTACH|DETACH|load_extension)\\b", RegexOptions.IgnoreCase);

    public NvxDatabase(string root)
    {
        _folder = Path.Combine(root, "data\\databases");
        Directory.CreateDirectory(_folder);
    }

    public List<string> List()
    {
        List<string> names = new List<string>();
        string[] files = Directory.GetFiles(_folder, "*.sqlite");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++) names.Add(Path.GetFileNameWithoutExtension(files[i]));
        return names;
    }

    public string PathOf(string name)
    {
        return Path.Combine(_folder, SafeName(name) + ".sqlite");
    }

    public void Create(string name)
    {
        string path = PathOf(name);
        if (File.Exists(path)) throw new InvalidOperationException("Database " + name + " already exists.");
        using (SqliteDb db = SqliteDb.Open(path))
        {
            db.Exec("CREATE TABLE IF NOT EXISTS _nvx_meta (key TEXT PRIMARY KEY, value TEXT)");
        }
    }

    public void Drop(string name, string confirm)
    {
        if (confirm != name) throw new InvalidOperationException("Type the database name to delete it.");
        string path = PathOf(name);
        if (!File.Exists(path)) throw new InvalidOperationException("Database not found.");
        File.Delete(path);
    }

    public List<string> Tables(string name)
    {
        NvxRowSet set = Query(name, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name != '_nvx_meta' ORDER BY name");
        List<string> tables = new List<string>();
        for (int i = 0; i < set.Rows.Count; i++) tables.Add(set.Rows[i][0]);
        return tables;
    }

    public void CreateTable(string database, string table, List<string[]> columns)
    {
        if (!IdPattern.IsMatch(table)) throw new InvalidOperationException("Use a letter, then letters, numbers, or underscores for the table name.");
        if (columns.Count == 0) throw new InvalidOperationException("Add at least one column.");
        StringBuilder sql = new StringBuilder();
        sql.Append("CREATE TABLE \"").Append(table).Append("\" (");
        for (int i = 0; i < columns.Count; i++)
        {
            string column = columns[i][0];
            string type = columns[i][1].ToUpperInvariant();
            if (!IdPattern.IsMatch(column)) throw new InvalidOperationException("Invalid column name " + column);
            if (type != "INTEGER" && type != "TEXT" && type != "REAL" && type != "BLOB" && type != "NUMERIC")
            {
                throw new InvalidOperationException("Unsupported column type " + type);
            }
            if (i > 0) sql.Append(", ");
            sql.Append("\"").Append(column).Append("\" ").Append(type);
        }
        sql.Append(")");
        Execute(database, sql.ToString());
    }

    public void DropTable(string database, string table)
    {
        if (!IdPattern.IsMatch(table)) throw new InvalidOperationException("Invalid table name.");
        Execute(database, "DROP TABLE \"" + table + "\"");
    }

    public NvxRowSet Query(string database, string sql)
    {
        CheckSql(sql);
        using (SqliteDb db = SqliteDb.Open(PathOf(database)))
        {
            return db.Query(sql, Limit);
        }
    }

    public int Execute(string database, string sql)
    {
        CheckSql(sql);
        using (SqliteDb db = SqliteDb.Open(PathOf(database)))
        {
            db.Exec(sql);
            return db.Changes();
        }
    }

    public static string SafeName(string name)
    {
        if (name == null || !NamePattern.IsMatch(name))
        {
            throw new InvalidOperationException("Use a letter, then letters, numbers, underscores, or hyphens (up to 63 characters).");
        }
        return name;
    }

    static void CheckSql(string sql)
    {
        string trimmed = (sql ?? "").Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("Enter a SQL statement.");
        if (trimmed.IndexOf(';') >= 0 && trimmed.IndexOf(';') < trimmed.Length - 1)
        {
            throw new InvalidOperationException("Run one SQL statement at a time.");
        }
        trimmed = trimmed.TrimEnd(';').Trim();
        if (!SqlStart.IsMatch(trimmed)) throw new InvalidOperationException("Allowed statements start with SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, or WITH.");
        if (Blocked.IsMatch(trimmed)) throw new InvalidOperationException("That SQL statement is blocked.");
    }
}

sealed class SqliteDb : IDisposable
{
    IntPtr _db;

    SqliteDb(IntPtr db) { _db = db; }

    public static SqliteDb Open(string path)
    {
        if (!File.Exists(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + "\\sqlite3.dll") && !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sqlite3.dll")))
        {
            throw new InvalidOperationException("sqlite3.dll is missing beside nvx.exe. Reinstall NVX Server.");
        }
        IntPtr db;
        int rc = Native.sqlite3_open16(path, out db);
        if (rc != 0 || db == IntPtr.Zero)
        {
            string message = "Could not open the database.";
            if (db != IntPtr.Zero)
            {
                message = Native.Message(db);
                Native.sqlite3_close(db);
            }
            throw new InvalidOperationException(message);
        }
        Native.sqlite3_busy_timeout(db, 3000);
        SqliteDb wrapper = new SqliteDb(db);
        wrapper.Exec("PRAGMA foreign_keys = ON");
        return wrapper;
    }

    public void Exec(string sql)
    {
        IntPtr err;
        int rc = Native.sqlite3_exec(_db, sql, IntPtr.Zero, IntPtr.Zero, out err);
        if (rc != 0)
        {
            string message = Native.FromUtf8(err);
            if (err != IntPtr.Zero) Native.sqlite3_free(err);
            if (message.Length == 0) message = Native.Message(_db);
            throw new InvalidOperationException(message);
        }
    }

    public int Changes()
    {
        return Native.sqlite3_changes(_db);
    }

    public NvxRowSet Query(string sql, int limit)
    {
        IntPtr stmt;
        IntPtr tail;
        int rc = Native.sqlite3_prepare16_v2(_db, sql, -1, out stmt, out tail);
        if (rc != 0)
        {
            if (stmt != IntPtr.Zero) Native.sqlite3_finalize(stmt);
            throw new InvalidOperationException(Native.Message(_db));
        }
        NvxRowSet set = new NvxRowSet();
        int columns = Native.sqlite3_column_count(stmt);
        for (int i = 0; i < columns; i++) set.Columns.Add(Native.Text(Native.sqlite3_column_name16(stmt, i)));
        set.HasRows = columns > 0;
        while (true)
        {
            rc = Native.sqlite3_step(stmt);
            if (rc == 100)
            {
                if (set.Rows.Count >= limit) { set.Truncated = true; break; }
                string[] row = new string[columns];
                for (int i = 0; i < columns; i++)
                {
                    if (Native.sqlite3_column_type(stmt, i) == 5) row[i] = "";
                    else row[i] = Native.Text(Native.sqlite3_column_text16(stmt, i));
                }
                set.Rows.Add(row);
            }
            else if (rc == 101) break;
            else
            {
                Native.sqlite3_finalize(stmt);
                throw new InvalidOperationException(Native.Message(_db));
            }
        }
        Native.sqlite3_finalize(stmt);
        set.Affected = Native.sqlite3_changes(_db);
        return set;
    }

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            Native.sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }
}

static class Native
{
    const CallingConvention Call = CallingConvention.Cdecl;

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_open16([MarshalAs(UnmanagedType.LPWStr)] string filename, out IntPtr db);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_close(IntPtr db);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    [DllImport("sqlite3.dll", CallingConvention = Call, CharSet = CharSet.Ansi)]
    public static extern int sqlite3_exec(IntPtr db, string sql, IntPtr callback, IntPtr arg, out IntPtr err);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern void sqlite3_free(IntPtr pointer);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_prepare16_v2(IntPtr db, [MarshalAs(UnmanagedType.LPWStr)] string sql, int nByte, out IntPtr stmt, out IntPtr tail);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_step(IntPtr stmt);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_column_count(IntPtr stmt);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern IntPtr sqlite3_column_name16(IntPtr stmt, int index);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern IntPtr sqlite3_column_text16(IntPtr stmt, int index);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_column_type(IntPtr stmt, int index);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern int sqlite3_changes(IntPtr db);

    [DllImport("sqlite3.dll", CallingConvention = Call)]
    public static extern IntPtr sqlite3_errmsg16(IntPtr db);

    public static string Message(IntPtr db)
    {
        return Text(sqlite3_errmsg16(db));
    }

    public static string Text(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return "";
        return Marshal.PtrToStringUni(pointer) ?? "";
    }

    public static string FromUtf8(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return "";
        int length = 0;
        while (Marshal.ReadByte(pointer, length) != 0) length++;
        if (length == 0) return "";
        byte[] bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }
}
