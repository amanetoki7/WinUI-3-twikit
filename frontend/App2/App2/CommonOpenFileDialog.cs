using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace WinUI3Twikit
{
    /// <summary>
    /// Win32 コモンダイアログ (IFileOpenDialog) の薄いラッパ。
    /// FileOpenPicker と違い、表示名付きで複数拡張子を1行にまとめられる。
    /// </summary>
    internal static class CommonOpenFileDialog
    {
        private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);

        /// <summary>
        /// ファイルを開くダイアログを表示する。キャンセル時は空配列。
        /// </summary>
        public static IReadOnlyList<string> Show(
            IntPtr ownerHwnd,
            IReadOnlyList<(string Name, string Spec)> filters,
            bool multiSelect = true,
            string? title = null,
            string? initialDirectory = null)
        {
            if (filters is null || filters.Count == 0)
                throw new ArgumentException("At least one filter is required.", nameof(filters));

            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                uint options =
                    (uint)FOS.FOS_FORCEFILESYSTEM |
                    (uint)FOS.FOS_PATHMUSTEXIST |
                    (uint)FOS.FOS_FILEMUSTEXIST;
                if (multiSelect)
                    options |= (uint)FOS.FOS_ALLOWMULTISELECT;

                dialog.SetOptions(options);

                var specs = new COMDLG_FILTERSPEC[filters.Count];
                for (var i = 0; i < filters.Count; i++)
                {
                    specs[i] = new COMDLG_FILTERSPEC
                    {
                        pszName = filters[i].Name,
                        pszSpec = filters[i].Spec
                    };
                }

                dialog.SetFileTypes((uint)specs.Length, specs);
                dialog.SetFileTypeIndex(1);

                if (!string.IsNullOrWhiteSpace(title))
                    dialog.SetTitle(title);

                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    var shellItemIid = typeof(IShellItem).GUID;
                    var hrFolder = SHCreateItemFromParsingName(
                        initialDirectory,
                        IntPtr.Zero,
                        ref shellItemIid,
                        out var folder);
                    if (hrFolder == 0 && folder is not null)
                    {
                        try
                        {
                            dialog.SetDefaultFolder(folder);
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(folder);
                        }
                    }
                }

                var hr = dialog.Show(ownerHwnd);
                if (hr == HRESULT_CANCELLED)
                    return [];
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                dialog.GetResults(out var results);
                try
                {
                    results.GetCount(out var count);
                    var paths = new List<string>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        results.GetItemAt(i, out var item);
                        try
                        {
                            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var psz);
                            try
                            {
                                var path = Marshal.PtrToStringUni(psz);
                                if (!string.IsNullOrEmpty(path))
                                    paths.Add(path);
                            }
                            finally
                            {
                                if (psz != IntPtr.Zero)
                                    Marshal.FreeCoTaskMem(psz);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(item);
                        }
                    }

                    return paths;
                }
                finally
                {
                    Marshal.ReleaseComObject(results);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        #region COM interop

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszSpec;
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000,
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_PATHMUSTEXIST = 0x00000800,
            FOS_FILEMUSTEXIST = 0x00001000,
            FOS_ALLOWMULTISELECT = 0x00000200,
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW
        {
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport]
        [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
            void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
            void GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid, out IntPtr ppv);
            void GetAttributes(int dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
            void EnumItems(out IntPtr ppenumShellItems);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        /// <summary>
        /// IFileOpenDialog は IFileDialog を継承するため、メソッド順を COM vtable どおりに並べる。
        /// </summary>
        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // IModalWindow
            [PreserveSig]
            int Show(IntPtr parent);

            // IFileDialog
            void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName(out IntPtr pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);

            // IFileOpenDialog
            void GetResults(out IShellItemArray ppenum);
            void GetSelectedItems(out IShellItemArray ppsai);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        #endregion
    }
}
