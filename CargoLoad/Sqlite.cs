using System;
using System.Runtime.InteropServices;

namespace CargoLoad
{
    public sealed class SqliteException : Exception
    {
        public int Code { get; }
        public SqliteException(int code, string message) : base(message) { Code = code; }
    }

    internal static class Native
    {
        private const string Dll = "winsqlite3.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
            out IntPtr db, int flags, IntPtr zVfs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(
            IntPtr db,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string zSql,
            int nByte, out IntPtr ppStmt, IntPtr pzTail);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_text(IntPtr stmt, int index,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int n, IntPtr destructor);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_null(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr stmt, int i);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern double sqlite3_column_double(IntPtr stmt, int i);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_type(IntPtr stmt, int i);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_last_insert_rowid(IntPtr db);
    }

    public sealed class Sqlite : IDisposable
    {
        private IntPtr _db;

        public const int OK = 0;
        public const int ROW = 100;
        public const int DONE = 101;

        private const int OPEN_READWRITE = 2;
        private const int OPEN_CREATE = 4;
        private const int INTEGER = 1;
        private const int FLOAT = 2;
        private const int TEXT = 3;
        private const int NULL = 5;
        private const int BLOB = 4;

        public Sqlite(string path)
        {
            int rc = Native.sqlite3_open_v2(path, out _db, OPEN_READWRITE | OPEN_CREATE, IntPtr.Zero);
            if (rc != OK) throw new SqliteException(rc, ErrorMessage());
        }

        public void Execute(string sql)
        {
            int rc = Native.sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, IntPtr.Zero);
            if (rc != OK) throw new SqliteException(rc, ErrorMessage());
            try
            {
                rc = Native.sqlite3_step(stmt);
                if (rc != DONE && rc != ROW) throw new SqliteException(rc, ErrorMessage());
            }
            finally
            {
                Native.sqlite3_finalize(stmt);
            }
        }

        public long ExecuteInsert(string sql)
        {
            int rc = Native.sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, IntPtr.Zero);
            if (rc != OK) throw new SqliteException(rc, ErrorMessage());
            try
            {
                rc = Native.sqlite3_step(stmt);
                if (rc != DONE && rc != ROW) throw new SqliteException(rc, ErrorMessage());
            }
            finally
            {
                Native.sqlite3_finalize(stmt);
            }
            return Native.sqlite3_last_insert_rowid(_db);
        }

        public Statement Prepare(string sql)
        {
            int rc = Native.sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, IntPtr.Zero);
            if (rc != OK) throw new SqliteException(rc, ErrorMessage());
            return new Statement(this, stmt);
        }

        public long LastRowId() => Native.sqlite3_last_insert_rowid(_db);

        public string ErrorMessage()
        {
            IntPtr p = Native.sqlite3_errmsg(_db);
            return p == IntPtr.Zero ? "sqlite error" : Marshal.PtrToStringUTF8(p) ?? "sqlite error";
        }

        public void Dispose()
        {
            if (_db != IntPtr.Zero)
            {
                Native.sqlite3_close(_db);
                _db = IntPtr.Zero;
            }
        }

        public sealed class Statement : IDisposable
        {
            private readonly Sqlite _db;
            private IntPtr _stmt;

            internal Statement(Sqlite db, IntPtr stmt)
            {
                _db = db;
                _stmt = stmt;
            }

            public bool Step()
            {
                int rc = Native.sqlite3_step(_stmt);
                if (rc == ROW) return true;
                if (rc == DONE) return false;
                throw new SqliteException(rc, _db.ErrorMessage());
            }

            public void Bind(int index, long value) => Check(Native.sqlite3_bind_int64(_stmt, index, value));
            public void Bind(int index, double value) => Check(Native.sqlite3_bind_double(_stmt, index, value));

            public void Bind(int index, string? value)
            {
                if (value == null)
                    Check(Native.sqlite3_bind_null(_stmt, index));
                else
                    Check(Native.sqlite3_bind_text(_stmt, index, value, -1, new IntPtr(-1))); // SQLITE_TRANSIENT
            }

            private void Check(int rc)
            {
                if (rc != OK) throw new SqliteException(rc, _db.ErrorMessage());
            }

            public int ColumnCount => Native.sqlite3_column_count(_stmt);

            public int ColumnType(int i) => Native.sqlite3_column_type(_stmt, i);

            public object? GetValue(int i)
            {
                int t = Native.sqlite3_column_type(_stmt, i);
                switch (t)
                {
                    case INTEGER: return Native.sqlite3_column_int64(_stmt, i);
                    case FLOAT: return Native.sqlite3_column_double(_stmt, i);
                    case TEXT:
                        IntPtr p = Native.sqlite3_column_text(_stmt, i);
                        return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
                    case BLOB: return null;
                    default: return null;
                }
            }

            public long GetInt64(int i) => Native.sqlite3_column_int64(_stmt, i);
            public double GetDouble(int i) => Native.sqlite3_column_double(_stmt, i);

            public string? GetString(int i)
            {
                IntPtr p = Native.sqlite3_column_text(_stmt, i);
                return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
            }

            public void Dispose()
            {
                if (_stmt != IntPtr.Zero)
                {
                    Native.sqlite3_finalize(_stmt);
                    _stmt = IntPtr.Zero;
                }
            }
        }
    }
}
