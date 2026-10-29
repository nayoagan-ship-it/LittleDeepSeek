using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace LittleDeepSeek
{
    // The recipient owns this snapshot, including its icon.
    internal sealed class DesktopDeletion : IDisposable
    {
        internal string FullPath;
        internal string Name;
        internal Point Center;
        internal Bitmap Icon;
        internal bool HasPosition;
        public void Dispose() { if (Icon != null) { Icon.Dispose(); Icon = null; } }
    }

    // UI/STA-thread component. It observes Shell operations; it never deletes files.
    // In particular, a raw FileSystemWatcher.Deleted event is not a deletion signal:
    // moving a file out of a directory produces that event too.
    internal sealed class DesktopDeletionMonitor : IDisposable
    {
        internal const int MessageId = 0x8000 + 217;
        private const int Delete = 0x00000004, RemoveDirectory = 0x00000010;
        private const int RenameItem = 0x00000001, RenameFolder = 0x00020000;
        private const int MaxCache = 512;
        private readonly IntPtr hwnd;
        private readonly List<uint> registrations = new List<uint>();
        private readonly List<string> roots = new List<string>();
        private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> pending = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> moved = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private IntPtr view, viewWindow;
        private int scanIndex;
        private DateTime nextConnect, nextRoots;
        private bool disposed;
        private string lastStatus = "Waiting for Explorer desktop";
        private int notificationCount, deletionCount;
        internal event Action<DesktopDeletion> Deleted;
        internal int CachedCount { get { return cache.Count; } }
        internal string LastStatus { get { return lastStatus; } }
        internal string DiagnosticSummary
        {
            get { return "status=" + lastStatus + "; roots=" + String.Join("|", roots.ToArray()) +
                "; registrations=" + registrations.Count + "; cached=" + cache.Count +
                "; shellEvents=" + notificationCount + "; emitted=" + deletionCount +
                "; pending=" + pending.Count + "; view=" + viewWindow; }
        }

        private sealed class Entry
        {
            internal string Path;
            internal Point Center;
            internal Bitmap Icon;
            internal SafeFileHandle Handle;
            internal bool ConfirmedDeletion;
            internal DateTime Seen;
            internal DesktopDeletion Snapshot()
            {
                return new DesktopDeletion { FullPath = Path, Name = System.IO.Path.GetFileName(Path),
                    Center = Center, HasPosition = true, Icon = Icon == null ? null : (Bitmap)Icon.Clone() };
            }
        }

        internal DesktopDeletionMonitor(IntPtr window)
        {
            hwnd = window;
            RefreshRoots();
        }

        // Reflection-friendly diagnostics do not expose mutable cache entries.
        internal string[] GetCachedItems()
        {
            List<string> result = new List<string>();
            foreach (Entry item in cache.Values)
                result.Add(item.Path + "|" + item.Center.X + "," + item.Center.Y + "|" + item.Seen.ToString("O"));
            return result.ToArray();
        }
        internal DesktopDeletion GetCachedItem(string fullPath)
        {
            Entry item;
            return cache.TryGetValue(Normalize(fullPath), out item) ? item.Snapshot() : null;
        }

        internal void Refresh()
        {
            if (disposed) return;
            DateTime now = DateTime.UtcNow;
            DeliverPending(now);
            Trim(now);
            try
            {
                if (now >= nextRoots) RefreshRoots();
                if (view != IntPtr.Zero && !IsWindow(viewWindow)) Disconnect();
                if (view == IntPtr.Zero)
                {
                    if (now < nextConnect) return;
                    nextConnect = now.AddSeconds(3);
                    Connect();
                }
                int count;
                Check(Call<ItemCountDelegate>(view, 7)(view, 2, out count)); // SVGIO_ALLVIEW
                if (count <= 0) { scanIndex = 0; lastStatus = "Explorer desktop has no visible items"; return; }
                if (scanIndex >= count) scanIndex = 0;
                POINT spacing;
                Check(Call<PointDelegate>(view, 12)(view, out spacing));
                int iconSize = GetIconSize();
                Stopwatch budget = Stopwatch.StartNew();
                int examined = 0;
                while (examined < Math.Min(count, 24))
                {
                    int index = scanIndex;
                    scanIndex = (scanIndex + 1) % count;
                    ReadItem(index, spacing, iconSize, now);
                    examined++;
                    if (budget.ElapsedMilliseconds >= 25) break;
                }
                lastStatus = "Watching Explorer desktop";
            }
            catch (Exception ex)
            {
                lastStatus = ex.GetType().Name + ": " + ex.Message;
                Disconnect();
                nextConnect = now.AddSeconds(3);
            }
        }

        private void ReadItem(int index, POINT spacing, int iconSize, DateTime now)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                if (Call<ItemDelegate>(view, 6)(view, index, out pidl) < 0 || pidl == IntPtr.Zero) return;
                // Desktop view PIDLs are relative to the desktop namespace root, whose
                // absolute PIDL is empty. They are therefore also absolute desktop PIDLs.
                string path = PathFromPidl(pidl);
                if (!IsDirectDesktopItem(path)) return;
                POINT location;
                if (Call<PositionDelegate>(view, 11)(view, pidl, out location) < 0) return;
                // GetItemPosition gives the item's top-left corner (includes its white
                // space); the icon is centered in the horizontal item spacing.
                location.X += Math.Max(iconSize, spacing.X) / 2;
                location.Y += iconSize / 2 + 2;
                if (!ClientToScreen(viewWindow, ref location)) return;
                Point center = new Point(location.X, location.Y);
                bool onScreen = false;
                foreach (Screen screen in Screen.AllScreens)
                    if (screen.Bounds.Contains(center)) { onScreen = true; break; }
                if (!onScreen) return;
                Entry item;
                if (!cache.TryGetValue(path, out item))
                {
                    if (cache.Count >= MaxCache) return;
                    item = new Entry { Path = path, Icon = ReadIcon(path), Handle = OpenTrackingHandle(path) };
                    cache.Add(path, item);
                }
                item.Center = center;
                item.Seen = now;
                if (item.Handle == null)
                {
                    item.Handle = OpenTrackingHandle(path);
                    if (item.Handle != null) item.ConfirmedDeletion = false;
                }
            }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        internal bool ProcessMessage(ref Message message)
        {
            if (message.Msg != MessageId) return false;
            if (disposed) return true;
            IntPtr lists;
            int change;
            IntPtr notification = SHChangeNotification_Lock(message.WParam, unchecked((uint)message.LParam.ToInt64()), out lists, out change);
            if (notification == IntPtr.Zero) return true;
            string source = null, destination = null;
            try
            {
                notificationCount++;
                if (lists != IntPtr.Zero)
                {
                    source = PathFromPidl(Marshal.ReadIntPtr(lists));
                    if ((change & (RenameItem | RenameFolder)) != 0)
                        destination = PathFromPidl(Marshal.ReadIntPtr(lists, IntPtr.Size));
                }
            }
            finally { SHChangeNotification_Unlock(notification); }
            if (!IsDirectDesktopItem(source)) return true;
            DateTime now = DateTime.UtcNow;
            if ((change & (RenameItem | RenameFolder)) != 0)
            {
                // Some Shell versions report recycling as a rename into $Recycle.Bin.
                if (IsRecyclePath(destination)) Schedule(source, now);
                else
                {
                    moved[source] = now;
                    pending.Remove(source);
                    RemoveCached(source);
                }
            }
            else if ((change & (Delete | RemoveDirectory)) != 0) Schedule(source, now);
            return true;
        }

        private void Schedule(string path, DateTime now)
        {
            DateTime moveTime;
            if (moved.TryGetValue(path, out moveTime) && (now - moveTime).TotalSeconds < 2) return;
            if (cache.ContainsKey(path) && !pending.ContainsKey(path)) pending.Add(path, now);
        }

        private void DeliverPending(DateTime now)
        {
            List<string> ready = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in pending)
                if ((now - pair.Value).TotalMilliseconds >= 220) ready.Add(pair.Key);
            foreach (string path in ready)
            {
                pending.Remove(path);
                Entry item;
                if (!cache.TryGetValue(path, out item)) continue;
                // Shell can report even a same-volume move as DELETE + CREATE.
                // A metadata-only, delete-shared handle follows renames, so compare
                // its current location before accepting the deletion notification.
                if (!WasDeleted(item)) { RemoveCached(path); continue; }
                DesktopDeletion snapshot = item.Snapshot();
                // Close the tracking handle before queueing: permanent deletion can
                // remain pending until the final handle closes.
                RemoveCached(path);
                Action<DesktopDeletion> handler = Deleted;
                if (handler == null) snapshot.Dispose();
                else
                {
                    deletionCount++;
                    try { handler(snapshot); }
                    catch { snapshot.Dispose(); throw; }
                }
            }
        }

        private void Trim(DateTime now)
        {
            // A vanished icon must stay cached until its queued Shell notification arrives.
            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, Entry> pair in cache)
            {
                Entry item = pair.Value;
                FILE_STANDARD_INFO info;
                if (item.Handle != null && GetFileInformationByHandleEx(item.Handle, 1, out info, (uint)Marshal.SizeOf(typeof(FILE_STANDARD_INFO))) && info.DeletePending)
                {
                    // Release delete-pending handles promptly even when another app
                    // deletes a file without ever sending a Shell notification.
                    item.ConfirmedDeletion = true;
                    item.Handle.Dispose(); item.Handle = null;
                }
                if ((now - pair.Value.Seen).TotalSeconds > 60) expired.Add(pair.Key);
            }
            foreach (string path in expired) RemoveCached(path);
            expired.Clear();
            foreach (KeyValuePair<string, DateTime> pair in moved)
                if ((now - pair.Value).TotalSeconds > 2) expired.Add(pair.Key);
            foreach (string path in expired) moved.Remove(path);
        }

        private void RefreshRoots()
        {
            nextRoots = DateTime.UtcNow.AddSeconds(60);
            List<string> latest = new List<string>();
            AddRoot(latest, KnownFolder(new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641")));
            AddRoot(latest, KnownFolder(new Guid("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25")));
            AddRoot(latest, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddRoot(latest, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            if (String.Join("|", latest.ToArray()) == String.Join("|", roots.ToArray()) && registrations.Count > 0) return;
            roots.Clear(); roots.AddRange(latest);
            Register();
        }

        private void Register()
        {
            foreach (uint registration in registrations) SHChangeNotifyDeregister(registration);
            registrations.Clear();
            foreach (string path in roots)
            {
                IntPtr pidl;
                uint attributes;
                if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attributes) < 0) continue;
                try
                {
                    SHChangeNotifyEntry entry = new SHChangeNotifyEntry { Pidl = pidl, Recursive = false };
                    // Shell-level only: interrupt-level reports cannot distinguish move-outs.
                    uint registration = SHChangeNotifyRegister(hwnd, 0x8002, Delete | RemoveDirectory | RenameItem | RenameFolder, MessageId, 1, ref entry);
                    if (registration != 0) registrations.Add(registration);
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
        }

        private void Connect()
        {
            IntPtr windows = IntPtr.Zero, dispatch = IntPtr.Zero, provider = IntPtr.Zero;
            IntPtr browser = IntPtr.Zero, shellView = IntPtr.Zero;
            try
            {
                Guid clsid = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
                Guid iid = new Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85");
                Check(CoCreateInstance(ref clsid, IntPtr.Zero, 23, ref iid, out windows));
                object location = 0, empty = null;
                int desktopHwnd;
                Check(Call<FindWindowDelegate>(windows, 15)(windows, ref location, ref empty, 8, out desktopHwnd, 1, out dispatch));
                if (dispatch == IntPtr.Zero) throw new InvalidOperationException("Explorer desktop is not ready");
                iid = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
                Check(Marshal.QueryInterface(dispatch, ref iid, out provider));
                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                iid = new Guid("000214E2-0000-0000-C000-000000000046");
                Check(Call<QueryServiceDelegate>(provider, 3)(provider, ref service, ref iid, out browser));
                Check(Call<PointerDelegate>(browser, 15)(browser, out shellView));
                Check(Call<PointerDelegate>(shellView, 3)(shellView, out viewWindow)); // IOleWindow.GetWindow
                iid = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
                Check(Marshal.QueryInterface(shellView, ref iid, out view));
                scanIndex = 0;
                Register(); // Reconnect to the notification broker after Explorer restarts.
            }
            finally
            {
                Release(ref shellView); Release(ref browser); Release(ref provider);
                Release(ref dispatch); Release(ref windows);
            }
        }

        private int GetIconSize()
        {
            IntPtr view2 = IntPtr.Zero;
            Guid iid = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            try
            {
                if (Marshal.QueryInterface(view, ref iid, out view2) >= 0)
                {
                    int mode, size;
                    if (Call<ViewSizeDelegate>(view2, 36)(view2, out mode, out size) >= 0 && size >= 16 && size <= 256) return size;
                }
                return SystemInformation.IconSize.Width;
            }
            finally { Release(ref view2); }
        }

        private void Disconnect() { Release(ref view); viewWindow = IntPtr.Zero; scanIndex = 0; }
        private void RemoveCached(string path)
        {
            Entry item;
            if (!cache.TryGetValue(path, out item)) return;
            cache.Remove(path);
            if (item.Icon != null) item.Icon.Dispose();
            if (item.Handle != null) item.Handle.Dispose();
        }
        private bool IsDirectDesktopItem(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            string parent;
            try { parent = Normalize(Path.GetDirectoryName(path)); }
            catch { return false; }
            foreach (string root in roots)
                if (String.Equals(root, parent, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private static bool IsRecyclePath(string path)
        { return !String.IsNullOrEmpty(path) && path.IndexOf("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase) >= 0; }
        private static string Normalize(string path)
        {
            if (String.IsNullOrEmpty(path)) return "";
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return ""; }
        }
        private static void AddRoot(List<string> list, string path)
        {
            path = Normalize(path);
            if (path.Length == 0) return;
            foreach (string item in list) if (String.Equals(item, path, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(path);
        }
        private static string KnownFolder(Guid id)
        {
            IntPtr value = IntPtr.Zero;
            try { return SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out value) >= 0 ? Marshal.PtrToStringUni(value) : null; }
            finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
        }
        private static string PathFromPidl(IntPtr pidl)
        {
            if (pidl == IntPtr.Zero) return null;
            IntPtr value = IntPtr.Zero;
            try { return SHGetNameFromIDList(pidl, 0x80058000, out value) >= 0 ? Normalize(Marshal.PtrToStringUni(value)) : null; }
            finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
        }
        private static Bitmap ReadIcon(string path)
        {
            SHFILEINFO info = new SHFILEINFO();
            try
            {
                uint attributes = Directory.Exists(path) ? 0x10U : 0x80U;
                if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x100) != IntPtr.Zero && info.Icon != IntPtr.Zero)
                { using (Icon icon = Icon.FromHandle(info.Icon)) return icon.ToBitmap(); }
            }
            catch { }
            finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
            return SystemIcons.Application.ToBitmap();
        }

        private static SafeFileHandle OpenTrackingHandle(string path)
        {
            // No content access, no locks on rename/deletion. Open the link itself
            // for reparse points instead of monitoring its target.
            SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            handle.Dispose(); return null;
        }

        private static bool WasDeleted(Entry item)
        {
            if (item.ConfirmedDeletion) return true;
            if (item.Handle == null || item.Handle.IsClosed || item.Handle.IsInvalid) return false;
            FILE_STANDARD_INFO info;
            if (GetFileInformationByHandleEx(item.Handle, 1, out info, (uint)Marshal.SizeOf(typeof(FILE_STANDARD_INFO))) && info.DeletePending)
                return true;
            StringBuilder path = new StringBuilder(512);
            uint length = GetFinalPathNameByHandle(item.Handle, path, (uint)path.Capacity, 0);
            if (length >= path.Capacity && length < 32768)
            {
                path = new StringBuilder((int)length + 1);
                length = GetFinalPathNameByHandle(item.Handle, path, (uint)path.Capacity, 0);
            }
            if (length == 0)
            {
                int error = Marshal.GetLastWin32Error();
                return error == 2 || error == 3; // The name has already been removed.
            }
            if (length >= path.Capacity) return false;
            string current = path.ToString();
            if (current.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) current = "\\\\" + current.Substring(8);
            else if (current.StartsWith("\\\\?\\", StringComparison.Ordinal)) current = current.Substring(4);
            current = Normalize(current);
            return IsRecyclePath(current) || String.Equals(current, item.Path, StringComparison.OrdinalIgnoreCase);
        }
        private static T Call<T>(IntPtr instance, int slot) where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }
        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        private static void Release(ref IntPtr pointer)
        { if (pointer != IntPtr.Zero) { Marshal.Release(pointer); pointer = IntPtr.Zero; } }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (uint registration in registrations) SHChangeNotifyDeregister(registration);
            registrations.Clear();
            Disconnect();
            foreach (Entry item in cache.Values)
            {
                if (item.Icon != null) item.Icon.Dispose();
                if (item.Handle != null) item.Handle.Dispose();
            }
            cache.Clear(); pending.Clear(); moved.Clear(); Deleted = null;
        }

        // These are the documented COM vtable slots. Raw interface pointers avoid an
        // Office/SHDocVw interop dependency and keep deployment a single C# build.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FindWindowDelegate(IntPtr self, [In, MarshalAs(UnmanagedType.Struct)] ref object location, [In, MarshalAs(UnmanagedType.Struct)] ref object root, int windowClass, out int window, int options, out IntPtr dispatch);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryServiceDelegate(IntPtr self, ref Guid service, ref Guid iid, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PointerDelegate(IntPtr self, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ItemCountDelegate(IntPtr self, uint flags, out int count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ItemDelegate(IntPtr self, int index, out IntPtr pidl);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PositionDelegate(IntPtr self, IntPtr pidl, out POINT point);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PointDelegate(IntPtr self, out POINT point);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ViewSizeDelegate(IntPtr self, out int mode, out int size);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SHChangeNotifyEntry { internal IntPtr Pidl; [MarshalAs(UnmanagedType.Bool)] internal bool Recursive; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SHFILEINFO
        { internal IntPtr Icon; internal int IconIndex; internal uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName; }
        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr result);
        [DllImport("shell32.dll")] private static extern uint SHChangeNotifyRegister(IntPtr window, int sources, int events, int message, int entries, ref SHChangeNotifyEntry entry);
        [DllImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHChangeNotifyDeregister(uint registration);
        [DllImport("shell32.dll")] private static extern IntPtr SHChangeNotification_Lock(IntPtr change, uint processId, out IntPtr pidls, out int events);
        [DllImport("shell32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHChangeNotification_Unlock(IntPtr notification);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, IntPtr bind, out IntPtr pidl, uint attributes, out uint found);
        [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
        [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, uint type, out IntPtr name);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct FILE_STANDARD_INFO
        {
            internal long AllocationSize, EndOfFile;
            internal uint NumberOfLinks;
            [MarshalAs(UnmanagedType.U1)] internal bool DeletePending;
            [MarshalAs(UnmanagedType.U1)] internal bool Directory;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int type, out FILE_STANDARD_INFO information, uint size);
    }
}
